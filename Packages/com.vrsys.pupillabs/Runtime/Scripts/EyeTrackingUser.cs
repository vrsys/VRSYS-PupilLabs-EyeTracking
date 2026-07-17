using PupilLabs;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace VRSYS.PupilLabs
{
    public class EyeTrackingUser : NetworkBehaviour
    {
        #region Properties

        private NetworkVariable<int> _connectedToDeviceIndex = new(-1, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        public UnityEvent<EyeTrackingData> OnEyeTrackingData = new();
        
        #endregion

        #region Mono- & NetworkBehaviour Methods

        public override void OnNetworkSpawn()
        {
            NeonDeviceConnector.Instance.GazeDataProvider.gazeDataReady.AddListener(OnGazeDataReady);
        }

        public override void OnNetworkDespawn()
        {
            NeonDeviceConnector.Instance.GazeDataProvider.gazeDataReady.RemoveListener(OnGazeDataReady);
        }

        #endregion

        #region Private Methods

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

        #endregion

        #region RPCs

        [Rpc(SendTo.Owner)]
        public void SetDeviceIndexRpc(int idx) => _connectedToDeviceIndex.Value = idx;

        [Rpc(SendTo.Owner)]
        public void ConnectToDeviceRpc() => NeonDeviceConnector.Instance.Connect(_connectedToDeviceIndex.Value);

        [Rpc(SendTo.Owner)]
        public void SetDeviceIndexAndConnectRpc(int idx)
        {
            _connectedToDeviceIndex.Value = idx;
            NeonDeviceConnector.Instance.Connect(_connectedToDeviceIndex.Value);
        }

        #endregion
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
