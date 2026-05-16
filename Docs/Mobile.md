# Mobile-Ver

`Assets/Scenes/Mobile-Ver.unity` is the mobile AR target. It should provide the same user-facing behavior as `Desktop-Ver`, but through the AR Foundation mobile pipeline instead of the desktop 2D/MediaPipe pipeline.

## Current Development Status

Mobile is now wired to AR-native adapter scripts rather than the desktop MediaPipe runner.

Current status:

- `Mobile-Ver.unity` contains `AR Session`.
- `Mobile-Ver.unity` contains `XR Origin (Mobile AR)`.
- `Main Camera` has AR Foundation camera components, including `ARCameraBackground` and `ARCameraManager`.
- `AppManager` uses mobile-specific face/head tracking adapters.
- `SpeechToTextManager` is shared and can auto-bind to mobile or desktop head label controllers.
- Android speech-to-text is still a placeholder.

The mobile face/head tracking path currently supports:

- `ScreenCenterFallback`: stable fallback target for development.
- `TouchPosition`: touch/mouse-driven target for testing label movement.
- `ARFaceManager`: projects tracked AR face positions to screen coordinates if an `ARFaceManager` is present and configured.

At the moment, `Mobile-Ver.unity` is set to `ScreenCenterFallback`. That means it validates the AR camera/session wiring and label/STT behavior, but it does not yet prove real device face tracking.

## Current Mobile Wiring

Scene:

- `Assets/Scenes/Mobile-Ver.unity`

Core AR objects:

- `AR Session`
- `XR Origin (Mobile AR)`
- `Camera Offset`
- `Main Camera`

Main camera components:

- `Camera`
- `AudioListener`
- `TrackedPoseDriver`
- `ARCameraBackground`
- `ARCameraManager`

`AppManager` components:

- `AndroidPermissionRequest`
- `MobileARHeadTracker`
- `SpeechToTextManager`
- `MobileARFaceTrackingRunner`
- `MobileCamFeed`
- `ARCameraConfigSwitcher`
- `MobileSceneAutoBinder`

Important bindings:

- `MobileARHeadTracker.runner` points to `MobileARFaceTrackingRunner`.
- `MobileARHeadTracker.headLabel` points to `HeadLabel`.
- `MobileARHeadTracker.headLabelText` points to the `HeadLabel` TMP text.
- `MobileARFaceTrackingRunner.trackingCamera` points to the AR `Main Camera`.
- `MobileCamFeed.startOnEnable` is disabled in the AR scene so it does not start a parallel `WebCamTexture` feed over the AR camera background.
- `ARCameraConfigSwitcher.cameraManager` points to the AR camera manager on `Main Camera`.
- `ARCameraConfigSwitcher.statusText` points to `DebugText`.
- `MobileSceneAutoBinder` runs on awake to repair missing scene references when possible.

## Verification Needed

Editor verification:

- Open `Mobile-Ver.unity`.
- Confirm there are no missing script components on `AppManager`, `Main Camera`, `AR Session`, or `XR Origin`.
- Confirm `AppManager` has the seven components listed above.
- Confirm `Main Camera` still has `ARCameraBackground` and `ARCameraManager`.
- Confirm `MobileCamFeed.startOnEnable` is off for the AR scene.
- Confirm the camera button is wired to camera configuration switching, not to starting a desktop-style preview.
- Confirm `DebugText` receives AR camera config/status messages in Play Mode.

Unity compile/import verification:

- Run a Unity import/compile check with Unity `6000.3.13f1`.
- Confirm there are no C# compiler errors.
- Existing warnings from MediaPipe samples and the Android STT placeholder are expected for now.

Android/device verification:

- Build and deploy to an ARCore-capable Android device.
- Confirm camera permission is requested.
- Confirm microphone permission is requested.
- Confirm the AR camera background renders the real camera feed.
- Confirm `ARCameraConfigSwitcher` reports available camera configs.
- Tap the camera/config button and confirm config status updates without breaking the AR feed.
- Confirm `HeadLabel` appears at the fallback target.
- Switch `MobileARFaceTrackingRunner.trackingSource` to `TouchPosition` and confirm touches move the label.
- If using face tracking, add/configure `ARFaceManager`, switch `trackingSource` to `ARFaceManager`, and confirm tracked face positions drive the label.

Speech-to-text verification:

- Confirm `SpeechToTextManager.headLabelText` points to the mobile `HeadLabel` text.
- Confirm `PlatformDefault` selects Android STT on Android builds.
- Replace or finish `AndroidSpeechProvider` before treating Android STT as complete.
- Confirm partial/final recognized text appears above the tracked/fallback head target and fades out.

Feature parity verification:

- Compare behavior against `Desktop-Ver` after each feature change.
- Confirm the same user-facing feature works on mobile even if the input provider differs.
- Keep feature logic shared where practical, and keep camera/tracking differences inside mobile adapter scripts.

## Known Gaps

- Android STT is a placeholder.
- Real AR face tracking is not proven until `ARFaceManager` is added/configured and tested on a supported device.
- The mobile scene currently validates AR camera/session wiring plus fallback/touch-style label positioning, not production-grade face tracking.
- Any future desktop feature that directly depends on MediaPipe result types will need an adapter before it can work cleanly on mobile.
