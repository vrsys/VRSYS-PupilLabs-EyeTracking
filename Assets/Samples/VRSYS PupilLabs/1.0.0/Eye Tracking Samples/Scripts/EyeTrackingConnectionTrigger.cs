using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VRSYS.PupilLabs.Samples
{
    public class EyeTrackingConnectionTrigger : MonoBehaviour
    {
        #region Properties

        [SerializeField] private InputAction _triggerConnectionAction;
        [SerializeField] private int _deviceIndex = 0;

        private EyeTrackingUser _eyeTrackingUser;

        #endregion

        #region MonoBehaviour Methods

        private void Start()
        {
            if (!GetComponentInParent<NetworkObject>().IsOwner)
            {
                Destroy(this);
                return;
            }

            _triggerConnectionAction.Enable();
            _eyeTrackingUser = GetComponentInParent<EyeTrackingUser>();
        }

        private void Update()
        {
            if(_triggerConnectionAction.WasPressedThisFrame())
                _eyeTrackingUser.Connect(_deviceIndex);
        }

        #endregion
    }
}
