using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PupilLabs;
using UnityEngine;
using VRSYS.Core.Logging;
using VRSYS.Core.Networking;

namespace VRSYS.PupilLabs
{
    public class NeonDeviceConnector : MonoBehaviour, INetworkUserCallbacks
    {
        #region Singleton

        public static NeonDeviceConnector Instance { get; private set; }

        #endregion

        public event Action<NeonGazeDataProvider> GazeProviderActivated;


        #region Properties

        [Header("Connection Configuration")]
        [SerializeField] private List<string> _deviceNames;
        [SerializeField] private bool _autoConnect;
        [SerializeField, UserRoleSelector] private List<UserRole> _eyeTrackingUserRoles;
        [SerializeField, Range(0, 10)] private int _maxDiscoveryRetries = 5;
        [SerializeField, Range(0, 60)] private int _retryDelaySeconds = 3;

        [Header("Eye Tracking Components")] 
        [SerializeField] private DataStorage _dataStorage;
        [SerializeField] private DeviceManager _deviceManager;
        [SerializeField] private GazeDataProvider _gazeDataProvider;
        public GazeDataProvider GazeDataProvider => _gazeDataProvider;
        
        public bool IsGazeDataProviderActive => 
            _gazeDataProvider != null && _gazeDataProvider.isActiveAndEnabled;

        [Header("Debug")] 
        [SerializeField] private bool _verbose = true;

        #endregion

        #region MonoBehaviour Methods

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        #endregion

        #region Public Methods
        
        public async void Connect(int deviceIndex)
        {
            if (_gazeDataProvider.isActiveAndEnabled)
            {
                ExtendedLogger.LogWarning(GetType().Name, "Eye tracker is already connected.", this);
                return;
            }
            
            if(_verbose)
                ExtendedLogger.LogInfo(GetType().Name, "Starting connection with eye tracker.", this);

            if (_dataStorage == null || _deviceManager == null || _gazeDataProvider == null)
            {
                ExtendedLogger.LogError(GetType().Name, "Canceling connection. One or multiple missing eye tracking reference. Check inspector configuration.", this);
                return;
            }

            await _dataStorage.WhenReady();

            // If device index == -1 --> auto connecting
            // Connecting to first device found
            if (deviceIndex == -1)
            {
                if(_verbose)
                    ExtendedLogger.LogInfo(GetType().Name, "Connecting to auto IP...");

                _dataStorage.Config.rtspSettings.autoIp = true;
            }
            else
            {
                if (deviceIndex < -1 || deviceIndex >= _deviceNames.Count)
                {
                    throw new IndexOutOfRangeException(
                        $"Device index {deviceIndex} is out of range of device names list.");
                }

                string targetName = _deviceNames[deviceIndex];
                
                if(_verbose)
                    ExtendedLogger.LogInfo(GetType().Name, $"Searching for target device {targetName}...", this);

                string foundIp = null;

                for (int attempt = 0; attempt < _maxDiscoveryRetries; attempt++)
                {
                    if (attempt > 0)
                    {
                        if(_verbose)
                            ExtendedLogger.LogInfo(GetType().Name, $"Retrying discovery ({attempt + 1}/{_maxDiscoveryRetries})...");
                        await Task.Delay(_retryDelaySeconds * 1000);
                    }

                    await _deviceManager.Discover();
                    
                    if(_deviceManager.DiscoveredDevices != null)
                        foreach (var kvp in _deviceManager.DiscoveredDevices)
                        {
                            if(_verbose)
                                ExtendedLogger.LogInfo(GetType().Name, $"Device found: {kvp.Key}", this);
                            if (kvp.Key.StartsWith(targetName))
                            {
                                _deviceManager.SelectDevice(kvp.Key);
                                foundIp = _deviceManager.SelectedDeviceIp;
                                break;
                            }
                        }
                    if(!string.IsNullOrEmpty(foundIp))
                        break;
                }

                if (!string.IsNullOrEmpty(foundIp))
                {
                    if(_verbose)
                        ExtendedLogger.LogInfo(GetType().Name, $"Found {targetName} at {foundIp}", this);

                    _dataStorage.Config.rtspSettings.autoIp = false;
                    _dataStorage.Config.rtspSettings.ip = foundIp;
                }
                else
                {
                    ExtendedLogger.LogWarning(GetType().Name, $"Device {targetName} not found after {_maxDiscoveryRetries} attempts, aborting discovery...", this);
                    _dataStorage.Config.rtspSettings.autoIp = false; // prevent automatic connection with incorrect tracker
                    return;
                }
            }
            
            if(_verbose)
                ExtendedLogger.LogInfo(GetType().Name, "Activating gaze provider.", this);
            
            _gazeDataProvider.gameObject.SetActive(true);
            GazeProviderActivated?.Invoke( (NeonGazeDataProvider) _gazeDataProvider);
        }

        #endregion

        #region INetworkUserCallbacks

        public void OnLocalNetworkUserSetup()
        {
            if (!_autoConnect)
            {
                if(_verbose)
                    ExtendedLogger.LogInfo(GetType().Name, "Eye tracking auto connect set to false. Skipping auto connection.", this);
                return;
            }

            if (!_eyeTrackingUserRoles.Contains(NetworkUser.LocalInstance.userRole.Value))
            {
                if(_verbose)
                    ExtendedLogger.LogInfo(GetType().Name, $"Local user role {NetworkUser.LocalInstance.userRole.Value.Name} is not using eye tracking. Skipping auto connection.");
                return;
            }
            
            // Connect to first device found.
            Connect(-1);
        }

        public void OnRemoteNetworkUserSetup(NetworkUser user)
        {
            // ...
        }

        #endregion
    }
}


