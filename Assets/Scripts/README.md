# Scripts Folder Map

This folder contains the project-owned runtime scripts. Imported Unity, TextMesh Pro,
XR, XRI, and MediaPipe sample/vendor content should stay outside this folder unless
it is intentionally customized for the app.

## App

Small application-level behaviours, such as platform permission requests.

## Camera

Camera feed and AR camera configuration helpers.

## FaceTracking

MediaPipe face detection integration and UI label positioning.

## SpeechToText

Speech recognition provider interface plus platform-specific providers.

## Legacy

Experimental or older scripts kept for reference. Move scripts out of this folder
before wiring them into production scenes.

## Organization Notes

- Keep MonoBehaviour file names matched to their class names.
- Move script `.meta` files together with scripts so Unity scene and prefab GUID
  references remain valid.
- Prefer adding new project code under `Assets/Scripts/<Feature>` rather than into
  imported folders like `Assets/MediaPipeUnity`, `Assets/XR`, or `Assets/XRI`.
