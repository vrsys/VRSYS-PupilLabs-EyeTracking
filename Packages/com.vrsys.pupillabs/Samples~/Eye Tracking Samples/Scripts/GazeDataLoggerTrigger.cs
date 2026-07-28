using UnityEngine;
using UnityEngine.InputSystem;

namespace VRSYS.PupilLabs.Samples
{

    public class GazeDataLoggerTrigger : MonoBehaviour
    {

        [SerializeField] private InputAction _triggerRecordingAction;
        [SerializeField] private string _participantId = "0";

        private GazeDataLogger _gazeDataLogger;
        private TransformRecorder _headDataLogger;

        private int trialBlock = 0;


        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            _gazeDataLogger = GetComponent<GazeDataLogger>();
            _headDataLogger = GetComponent<TransformRecorder>();
            _gazeDataLogger.BeginSession(_participantId);
            _headDataLogger.BeginSession(_participantId);


            _triggerRecordingAction.Enable();

        }

        // Update is called once per frame
        void Update()
        {
            if (_triggerRecordingAction.WasPressedThisFrame())
            {
                if (_gazeDataLogger.IsRecording)
                {
                    _gazeDataLogger.StopRecording();
                }
                else
                {
                    _gazeDataLogger.StartRecording(trialBlock);
                }

                if (_headDataLogger.IsRecording)
                {
                    _headDataLogger.StopRecording();
                }
                else
                {
                    _headDataLogger.StartRecording(trialBlock++);
                }
            }
        }
    }
}