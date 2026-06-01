# Mobile Camera, Face, and Subtitle Integration

Developer notes for adding UI, STT, subtitle anchors, gestures, or camera-dependent features to `Mobile-Ver.unity`.

## Architecture

```text
MobileARModeController
  -> switches FrontFaceAR / BackFace2D / WorldHands
  -> enables the correct camera-facing provider

MobileARFaceTrackingRunner        BackCameraFacePositionProvider
  -> FrontFaceAR candidates        -> BackFace2D candidates
              \                    /
               ActiveFacePositionProviderRouter
                 -> active provider
                 -> active transform settings
                 -> PersonFaceTrackManager
                    -> P1 / P2 / Primary Person
                         |
                 FaceAnchoredSubtitleTarget
                   -> moves HeadSubtitleAnchor
                   -> HeadLabel / subtitle UI follows anchor

STT provider / stream
  -> SttStreamReceiver
  -> StreamingSubtitleController
  -> HeadLabel text
```

## Component Responsibilities

| Component | Owns | Notes |
| --- | --- | --- |
| `MobileARModeController` | Camera mode switching | Requests front/back facing direction, enables front AR face tracking or back 2D face provider, disables conflicting AR systems by mode. |
| `ActiveFacePositionProviderRouter` | Active face/person data route | Selects front or back provider, exposes active mode, active transform settings, primary person, `P1`/`P2` lookup, and legacy single-face center/rect. |
| `PersonFaceTrackManager` | Stable person IDs | Converts current-frame face candidates into stable `P1`, `P2`, and primary person using nearest-position matching. |
| `FaceAnchoredSubtitleTarget` | Subtitle anchor movement | Moves the subtitle target, applies front/back follow settings, and follows Primary/P1/P2. Do not duplicate its positioning math. |
| `BackCameraFacePositionProvider` | Back-camera MediaPipe face detections | Uses AR CPU camera images and publishes both legacy best face and multi-face candidates. |
| `MobileARFaceTrackingRunner` | Front AR face detections | Publishes front AR face candidates when available. True multi-face depends on ARFoundation/device support. |
| `StreamingSubtitleController` | Visible subtitle text UI | Receives partial/final strings and updates the HeadLabel text template with fade behavior. |
| `SttStreamReceiver` | STT stream bridge | Accepts partial/final/token callbacks and forwards them to `StreamingSubtitleController`. |
| `FaceDebugOverlay` | Runtime visualization | Draws face boxes, person labels, primary marker, movement-stage markers, and subtitle target markers. |

## Current Integration Contracts

Use `ActiveFacePositionProviderRouter` for face/person state:

```csharp
router.CurrentMode
router.CurrentProviderName
router.CurrentTransformSettings
router.Back2DProfile
router.HasFace
router.NormalizedFaceCenter
router.NormalizedFaceRect
router.ActivePersonTracks
router.HasPrimaryPersonTrack
router.PrimaryPersonTrack
router.TryGetPersonTrack(1, out PersonFaceTrack p1)
router.TryGetPersonTrack(2, out PersonFaceTrack p2)
```

Use `FaceAnchoredSubtitleTarget` for subtitle anchor diagnostics/settings:

```csharp
subtitleTarget.ActiveFollowSettings
subtitleTarget.ActiveFollowSettingsName
subtitleTarget.CurrentDesiredSubtitleWorldCenter
subtitleTarget.CurrentDesiredAnchoredPosition
subtitleTarget.CurrentActualAnchoredPosition
```

Use `MobileARModeController` for mode control:

```csharp
modeController.CurrentMode
modeController.SetFrontFaceARMode()
modeController.SetBackFace2DMode()
modeController.SetWorldHandsMode()
modeController.ToggleMode()
```

Use `SttStreamReceiver` / `StreamingSubtitleController` for text:

```csharp
sttReceiver.OnPartialTextReceived("hello wor");
sttReceiver.OnFinalTextReceived("hello world");

subtitleController.ReceivePartialText("hello wor");
subtitleController.ReceiveFinalText("hello world");
```

## UI Integration Patterns

### Attach UI to the Current Primary Person

Prefer a small follower component that reads the router. Do not make the UI element talk directly to front/back providers.

```csharp
using UnityEngine;

public class PrimaryPersonFollower : MonoBehaviour
{
    [SerializeField] private ActiveFacePositionProviderRouter router;
    [SerializeField] private RectTransform target;

    private void LateUpdate()
    {
        if (router == null || target == null || !router.HasPrimaryPersonTrack)
            return;

        PersonFaceTrack person = router.PrimaryPersonTrack;
        Vector2 normalized = person.HasBounds
            ? new Vector2(person.NormalizedBounds.center.x, person.NormalizedBounds.yMax)
            : person.NormalizedCenter;

        // Convert normalized screen coordinates using the same UI conversion style
        // as your local canvas. Do not reimplement face coordinate transforms here.
    }
}
```

For production, copy only the canvas conversion part you need, or expose a shared anchor-source helper later. Keep face/person selection in the router.

### Attach UI to P1 or P2

```csharp
if (router.TryGetPersonTrack(1, out PersonFaceTrack p1))
{
    Vector2 center = p1.NormalizedCenter;
}

if (router.TryGetPersonTrack(2, out PersonFaceTrack p2))
{
    Vector2 center = p2.NormalizedCenter;
}
```

Use `PersonId`, not detection index. Detection order can change frame to frame.

### Attach UI to the No-Face Fallback Center

If the UI is the existing subtitle, use `FaceAnchoredSubtitleTarget` and its `Center Fallback Anchor`/follow settings. If this is a new UI element, add a small follower component with its own fallback behavior, but still read faces from `ActiveFacePositionProviderRouter`.

### Attach UI to the Active Subtitle Anchor

If the UI should move exactly with the current subtitle anchor, parent it under the same `HeadSubtitleAnchor` or read:

```csharp
subtitleTarget.CurrentDesiredSubtitleWorldCenter
subtitleTarget.CurrentActualAnchoredPosition
```

Use the actual target if you want what the user sees after smoothing. Use desired position only for debugging or predictive UI.

## STT Integration Patterns

STT text should flow into:

```text
provider or network stream
  -> SttStreamReceiver
  -> StreamingSubtitleController
  -> HeadLabel text template
```

Recommended extension point:

```csharp
SttStreamReceiver.OnPartialTextReceived(text);
SttStreamReceiver.OnFinalTextReceived(text);
```

If your STT provider is already inside Unity and does not need token buffering, calling `StreamingSubtitleController.ReceivePartialText` / `ReceiveFinalText` directly is acceptable. Avoid direct `TMP_Text.text` assignment for normal subtitle updates because it bypasses fade, clearing, logging, and future subtitle presentation behavior.

A future `ISubtitleTextSink` would be useful if more than one STT provider needs to target subtitles without knowing about `StreamingSubtitleController`:

```csharp
public interface ISubtitleTextSink
{
    void ReceivePartialText(string text);
    void ReceiveFinalText(string text);
}
```

Do not add this until there is a second real consumer/provider that benefits from the indirection.

## Gesture and Camera Integration

Gesture systems should use `MobileARModeController.CurrentMode` to decide what camera state they are in.

Do:

```csharp
switch (modeController.CurrentMode)
{
    case MobileARModeController.MobileTrackingMode.FrontFaceAR:
        // Use front-camera AR-compatible path.
        break;

    case MobileARModeController.MobileTrackingMode.BackFace2D:
        // Avoid taking over the AR CPU image stream used by BackCameraFacePositionProvider.
        break;

    case MobileARModeController.MobileTrackingMode.WorldHands:
        // Hand/gesture AR systems may be active here.
        break;
}
```

Do not create a second independent `WebCamTexture` for BackFace2D unless camera ownership is explicitly coordinated. BackFace2D already depends on `ARCameraManager.TryAcquireLatestCpuImage`; another camera feed can fight the same hardware or desynchronize preview, detection, and UI.

If a gesture recognizer needs frames in BackFace2D, prefer a future `ICameraFrameSource` that is owned by the existing camera pipeline. If it only needs mode changes, a small polling component that reads `CurrentMode` is enough. A future `IActiveModeListener` is reasonable only if many systems need event-style mode notifications.

## Scene Wiring in `Mobile-Ver.unity`

Main locations:

| Scene object/component | Purpose |
| --- | --- |
| `AppManager` | Holds `MobileARFaceTrackingRunner`, `MobileARModeController`, `ActiveFacePositionProviderRouter`, `PersonFaceTrackManager`, `FaceDebugOverlay`, and `MobileOrientationController`. |
| `AR Camera` / camera manager object | Holds `BackCameraFacePositionProvider` in the current scene. |
| `HeadLabel` | Text/UI object shown as the subtitle label. |
| `HeadSubtitlePanel` | Contains `StreamingSubtitleController` and the subtitle text/background references. |
| `HeadSubtitleAnchor` | Has `FaceAnchoredSubtitleTarget`; this is the moving subtitle anchor. |
| `SttStreamReceiver` | Bridges STT callbacks to `StreamingSubtitleController`. |

Required Inspector references:

| Component | Important references |
| --- | --- |
| `MobileARModeController` | `ARSession`, `XROrigin`, `ARCameraManager`, `ARFaceManager`, hand runner, `MobileARFaceTrackingRunner`, `BackCameraFacePositionProvider`, orientation controller, status text. |
| `ActiveFacePositionProviderRouter` | `MobileARModeController`, front provider, back provider, `PersonFaceTrackManager`, front/back coordinate profiles. |
| `PersonFaceTrackManager` | Usually on `AppManager`; defaults track two people. |
| `FaceAnchoredSubtitleTarget` | `target` = subtitle anchor RectTransform, `faceProviderBehaviour` = router, `Person To Follow` = Primary/P1/P2. |
| `FaceDebugOverlay` | `faceProviderBehaviour` = router, `subtitleTarget`, `subtitleTargetController`. |
| `StreamingSubtitleController` | `subtitleContent`, `wordTextTemplate`, `subtitleCanvasGroup`, `subtitleBackground`. |
| `SttStreamReceiver` | `subtitleController`. |

## Future Extension Points

Add only when the second consumer appears:

| Interface/component | When it helps |
| --- | --- |
| `ISubtitleTextSink` | Multiple STT sources need to send text without depending on `StreamingSubtitleController`. |
| `ISubtitleAnchorSource` | Multiple UI elements need the exact same desired/actual subtitle anchor without depending on `FaceAnchoredSubtitleTarget`. |
| `ICameraFrameSource` | Gesture, face, or recording features need shared camera frames without opening competing feeds. |
| `IActiveModeListener` | Several systems need mode-change events instead of polling `CurrentMode`. |
| `IPersonTrackConsumer` | Multiple systems need batched person-track notifications. |

## Do Not Do

- Do not create a second independent `WebCamTexture` for BackFace2D unless camera ownership is explicitly coordinated.
- Do not bypass `ActiveFacePositionProviderRouter` for face/person position.
- Do not duplicate subtitle positioning math from `FaceAnchoredSubtitleTarget`.
- Do not assign `HeadLabel` position directly every frame if `FaceAnchoredSubtitleTarget` owns movement.
- Do not rely on `DetectionIndex` as person identity.
- Do not assume `FrontFaceAR` always supports multiple faces; it depends on ARFoundation/device support.
- Do not add separate front/back UI code paths unless the router contract is insufficient.
- Do not rewrite coordinate mapping in consumers. Use router-provided positions/settings.

## Validation Checklist

- One face visible: `HeadLabel` follows the same primary person as before.
- Two faces visible in BackFace2D: `P1`/`P2` labels stay stable when people move slightly.
- Detection order changes do not swap `P1`/`P2` if positions remain close.
- Removing one person and re-entering after timeout can reuse a freed ID.
- Switching `FrontFaceAR` <-> `BackFace2D` does not crash and clears stale person smoothing/history.
- `FaceDebugOverlay` can show person boxes, labels, primary marker, and movement-stage markers.
- STT partial/final text reaches `StreamingSubtitleController` and updates `HeadLabel`.
- `dotnet build Assembly-CSharp.csproj` passes.
- `git diff --check` passes.
