using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PupilLabs;
using UnityEngine;
using VRSYS.PupilLabs;
using Stopwatch = System.Diagnostics.Stopwatch;

// Subscribes directly to RTSPClient.GazeDataReceived (the raw ~200 Hz tracker callback) instead of
// going through NeonGazeDataProvider.Update()/EyeTrackingUser, so every packet gets its own row
// instead of being throttled down to Unity's frame rate.
public class GazeDataLogger : MonoBehaviour
{
    private const string GazeCsvHeader =
        "unity_time,device_timestamp_ms,rtcp_synchronized,unity_receive_time,worn," +
        "gaze_x,gaze_y,raw_gaze_dir_x,raw_gaze_dir_y,raw_gaze_dir_z," +
        "eye_state_available,pupil_diameter_left,pupil_diameter_right," +
        "eyeball_center_left_x,eyeball_center_left_y,eyeball_center_left_z," +
        "optical_axis_left_x,optical_axis_left_y,optical_axis_left_z," +
        "eyeball_center_right_x,eyeball_center_right_y,eyeball_center_right_z," +
        "optical_axis_right_x,optical_axis_right_y,optical_axis_right_z," +
        "eyelid_available," +
        "eyelid_angle_top_left,eyelid_angle_bottom_left,eyelid_aperture_left," +
        "eyelid_angle_top_right,eyelid_angle_bottom_right,eyelid_aperture_right";

    [Tooltip("Same DataStorage used by NeonGazeDataProvider - needed to undistort the raw gaze " +
             "point into a direction (CameraIntrinsics), the same way NeonGazeDataProvider does.")]
    [SerializeField] private DataStorage _dataStorage;

    [Tooltip("Same DeviceManager used by NeonGazeDataProvider - polled periodically to measure the " +
             "PC/tracker clock offset, which converts device_timestamp_ms into unity_record_time.")]
    [SerializeField] private DeviceManager _deviceManager;

    [Tooltip("How often to re-measure the clock offset while a session is open. Each estimate is " +
             "100 TCP round trips to the tracker's time echo port, so keep this infrequent - it only " +
             "needs to track slow clock drift, not re-establish the offset from scratch.")]
    [SerializeField] private float _timeOffsetPollIntervalSeconds = 60f;

    private readonly object _writerLock = new();
    private StreamWriter _writer;
    private string _participantId;

    private NeonGazeDataProvider _subscribedProvider;

    // Anchors a Unity real time to a Stopwatch tick count captured at the same instant, both on the
    // main thread. Stopwatch.GetTimestamp() itself has no thread affinity, so this lets the RTSP
    // callback thread compute a Unity-comparable timestamp without ever touching UnityEngine.Time.
    private long _anchorTicks;
    private double _anchorUnityTime;

    // Anchors the same instant to the tracker-side Unix clock (RTSPServiceWrapper.UnixTimeMs(), the
    // same clock DeviceManager.EstimateTimeOffset() measures against) so a PC-Unix-time value can be
    // converted into the Unity timeline below without any further uncertainty - both reads happen
    // back-to-back on the main thread, so there is no network latency to account for here.
    private long _anchorUtcMs;

    private CancellationTokenSource _offsetPollCts;
    // PC Unix time minus tracker Unix time, from DeviceManager.EstimateTimeOffset(). Written from the
    // polling task (main thread) and read from the RTSP callback thread, hence Interlocked/volatile
    // instead of a lock - it is only ever replaced wholesale, never read-modify-written.
    private long _latestPcMinusDeviceOffsetMs;
    private volatile bool _haveTimeOffset;

    public bool IsRecording => _writer != null;

    private void Awake()
    {
        _anchorTicks = Stopwatch.GetTimestamp();
        _anchorUnityTime = Time.realtimeSinceStartupAsDouble;
        _anchorUtcMs = RTSPServiceWrapper.UnixTimeMs();
    }

    private void OnEnable()
    {
        StartCoroutine(SubscribeWhenConnectorReady());
    }

    private void OnDisable()
    {
        if (NeonDeviceConnector.Instance != null)
            NeonDeviceConnector.Instance.GazeProviderActivated -= HandleActivated;

        UnsubscribeFromRTSPClient();
    }

    private IEnumerator SubscribeWhenConnectorReady()
    {
        yield return new WaitUntil(() => NeonDeviceConnector.Instance != null);

        if (NeonDeviceConnector.Instance.IsGazeDataProviderActive)
        {
            HandleActivated(NeonDeviceConnector.Instance.GazeDataProvider);
        }
        else
        {
            NeonDeviceConnector.Instance.GazeProviderActivated += HandleActivated;
        }
    }

    private void HandleActivated(GazeDataProvider provider)
    {
        if (provider is not NeonGazeDataProvider neonProvider)
        {
            Debug.LogWarning($"{GetType().Name} only supports {nameof(NeonGazeDataProvider)}.", this);
            return;
        }

        StartCoroutine(SubscribeWhenRTSPClientReady(neonProvider));
    }

    private IEnumerator SubscribeWhenRTSPClientReady(NeonGazeDataProvider provider)
    {
        yield return new WaitUntil(() => provider.RTSPClient != null);

        // Guards against double-subscription if the tracker reconnects and fires GazeProviderActivated again.
        UnsubscribeFromRTSPClient();

        _subscribedProvider = provider;
        _subscribedProvider.RTSPClient.GazeDataReceived += OnGazeDataReceived;
    }

    private void UnsubscribeFromRTSPClient()
    {
        if (_subscribedProvider != null && _subscribedProvider.RTSPClient != null)
            _subscribedProvider.RTSPClient.GazeDataReceived -= OnGazeDataReceived;

        _subscribedProvider = null;
    }

    public void BeginSession(string participantId)
    {
        if (_dataStorage == null)
        {
            Debug.LogWarning($"{GetType().Name} did not get a reference to DataStorage, so cannot calculate gaze direction for logging", this);

        }

        _participantId = participantId;

        // Started here (before any trial/training phase) rather than at StartRecording, so the offset
        // has already been estimated at least once by the time real trial blocks are logged.
        StartTimeOffsetPolling();
    }

    private void StartTimeOffsetPolling()
    {
        StopTimeOffsetPolling();

        if (_deviceManager == null)
        {
            Debug.LogWarning($"{GetType().Name} did not get a reference to DeviceManager, so cannot estimate the tracker's clock offset - unity_record_time will not be written.", this);
            return;
        }

        _offsetPollCts = new CancellationTokenSource();
        PollTimeOffsetLoop(_offsetPollCts.Token).Forget();
    }

    private void StopTimeOffsetPolling()
    {
        _offsetPollCts?.Cancel();
        _offsetPollCts?.Dispose();
        _offsetPollCts = null;
    }

    // Runs on the main thread - DeviceManager.EstimateTimeOffset manages its own TCP connection - so
    // it never touches the RTSP callback thread directly; OnGazeDataReceived only reads the result.
    private async Task PollTimeOffsetLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                long offset = await _deviceManager.EstimateTimeOffset(cancellationToken);
                Interlocked.Exchange(ref _latestPcMinusDeviceOffsetMs, offset);
                _haveTimeOffset = true;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Debug.LogWarning($"{GetType().Name}: time offset estimation failed: {e.Message}", this);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_timeOffsetPollIntervalSeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void StartRecording(int trialBlockNumber)
    {
        StopRecording();

        string directory = Path.Combine(Application.persistentDataPath, "StudyData");
        Directory.CreateDirectory(directory);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string filePath = Path.Combine(
            directory,
            $"p{_participantId}_block{trialBlockNumber}_{timestamp}_gaze.csv");

        lock (_writerLock)
        {
            _writer = new StreamWriter(filePath, append: false);
            _writer.WriteLine(GazeCsvHeader);
        }
    }

    public void StopRecording()
    {
        lock (_writerLock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    public void EndSession()
    {
        StopRecording();
        StopTimeOffsetPolling();
    }

    // Runs on the RTSP receive thread (native Live555 callback or the RTSPClientWs receive loop) -
    // do not call UnityEngine.Time, Transform, or any other main-thread-only API from here.
    private void OnGazeDataReceived(object sender, EventArgs e)
    {
        if (sender is not RTSPClient client)
            return;

        GazeData data = client.GazeData;

        // unity_receive_time is when this packet was processed here, on the RTSP callback thread -
        // not when the tracker actually captured it (see clustering/jitter discussion elsewhere).
        double unityReceiveTime = _anchorUnityTime + (Stopwatch.GetTimestamp() - _anchorTicks) / (double)Stopwatch.Frequency;
        bool eyeStateAvailable = data.type >= EtDataType.EyeStateGazeData;
        bool eyelidAvailable = data.type >= EtDataType.EyeStateEyelidGazeData;

        // unity_time is the best available estimate of when the tracker actually captured this
        // sample: device_timestamp_ms (the tracker's own RTCP-synchronized UTC clock) converted into
        // Unity's timeline via the measured PC/tracker offset (from DeviceManager.EstimateTimeOffset,
        // polled periodically - see PollTimeOffsetLoop) plus the UTC<->realtimeSinceStartup anchor
        // taken in Awake(). Falls back to unity_receive_time until RTCP sync and a first offset
        // estimate are both available.
        double unityTime = unityReceiveTime;
        if (data.rtcpSynchronized && _haveTimeOffset)
        {
            long offsetMs = Interlocked.Read(ref _latestPcMinusDeviceOffsetMs);
            double pcUtcMs = data.timestampMs + offsetMs;
            unityTime = _anchorUnityTime + (pcUtcMs - _anchorUtcMs) / 1000.0;
        }

        // Same undistortion NeonGazeDataProvider.Update() applies to get its (untransformed) rawGazeDir -
        // CameraUtils.ImgPointToDir is pure math (no UnityEngine.Time/Transform), safe to call here.
        string rawGazeDirX = string.Empty, rawGazeDirY = string.Empty, rawGazeDirZ = string.Empty;
        if (_dataStorage != null && _dataStorage.CameraIntrinsics != null)
        {
            Vector3 rawGazeDir = CameraUtils.ImgPointToDir(
                data.gazePoint, _dataStorage.CameraIntrinsics.cameraMatrix, _dataStorage.CameraIntrinsics.distortionCoefficients);
            rawGazeDirX = rawGazeDir.x.ToString("F6", CultureInfo.InvariantCulture);
            rawGazeDirY = rawGazeDir.y.ToString("F6", CultureInfo.InvariantCulture);
            rawGazeDirZ = rawGazeDir.z.ToString("F6", CultureInfo.InvariantCulture);
        }

        lock (_writerLock)
        {
            if (_writer == null)
                return;

            _writer.WriteLine(string.Join(",",
                unityTime.ToString("F6", CultureInfo.InvariantCulture),
                data.timestampMs.ToString(CultureInfo.InvariantCulture),
                data.rtcpSynchronized ? "1" : "0",
                unityReceiveTime.ToString("F6", CultureInfo.InvariantCulture),
                data.worn ? "1" : "0",
                data.gazePoint.x.ToString("F4", CultureInfo.InvariantCulture),
                data.gazePoint.y.ToString("F4", CultureInfo.InvariantCulture),
                rawGazeDirX, rawGazeDirY, rawGazeDirZ,
                eyeStateAvailable ? "1" : "0",
                eyeStateAvailable ? data.eyeState.pupilDiameterLeft.ToString("F4", CultureInfo.InvariantCulture) : string.Empty,
                eyeStateAvailable ? data.eyeState.pupilDiameterRight.ToString("F4", CultureInfo.InvariantCulture) : string.Empty,
                FormatVector3(eyeStateAvailable, data.eyeState.eyeballCenterLeft),
                FormatVector3(eyeStateAvailable, data.eyeState.opticalAxisLeft),
                FormatVector3(eyeStateAvailable, data.eyeState.eyeballCenterRight),
                FormatVector3(eyeStateAvailable, data.eyeState.opticalAxisRight),
                eyelidAvailable ? "1" : "0",
                eyelidAvailable ? data.eyelid.eyelidAngleTopLeft.ToString("F4", CultureInfo.InvariantCulture) : string.Empty,
                eyelidAvailable ? data.eyelid.eyelidAngleBottomLeft.ToString("F4", CultureInfo.InvariantCulture) : string.Empty,
                eyelidAvailable ? data.eyelid.eyelidApertureLeft.ToString("F4", CultureInfo.InvariantCulture) : string.Empty,
                eyelidAvailable ? data.eyelid.eyelidAngleTopRight.ToString("F4", CultureInfo.InvariantCulture) : string.Empty,
                eyelidAvailable ? data.eyelid.eyelidAngleBottomRight.ToString("F4", CultureInfo.InvariantCulture) : string.Empty,
                eyelidAvailable ? data.eyelid.eyelidApertureRight.ToString("F4", CultureInfo.InvariantCulture) : string.Empty));
        }
    }

    private static string FormatVector3(bool available, Vector3 value)
    {
        if (!available)
            return ",,";

        return string.Join(",",
            value.x.ToString("F6", CultureInfo.InvariantCulture),
            value.y.ToString("F6", CultureInfo.InvariantCulture),
            value.z.ToString("F6", CultureInfo.InvariantCulture));
    }

    private void OnApplicationQuit()
    {
        EndSession();
    }

    private void OnDestroy()
    {
        EndSession();
    }
}
