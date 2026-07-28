using UnityEngine;
using UnityEngine.InputSystem;

namespace VRSYS.PupilLabs
{
    public class CalibrationTrigger : MonoBehaviour
    {

        [SerializeField] private InputAction _triggerCalibrationAction;
        [SerializeField] private InputAction _triggerValidationAction;

        private GazeCalibrationController _gazeCalibrationController;

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            _gazeCalibrationController = GetComponent<GazeCalibrationController>();

            _triggerCalibrationAction.Enable();
            _triggerValidationAction.Enable();

        }

        // Update is called once per frame
        void Update()
        {
            if (_triggerCalibrationAction.WasPressedThisFrame())
            {
                _gazeCalibrationController.StartCalibration();
            }

            if (_triggerValidationAction.WasPressedThisFrame())
            {
                _gazeCalibrationController.StartValidation();
            }
        }

    }
}