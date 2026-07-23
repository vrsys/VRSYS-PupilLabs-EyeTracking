using Unity.Netcode;
using UnityEngine;

namespace VRSYS.PupilLabs
{

    // Networking-specific companion for EyeTrackingUser. Requires EyeTrackingUser on the same
    // GameObject with its "Standalone" flag turned off, so this script alone controls when
    // Connect()/SubscribeToGazeData()/UnsubscribeFromGazeData() get called.
    [RequireComponent(typeof(EyeTrackingUser))]
    public class NetworkedEyeTrackingUser : NetworkBehaviour
    {
        private EyeTrackingUser _core;

        private NetworkVariable<int> _connectedToDeviceIndex = new(-1,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private void Awake()
        {
            _core = GetComponent<EyeTrackingUser>();
        }

        public override void OnNetworkSpawn()
        {
            _core.SubscribeToGazeData();
        }

        public override void OnNetworkDespawn()
        {
            _core.UnsubscribeFromGazeData();
        }

        #region RPCs

        [Rpc(SendTo.Owner)]
        public void SetDeviceIndexRpc(int idx) => _connectedToDeviceIndex.Value = idx;

        [Rpc(SendTo.Owner)]
        public void ConnectToDeviceRpc() => _core.Connect(_connectedToDeviceIndex.Value);

        [Rpc(SendTo.Owner)]
        public void SetDeviceIndexAndConnectRpc(int idx)
        {
            _connectedToDeviceIndex.Value = idx;
            _core.Connect(_connectedToDeviceIndex.Value);
        }

        #endregion
    }
}
