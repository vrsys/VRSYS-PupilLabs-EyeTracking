using PupilLabs;
using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;


namespace VRSYS.PupilLabs
{


    /// <summary>
    /// Runs a fast 5-point (centre, top-left, top-right, bottom-left, bottom-right) gaze calibration
    /// intended to be run before every trial block. Reuses the Wahba rotation solver shipped with
    /// com.pupil-labs.neon-xr.core (PupilLabs.WahbaPoseSolver) to fit a single corrective rotation that
    /// maps the tracker's raw, sensor-space gaze direction onto known world-space target directions, then
    /// applies it via GazeDataProvider.SetGazeOrigin so every downstream consumer (GazeRay,
    /// EyeTrackingUser, GazeDataLogger, ...) benefits without further changes.
    /// </summary>
    public class GazeCalibrationController : MonoBehaviour
    {
        private enum CalibrationPoint
        {
            Center,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        // Centre first (lets the participant settle), then the four corners.
        private static readonly CalibrationPoint[] PresentationOrder =
        {
            CalibrationPoint.Center,
            CalibrationPoint.TopLeft,
            CalibrationPoint.TopRight,
            CalibrationPoint.BottomLeft,
            CalibrationPoint.BottomRight
        };



        public readonly struct CalibrationResult
        {
            public bool Solved { get; }
            public bool Applied { get; }
            public float ErrorDegrees { get; }
            public int SampleCount { get; }

            public CalibrationResult(bool solved, bool applied, float errorDegrees, int sampleCount)
            {
                Solved = solved;
                Applied = applied;
                ErrorDegrees = errorDegrees;
                SampleCount = sampleCount;
            }
        }

        [Header("Scene")]
        [Tooltip("Root game object of scene which can be optionally hidden during calibration")]
        [SerializeField] private GameObject sceneRoot;

        [Header("Calibration Target Array")]
        [Tooltip("Defines the centre and the local right/up axes of the 5-point pattern")]
        [SerializeField] private Transform arrayOrigin;

        [SerializeField, Min(0f)] private float horizontalSpacing = 0.5f;
        [SerializeField, Min(0f)] private float verticalSpacing = 0.4f;

        [Header("Marker")]
        [Tooltip("Visual marker moved to each calibration point in turn.")]
        [SerializeField] private Transform marker;

        [Header("Gaze Source")]
        [Tooltip("The tracked head/camera transform gaze directions are expressed relative to.")]
        [SerializeField] private Transform headTransform;


        [Header("Gaze Cursor")]
        [Tooltip("Visual marker moved to gaze point for validation routine.")]
        [SerializeField] private Transform gazeCursor;

        [SerializeField] private WahbaPoseSolver poseSolver;




        [Header("Timing")]

        [Tooltip("Time between showing the start of the calibration routine and the first target appearing.")]
        [SerializeField, Min(0f)] private float startUpDuration = 2f;

        [Tooltip("Ignored time after the marker jumps to a new point, so the saccade lands before " +
                 "samples are collected.")]
        [SerializeField, Min(0f)] private float leadInDuration = 1f;

        [SerializeField, Min(0.05f)] private float fixationDuration = 2f;
        [SerializeField, Min(0f)] private float interPointPause = 2f;

        [Header("Quality Gate")]
        [Tooltip("If the solved average angular error exceeds this many degrees, the correction is " +
                 "logged but not applied, since it is likely worse than whatever calibration was already " +
                 "active.")]
        [SerializeField, Min(0f)] private float maxAcceptableErrorDegrees = 2.5f;

        [SerializeField, Min(1)] private int minSamplesPerTarget = 20;

        [Tooltip("The maximum (smoothed) angular speed that is allowed for samples to be collected (degrees per second)")]
        [SerializeField, Min(0f)] private float maxAngularMovementSpeed = 10f;

        [Tooltip("The maximum angle between target and gaze direction that is allowed for samples to be collected (degrees)")]
        [SerializeField, Min(0f)] private float maxAngleToTarget = 10f;

        private Vector3 lastGazeDirection;
        private bool hadValidGazeDirectionSample = false;
        private float[] angularSpeedBuffer = new float[16];
        private int angularBufferReadPos = 0;
        private int angularSpeedAveragingWindow = 4;

        public event Action<CalibrationResult> CalibrationCompleted;

        public bool IsCalibrating { get; private set; }

        private NeonGazeDataProvider gazeProvider;
        private Coroutine calibrationCoroutine;
        private Coroutine validationCoroutine;
        private Coroutine positionCursorCoroutine;


        #region MonoBehaviour Callbacks

        private void Start()
        {
            HideMarker();
            HideCursor();
            Array.Fill(angularSpeedBuffer, -1f);
        }
        private void OnDestroy()
        {
            CancelCalibration();
            CancelValidation();
        }

        #endregion


        #region Calibration Methods


        /// <summary>
        /// Starts the calibration sequence. Safe to call repeatedly (e.g. once before every trial block);
        /// each run clears any samples left over from a previous run.
        /// </summary>
        public void StartCalibration()
        {
            if (IsCalibrating)
            {
                Debug.LogWarning(
                    "GazeCalibrationController.StartCalibration called while already calibrating.",
                    this);
                return;
            }

            calibrationCoroutine = StartCoroutine(RunCalibration());
        }

        public void CancelCalibration()
        {
            if (calibrationCoroutine != null)
            {
                StopCoroutine(calibrationCoroutine);
                calibrationCoroutine = null;
            }

            IsCalibrating = false;
            HideMarker();
            ShowScene();
        }





        private IEnumerator RunCalibration()
        {
            if (!ValidateReferences())
            {
                CalibrationCompleted?.Invoke(new CalibrationResult(false, false, float.PositiveInfinity, 0));
                yield break;
            }

            IsCalibrating = true;

            yield return WaitForGazeProvider();

            if (gazeProvider == null)
            {
                Debug.LogError("GazeCalibrationController: no active Neon gaze provider; aborting calibration.", this);
                IsCalibrating = false;
                calibrationCoroutine = null;
                CalibrationCompleted?.Invoke(new CalibrationResult(false, false, float.PositiveInfinity, 0));
                yield break;
            }

            poseSolver.Clear();

            if (startUpDuration > 0f)
            {
                yield return new WaitForSeconds(startUpDuration);
            }


            ShowMarker();
            HideScene();


            foreach (CalibrationPoint point in PresentationOrder)
            {
                Vector3 targetPosition = GetTargetPosition(point);
                MoveMarkerTo(targetPosition);

                int samplesForTarget;
                do
                {
                    if (leadInDuration > 0f)
                    {
                        yield return new WaitForSeconds(leadInDuration);
                    }

                    int startSampleCount = poseSolver.SampleCount;

                    yield return CollectSamples(targetPosition, fixationDuration);

                    samplesForTarget = poseSolver.SampleCount - startSampleCount;

                    if (samplesForTarget < minSamplesPerTarget)
                    {
                        Debug.LogWarning(
                            $"GazeCalibrationController: only found {samplesForTarget} samples for target " +
                            $"point (need {minSamplesPerTarget}); retrying point.",
                            this);
                    }
                } while (samplesForTarget < minSamplesPerTarget);

                Debug.Log($"GazeCalibrationController: found {samplesForTarget} samples for target point", this);

                if (interPointPause > 0f)
                {
                    yield return new WaitForSeconds(interPointPause);
                }
            }

            HideMarker();

            int sampleCount = poseSolver.SampleCount;
            Task solveTask = poseSolver.Solve();
            yield return new WaitUntil(() => solveTask.IsCompleted);

            bool solved = solveTask.Exception == null;

            // TODO pose solver does not correctly report error so this will be 0
            // bug reported: https://github.com/pupil-labs/neon-xr/issues/5
            float errorDegrees = solved ? poseSolver.Error : float.PositiveInfinity;

            bool applied = false;

            if (!solved)
            {
                Debug.LogError(
                    $"GazeCalibrationController: Wahba solve failed: {solveTask.Exception}",
                    this);
            }
            else if (errorDegrees <= maxAcceptableErrorDegrees)
            {
                Pose currentOrigin = gazeProvider.GazeOrigin;
                gazeProvider.SetGazeOrigin(currentOrigin.position, poseSolver.Solution.rotation.eulerAngles);
                applied = true;
                Debug.Log(
                    $"GazeCalibrationController: applied correction, average error {errorDegrees:F2} deg " +
                    $"over {sampleCount} samples.",
                    this);
            }
            else
            {
                Debug.LogWarning(
                    $"GazeCalibrationController: solved error {errorDegrees:F2} deg exceeds the " +
                    $"{maxAcceptableErrorDegrees:F2} deg threshold; keeping the previous calibration.",
                    this);
            }

            IsCalibrating = false;
            calibrationCoroutine = null;
            ShowScene();
            CalibrationCompleted?.Invoke(new CalibrationResult(solved, applied, errorDegrees, sampleCount));
        }

        private IEnumerator CollectSamples(Vector3 targetWorldPosition, float duration)
        {
            double endTime = Time.realtimeSinceStartupAsDouble + duration;
            hadValidGazeDirectionSample = false;

            while (Time.realtimeSinceStartupAsDouble < endTime)
            {
                // EyeStateAvailable is used as a basic sample-quality gate. Tighten with an
                // eyelid-openness threshold (RawEyelid.eyelidApertureLeft/Right) if blinks turn out to
                // contaminate samples in practice.
                if (gazeProvider.EyeStateAvailable)
                {
                    Vector3 referencePoint = headTransform.InverseTransformPoint(targetWorldPosition);
                    Vector3 observedDirection = gazeProvider.RawGazeDir;

                    if (hadValidGazeDirectionSample)
                    {
                        float angle = Vector3.Angle(observedDirection, lastGazeDirection);
                        float angularSpeed = angle / Time.deltaTime;
                        float smoothedAngularSpeed = GetAveragedAngularSpeed(angularSpeed);

                        if (smoothedAngularSpeed < maxAngularMovementSpeed && angle < maxAngleToTarget)
                            poseSolver.AddSample(referencePoint, observedDirection);

                    }
                    else
                    {

                        hadValidGazeDirectionSample = true;
                        Array.Fill(angularSpeedBuffer, -1f);
                    }

                    lastGazeDirection = gazeProvider.RawGazeDir;

                }
                else
                {
                    hadValidGazeDirectionSample = false;
                }

                yield return null;
            }
        }

        #endregion

        #region Validation Methods

        public void StartValidation()
        {
            if (IsCalibrating)
            {
                Debug.LogWarning("GazeCalibrationController.StartValidation called while calibrating.", this);
                return;
            }

            validationCoroutine = StartCoroutine(RunValidation());
        }
        public void CancelValidation()
        {
            if (validationCoroutine != null)
            {
                StopCoroutine(validationCoroutine);
                validationCoroutine = null;
            }

            StopPositionCursor();

            HideMarker();
            ShowScene();
        }

        private IEnumerator RunValidation()
        {
            if (!ValidateReferences())
            {
                yield break;
            }

            if (gazeCursor == null)
            {
                Debug.LogError("GazeCalibrationController requires a gaze cursor to show where users are looking.", this);
                yield break;
            }

            yield return WaitForGazeProvider();

            if (gazeProvider == null)
            {
                Debug.LogError("GazeCalibrationController: no active Neon gaze provider; aborting validation.", this);
                validationCoroutine = null;
                yield break;
            }

            positionCursorCoroutine = StartCoroutine(PositionCursor());

            if (startUpDuration > 0f)
            {
                yield return new WaitForSeconds(startUpDuration);
            }

            ShowMarker();
            HideScene();


            for (int i = 0; i < PresentationOrder.Length; i++)
            {
                Vector3 targetPosition = arrayOrigin.position +
                    arrayOrigin.right * horizontalSpacing * UnityEngine.Random.Range(-1f, 1f) +
                    arrayOrigin.up * verticalSpacing * UnityEngine.Random.Range(-1f, 1f);
                MoveMarkerTo(targetPosition);

                if (interPointPause > 0f)
                {
                    yield return new WaitForSeconds(interPointPause);
                }
            }

            StopPositionCursor();

            HideMarker();

            validationCoroutine = null;
            ShowScene();
        }

        /// <summary>
        /// Continuously projects the (calibration-corrected) gaze ray onto the plane defined by
        /// arrayOrigin's right/up axes - the same plane the calibration targets sit on - and moves
        /// gazeCursor to the intersection, so the participant/conductor can see where the corrected
        /// gaze estimate lands relative to the calibration array.
        /// </summary>
        private IEnumerator PositionCursor()
        {
            ShowCursor();

            Plane arrayPlane = new Plane(arrayOrigin.forward, arrayOrigin.position);

            while (true)
            {
                if (gazeProvider.EyeStateAvailable)
                {
                    Ray localGazeRay = gazeProvider.GazeRay;
                    Ray worldGazeRay = new Ray(
                        headTransform.TransformPoint(localGazeRay.origin),
                        headTransform.TransformDirection(localGazeRay.direction));

                    if (arrayPlane.Raycast(worldGazeRay, out float distance) && distance >= 0f)
                    {
                        gazeCursor.position = worldGazeRay.GetPoint(distance);
                    }
                }

                yield return null;
            }
        }

        private void StopPositionCursor()
        {
            if (positionCursorCoroutine != null)
            {
                StopCoroutine(positionCursorCoroutine);
                positionCursorCoroutine = null;
            }

            HideCursor();
        }

        #endregion

        #region Eye-tracker connection functions

        private IEnumerator WaitForGazeProvider()
        {
            yield return new WaitUntil(() => NeonDeviceConnector.Instance != null);

            if (NeonDeviceConnector.Instance.IsGazeDataProviderActive)
            {
                gazeProvider = NeonDeviceConnector.Instance.GazeDataProvider as NeonGazeDataProvider;
                yield break;
            }

            bool activated = false;
            NeonGazeDataProvider activatedProvider = null;

            void HandleActivated(NeonGazeDataProvider provider)
            {
                activatedProvider = provider;
                activated = true;
            }

            NeonDeviceConnector.Instance.GazeProviderActivated += HandleActivated;
            yield return new WaitUntil(() => activated);
            NeonDeviceConnector.Instance.GazeProviderActivated -= HandleActivated;

            gazeProvider = activatedProvider;
        }

        #endregion

        #region Utility Methods

        private float GetAveragedAngularSpeed(float latestAngularSpeed)
        {
            angularSpeedBuffer[angularBufferReadPos] = latestAngularSpeed;

            float angSpeed = 0f;
            int samples = 0;
            int readPos = angularBufferReadPos + angularSpeedBuffer.Length;
            for (int i = 0; i < angularSpeedAveragingWindow; i++)
            {
                float s = angularSpeedBuffer[readPos % angularSpeedBuffer.Length];
                if (s >= 0f)
                {
                    angSpeed += s;
                    ++samples;
                }

                --readPos;
            }

            if (++angularBufferReadPos >= angularSpeedBuffer.Length)
                angularBufferReadPos = 0;

            return angSpeed / samples;

        }


        private Vector3 GetTargetPosition(CalibrationPoint point)
        {
            Vector3 centre = arrayOrigin.position;
            Vector3 right = arrayOrigin.right;
            Vector3 up = arrayOrigin.up;

            return point switch
            {
                CalibrationPoint.Center => centre,
                CalibrationPoint.TopLeft => centre - right * horizontalSpacing + up * verticalSpacing,
                CalibrationPoint.TopRight => centre + right * horizontalSpacing + up * verticalSpacing,
                CalibrationPoint.BottomLeft => centre - right * horizontalSpacing - up * verticalSpacing,
                CalibrationPoint.BottomRight => centre + right * horizontalSpacing - up * verticalSpacing,
                _ => centre
            };
        }

        private bool ValidateReferences()
        {
            if (arrayOrigin == null)
            {
                Debug.LogError("GazeCalibrationController requires an Array Origin transform to position gaze targets", this);
                return false;
            }

            if (headTransform == null)
            {
                Debug.LogError("GazeCalibrationController requires a Head Transform reference.", this);
                return false;
            }

            if (poseSolver == null)
            {
                Debug.LogError("GazeCalibrationController requires a WahbaPoseSolver reference.", this);
                return false;
            }

            if (marker == null)
            {
                Debug.LogError("GazeCalibrationController requires a Marker reference for users to fixate on.", this);
                return false;
            }


            return true;
        }

        #endregion

        #region Game Object visibility methods

        private void ShowMarker()
        {
            if (marker != null)
            {
                marker.gameObject.SetActive(true);
            }
        }

        private void HideMarker()
        {
            if (marker != null)
            {
                marker.gameObject.SetActive(false);
            }
        }
        private void MoveMarkerTo(Vector3 worldPosition)
        {
            if (marker != null)
            {
                marker.position = worldPosition;
            }
        }


        private void ShowCursor()
        {
            if (gazeCursor != null)
            {
                gazeCursor.gameObject.SetActive(true);
            }
        }

        private void HideCursor()
        {
            if (gazeCursor != null)
            {
                gazeCursor.gameObject.SetActive(false);
            }
        }

        private void ShowScene()
        {
            if (sceneRoot != null)
            {
                sceneRoot.SetActive(true);
            }
        }

        private void HideScene()
        {
            if (sceneRoot != null)
            {
                sceneRoot.gameObject.SetActive(false);
            }
        }

        #endregion
    }

}