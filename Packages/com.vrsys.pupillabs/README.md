# VRSYS-PupilLabs-EyeTracking

Unity package that provides custom connection and logging functionality for the [Pupil Labs Neon](https://pupil-labs.com/products/neon) eye trackers, and allows easy integration of eye-tracking into apps built using the [VRSYS](https://vrsys.gitbook.io/vrsys) framework.


## Installation

1. Add the Neon XR Core dependency to your project as described [here](https://docs.pupil-labs.com/neon/neon-xr/neon-xr-core-package/) in the _Adding Neon XR to Your Project_ section.
2. Add this package, e.g. via Package Manager → "Add package from git URL" pointing at this repo, path `https://github.com/vrsys/VRSYS-PupilLabs-EyeTracking.git?path=/Packages/com.vrsys.pupillabs` (requires VRSYS-Core, `com.vrsys.core` to already be installed).
4. Import the **Eye Tracking Samples** sample from the Package Manager window to get the example scenes and prefab described below.

## Contents

- `Runtime/Scripts/NeonDeviceConnector.cs` — singleton that discovers and connects to a Neon device (by name or auto-connect to the first one found), and exposes a `GazeProviderActivated` event once connected.
- `Runtime/Scripts/EyeTrackingUser.cs` — per-user component that subscribes to gaze data once the connector is ready and re-broadcasts it as an `OnEyeTrackingData` Unity event (pupil diameter, eye openness, gaze origin/direction).
- `Runtime/Scripts/NetworkedEyeTrackingUser.cs` — Netcode companion for `EyeTrackingUser` that drives connection/subscription over RPCs for the network-owning client (used together with `EyeTrackingUser` in "standalone = false" mode).
- `Runtime/Scripts/GazeDataLogger.cs` — subscribes directly to the raw ~200 Hz tracker stream (independent of Unity's frame rate) and writes gaze/pupil/eyelid samples to a timestamped CSV under `Application.persistentDataPath/StudyData`. In UDP mode, the timestamps provided by the tracker are used to derive a synchronised unity timestamp for each data packet. In TCP mode, no timestamp is read by the NeonXR package, so the time at which the package was received is used instead.   

Sample scenes (under `Samples~/Eye Tracking Samples/Scenes`, imported via Package Manager):

- **VRSYS - Eye Tracking Samples** — networked scene using `NetworkedEyeTrackingUser` on a spawned HMD user prefab, showing connection triggered by a role/network setup.
- **VRSYS - Non-Networked Eye Tracking** — single-user scene using standalone `EyeTrackingUser`, plus a and CSV recording (`GazeDataLogger` + `GazeDataLoggerTrigger`).

## Using it in a custom scene

1. Copy the "PupilLabs Eye Tracking" game object from a sample scene, which should have the components `DeviceManager`, `DataStorage`, `NeonGazeDataProvider` (on a child object), and this package's `NeonDeviceConnector`. Ensure that the connector's `_dataStorage` / `_deviceManager` / `_gazeDataProvider` fields are wired up to sibling components. Set `_autoConnect` / `_deviceNames` as needed (leave `_deviceNames` empty and call `Connect(-1)` to auto-connect to the first device found).
2. Add `EyeTrackingUser` to the user/camera GameObject that should receive gaze data. For a non-networked scene leave `_standalone = true` so it connects itself; for a networked scene set `_standalone = false` and add `NetworkedEyeTrackingUser` alongside it so connection is driven over the network for the owning client.
3. Listen to `EyeTrackingUser.OnEyeTrackingData` (or use the `GazeCursor` sample script as a template) to react to gaze — e.g. raycast from `GazeOrigin`/`GazeDirection` transformed into world space.
4. For logging, add `GazeDataLogger` to a game object. Call `BeginSession(participantId)` at least once before recording. Call `StartRecording(trialBlock)` and `StopRecording()` to record raw gaze/pupil data to a CSV.


## Notes on Working with the PupilLabs Neon eye tracker in VR

- Fit the Neon module to the HMD by swapping in the facial interface that has the tracker cable, routed out the top of the headset.
- The module should be connected to the companion phone via USB-C. On the companion phone, the Neon Companion app must be open.
- The Neon Companion phone and the Unity application/headset must be on the **same WiFi network** (eduroam doesn't seem to work reliably for this, prefer a local WiFi). 
- Connection configuration:
   - The connection settings used by the Unity app to connect with the tracker companion are set in `config.json`, which is copied from the NeonXR package's Addressables folder into the app's persistent data path, but only if it isn't already there. To change settings after first run, edit it directly at the persistent data path (`.../LocalLow/<company>/<product>/config.json`), not the source Addressable.
   - The config file allows UDP (or TCP) to be selected. The correct port should also be set there. It seems that 8086 is used for UDP and 8686 for TCP. The correct port for each mode can be checked by accessing the neon companion web interface at `http://{ip}:{port}/api/status`, using the IP and port shown in the app (click on phone icon to show streaming settings). 
   - Note: For direct (UDP) connection with the companion phone from a PC, set the WiFi network's type to "private" so Windows/Unity is allowed to receive the incoming data (click 'allow' if prompted when the Unity app starts).
- Connecting/discovering the device can be slow; give it time before assuming it failed. It seems to work better when the Neon Companion app is open. You can see if anything is connected to the companion device in the app (blue number appears by the streaming icon). Sometimes restarting the Neon Companion app can trigger a connection.

## TODOs

- Calibration workflow is not yet included.
- Triggering of recordings of eye-tracking data on the device not yet supported. 
- Script for application of gaze data to Meta Avatars.
