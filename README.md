# 26-VRoom

26-VRoom is a Unity project for building and testing a speech-driven face/head label experience across two scene targets:

- Desktop development scene: `Assets/Scenes/Desktop-Ver.unity`
- Mobile AR scene: `Assets/Scenes/Mobile-Ver.unity`

The intended workflow is to prototype and validate behavior on desktop first, then port the same feature behavior into the mobile scene while keeping mobile on the AR Foundation camera/session pipeline.

## Current Status

The project currently has two separate runtime paths:

- Desktop keeps the original 2D/MediaPipe-style pipeline for fast iteration.
- Mobile uses an AR-native pipeline based on `AR Session`, `XR Origin`, `ARCameraBackground`, `ARCameraManager`, and mobile-specific adapter scripts.

The mobile scene is not a full copy of the desktop scene. It is wired to provide equivalent app behavior through AR-facing adapters, so shared feature work should avoid assuming that both scenes use the same camera source or tracking implementation.

## Main Pieces

- `Assets/Scenes/Desktop-Ver.unity`: desktop test scene.
- `Assets/Scenes/Mobile-Ver.unity`: mobile AR scene.
- `Assets/Scripts/FaceTracking`: current desktop MediaPipe face/head tracking scripts.
- `Assets/Scripts/Mobile`: mobile AR adapter scripts.
- `Assets/Scripts/SpeechToText`: shared speech-to-text flow.
- `Assets/Scripts/Camera`: camera helpers, including AR camera configuration switching.
- `stt-server`: optional local Whisper/WebSocket STT server.

## Documentation

- [Desktop status](Docs/Desktop.md)
- [Mobile status and verification](Docs/Mobile.md)

## Verified So Far

Unity batchmode import/compile was run with Unity `6000.3.13f1` and exited with return code `0`.

Known warnings:

- MediaPipe sample code uses obsolete `Resolution.refreshRate` APIs.
- `AndroidSpeechProvider` is still a placeholder and reports unused event warnings.

No C# compiler errors were reported during the latest check.
