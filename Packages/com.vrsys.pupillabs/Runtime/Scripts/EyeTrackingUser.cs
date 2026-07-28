using System.Collections;
using PupilLabs;
using UnityEngine;
using UnityEngine.Events;

namespace VRSYS.PupilLabs
{

    public class EyeTrackingUser : MonoBehaviour
    {




        [Tooltip("If true, this component connects and subscribes to gaze data on its own " +
                 "(Start / OnEnable / OnDisable). Set to false when an external driver such as " +
                 "NetworkedEyeTrackingUser is responsible for calling Connect() / SubscribeToGazeData() / " +
                 "UnsubscribeFromGazeData() instead.")]
        [SerializeField] private bool _standalone = true;
        [SerializeField] private int _deviceIndex = -1;

        public UnityEvent<EyeTrackingData> OnEyeTrackingData = new();

        #region MonoBehaviour Callbacks
        private void Start()
        {
            if (_standalone)
                Connect(_deviceIndex);
        }

        private void OnEnable()
        {
            if (_standalone)
                SubscribeToGazeData();
        }

        private void OnDisable()
        {
            if (_standalone)
                UnsubscribeFromGazeData();
        }

        #endregion

        #region Eye-Tracker Connection Functions

        public void Connect(int deviceIndex)
        {
            NeonDeviceConnector.Instance.Connect(deviceIndex);
        }

        public void SubscribeToGazeData()
        {
            StartCoroutine(SubscribeWhenConnectorReady());
        }

        private IEnumerator SubscribeWhenConnectorReady()
        {
            yield return new WaitUntil(() =>
                NeonDeviceConnector.Instance != null);

            if (NeonDeviceConnector.Instance.IsGazeDataProviderActive)
            {
                HandleActivated(NeonDeviceConnector.Instance.GazeDataProvider);
            }
            else
            {
                NeonDeviceConnector.Instance.GazeProviderActivated += HandleActivated;
            }
        }

        public void UnsubscribeFromGazeData()
        {
            NeonDeviceConnector.Instance.GazeDataProvider.gazeDataReady.RemoveListener(OnGazeDataReady);
            NeonDeviceConnector.Instance.GazeProviderActivated -= HandleActivated;
        }

        private void HandleActivated(GazeDataProvider gazeDataProvider)
        {
            gazeDataProvider.gazeDataReady.AddListener(OnGazeDataReady);
        }

        #endregion

        private void OnGazeDataReady(GazeDataProvider provider)
        {
            EyeTrackingData data = new EyeTrackingData
            {
                PupilDiameterLeft = provider.EyeStateAvailable ? provider.EyeState.pupilDiameterLeft : float.NaN,
                PupilDiameterRight = provider.EyeStateAvailable ? provider.EyeState.pupilDiameterRight : float.NaN,
                EyeOpennessLeft = provider.EyelidAvailable ? provider.Eyelid.eyelidApertureLeft : float.NaN,
                EyeOpennessRight = provider.EyelidAvailable ? provider.Eyelid.eyelidApertureRight : float.NaN,
                GazeOrigin = provider.GazeRay.origin,
                GazeDirection = provider.GazeRay.direction,
                UpdateTime = AudioSettings.dspTime
            };

            OnEyeTrackingData.Invoke(data);
        }
    }

    public struct EyeTrackingData
    {
        public float PupilDiameterLeft { get; set; }
        public float PupilDiameterRight { get; set; }
        public float EyeOpennessLeft { get; set; }
        public float EyeOpennessRight { get; set; }
        public Vector3 GazeOrigin { get; set; }
        public Vector3 GazeDirection { get; set; }
        public double UpdateTime { get; set; }
    }
}
