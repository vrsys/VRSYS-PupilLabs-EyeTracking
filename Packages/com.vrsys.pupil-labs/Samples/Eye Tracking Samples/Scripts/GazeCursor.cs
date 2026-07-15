using Unity.Netcode;
using UnityEngine;

namespace VRSYS.PupilLabs.Samples
{
    public class GazeCursor : MonoBehaviour
    {
        #region Properties

        [SerializeField] private GameObject _cursor;
        [SerializeField, Range(0, 100)] private int _raycastDistance = 100;
        [SerializeField] private LayerMask _raycastLayers;

        private EyeTrackingUser _eyeTrackingUser;

        #endregion

        #region MonoBehaviour Methods

        private void Start()
        {
            if (!GetComponentInParent<NetworkObject>().IsOwner)
            {
                _cursor.SetActive(false);
                return;
            }

            _eyeTrackingUser = GetComponentInParent<EyeTrackingUser>();
            _eyeTrackingUser.OnEyeTrackingData.AddListener(OnEyeTrackingData);
        }

        #endregion

        #region Private Methods

        private void OnEyeTrackingData(EyeTrackingData data)
        {
            transform.localPosition = data.GazeOrigin;
            transform.localRotation = Quaternion.LookRotation(data.GazeDirection, Vector3.up);

            if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, _raycastDistance,
                    _raycastLayers))
            {
                _cursor.SetActive(true);
                _cursor.transform.position = hit.point;
            }
            else
            {
                _cursor.SetActive(false);
            }
        }

        #endregion
    }
}
