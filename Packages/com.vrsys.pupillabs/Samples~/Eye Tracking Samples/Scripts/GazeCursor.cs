using Unity.Netcode;
using UnityEngine;

namespace VRSYS.PupilLabs.Samples
{
    public class GazeCursor : MonoBehaviour
    {
        #region Properties

        [SerializeField] private Transform _camera;
        [SerializeField] private Transform _cursor;
        [SerializeField, Range(0, 100)] private int _raycastDistance = 100;
        [SerializeField] private LayerMask _raycastLayers;

        private EyeTrackingUser _eyeTrackingUser;

        #endregion

        #region MonoBehaviour Methods

        private void Start()
        {
            if (GetComponentInParent<NetworkObject>() && !GetComponentInParent<NetworkObject>().IsOwner)
            {
                _cursor.gameObject.SetActive(false);
                return;
            }

            _eyeTrackingUser = GetComponentInParent<EyeTrackingUser>();
            _eyeTrackingUser.OnEyeTrackingData.AddListener(OnEyeTrackingData);
        }

        #endregion

        #region Private Methods

        private void OnEyeTrackingData(EyeTrackingData data)
        {
            Vector3 rayWorldOrigin = _camera.TransformPoint(data.GazeOrigin);
            Vector3 rayWorldDirection = _camera.TransformDirection(data.GazeDirection);

            Ray ray = new Ray(rayWorldOrigin, rayWorldDirection);

            if (Physics.Raycast(ray, out RaycastHit hit, _raycastDistance, _raycastLayers))
            {
                _cursor.gameObject.SetActive(true);
                _cursor.position = hit.point;
            }
            else
            {
                _cursor.gameObject.SetActive(false);
            }
        }

        #endregion
    }
}
