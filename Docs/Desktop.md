# Desktop-Ver

`Assets/Scenes/Desktop-Ver.unity` is the desktop-first development scene. Use this scene to validate feature behavior before porting that behavior to mobile.

## Current Role

Desktop is the fast iteration target. It should remain focused on simple editor/desktop testing rather than AR device behavior.

Current desktop behavior uses:

- `HeadTracker`
- `SpeechToTextManager`
- the existing MediaPipe face detection runner in `Assets/Scripts/FaceTracking`
- a 2D display/camera-style setup through the existing scene objects

## Development Expectations

When adding a new feature:

1. Build and test the app behavior here first.
2. Keep feature logic as scene-independent as possible.
3. Avoid hard-coding desktop camera, `RawImage`, or MediaPipe assumptions into shared behavior.
4. Port only the platform-specific input/tracking/camera parts into `Mobile-Ver`.

## Things To Check On Desktop

- `Desktop-Ver.unity` opens without missing scripts.
- The app manager still has the expected face/head tracking and speech-to-text components.
- The head label appears and follows the detected/tracked position.
- Speech-to-text text appears in the head label and fades out.
- The selected STT provider is appropriate for the test environment:
  - Windows/editor: `WindowsNative` or `PlatformDefault`
  - local server testing: `WhisperServer`

## Known Notes

- Desktop is still tied to the MediaPipe-style 2D pipeline.
- Desktop is intentionally not using the AR camera background or AR Foundation session pipeline.
- If desktop behavior changes, mobile should be updated through the mobile adapter scripts rather than by copying desktop scene wiring directly.
