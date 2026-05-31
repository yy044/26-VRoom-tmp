using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public struct SubtitleFollowSettings
{
    [Header("Subtitle Follow Settings")]
    [InspectorName("Center Offset When No Face")]
    [SerializeField] private Vector2 centerOffsetWhenNoFace;

    [InspectorName("Use Top Of Face Box")]
    [SerializeField] private bool useTopOfFaceBox;

    [InspectorName("Extra Gap Above Face")]
    [SerializeField] private float extraGapAboveFace;

    [InspectorName("Face Box Height Gap Multiplier")]
    [SerializeField] private float faceBoxHeightGapMultiplier;

    [InspectorName("Smooth Face Tracking")]
    [SerializeField] private bool smoothFaceTracking;

    [InspectorName("Face Tracking Smooth Time")]
    [SerializeField] private float faceTrackingSmoothTime;

    [InspectorName("Keep Inside Screen Padding")]
    [SerializeField] private Vector2 keepInsideScreenPadding;

    [InspectorName("Subtitle Movement Smooth Time")]
    [SerializeField] private float subtitleMovementSmoothTime;

    public Vector2 CenterOffsetWhenNoFace => centerOffsetWhenNoFace;
    public bool UseTopOfFaceBox => useTopOfFaceBox;
    public float ExtraGapAboveFace => extraGapAboveFace;
    public float FaceBoxHeightGapMultiplier => faceBoxHeightGapMultiplier;
    public bool SmoothFaceTracking => smoothFaceTracking;
    public float FaceTrackingSmoothTime => faceTrackingSmoothTime;
    public Vector2 KeepInsideScreenPadding => keepInsideScreenPadding;
    public float SubtitleMovementSmoothTime => subtitleMovementSmoothTime;

    public static SubtitleFollowSettings FrontDefaults()
    {
        return new SubtitleFollowSettings
        {
            centerOffsetWhenNoFace = new Vector2(0f, 0.12f),
            useTopOfFaceBox = true,
            extraGapAboveFace = 0.02f,
            faceBoxHeightGapMultiplier = 0.15f,
            smoothFaceTracking = true,
            faceTrackingSmoothTime = 0.06f,
            keepInsideScreenPadding = new Vector2(0.05f, 0.08f),
            subtitleMovementSmoothTime = 0.08f
        };
    }

    public static SubtitleFollowSettings BackDefaults()
    {
        return new SubtitleFollowSettings
        {
            centerOffsetWhenNoFace = new Vector2(0f, 0.12f),
            useTopOfFaceBox = true,
            extraGapAboveFace = 0.02f,
            faceBoxHeightGapMultiplier = 0.08f,
            smoothFaceTracking = true,
            faceTrackingSmoothTime = 0.18f,
            keepInsideScreenPadding = new Vector2(0.05f, 0.08f),
            subtitleMovementSmoothTime = 0.20f
        };
    }

    public void Validate()
    {
        extraGapAboveFace = Mathf.Max(0f, extraGapAboveFace);
        faceBoxHeightGapMultiplier = Mathf.Max(0f, faceBoxHeightGapMultiplier);
        faceTrackingSmoothTime = Mathf.Max(0f, faceTrackingSmoothTime);
        subtitleMovementSmoothTime = Mathf.Max(0f, subtitleMovementSmoothTime);
    }
}

[System.Serializable]
public class SubtitleFollowState
{
    public Vector2 uiVelocity;
    public Vector2 faceAnchorVelocity;
    public Vector2 smoothedFaceAnchor;
    public bool hasSmoothedFaceAnchor;

    public void ResetFaceAnchorSmoothing()
    {
        faceAnchorVelocity = Vector2.zero;
        hasSmoothedFaceAnchor = false;
    }
}

public enum SubtitlePersonToFollow
{
    [InspectorName("Primary Person")]
    PrimaryPerson,

    P1,
    P2
}

public class FaceAnchoredSubtitleTarget : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("RectTransform to move. Defaults to this object's RectTransform when empty.")]
    [SerializeField] private RectTransform target;

    [Header("Face Provider")]
    [Tooltip("Any MonoBehaviour implementing IFacePositionProvider.")]
    [SerializeField] private MonoBehaviour faceProviderBehaviour;

    [Header("Fallback")]
    [Tooltip("When no face is detected, move the target to centerFallbackAnchor.")]
    [SerializeField] private bool useCenterWhenNoFace = true;

    [Tooltip("Normalized target position used when no face is detected.")]
    [SerializeField] private Vector2 centerFallbackAnchor = new Vector2(0.5f, 0.5f);

    [Header("Person Selection")]
    [InspectorName("Person To Follow")]
    [SerializeField] private SubtitlePersonToFollow personToFollow = SubtitlePersonToFollow.PrimaryPerson;

    [Header("Front Camera Subtitle Movement")]
    [SerializeField] private SubtitleFollowSettings frontCameraFollowSettings = SubtitleFollowSettings.FrontDefaults();

    [Header("Back Camera Subtitle Movement")]
    [SerializeField] private SubtitleFollowSettings backCameraFollowSettings = SubtitleFollowSettings.BackDefaults();

    [Header("Debug")]
    [Tooltip("Emit throttled subtitle positioning logs.")]
    [SerializeField] private bool debugLogs = false;

    [Tooltip("Emit back-camera-only face position logs, including final UI position.")]
    [SerializeField] private bool debugBackCameraFacePosition = false;

    [Header("Diagnostics")]
    [SerializeField] private float currentFaceHeight;
    [SerializeField] private float currentAdaptivePadding;
    [SerializeField] private Vector2 currentAnchorPosition;
    [SerializeField] private Vector2 currentRawFaceCenter;
    [SerializeField] private Vector2 currentRawFaceTarget;
    [SerializeField] private Vector2 currentSmoothedFaceAnchor;
    [SerializeField] private Vector2 currentDesiredNormalizedPosition;
    [SerializeField] private Vector2 currentDesiredAnchoredPosition;
    [SerializeField] private Vector2 currentActualAnchoredPosition;
    [SerializeField] private bool currentPositionWasClamped;
    [SerializeField] private float currentUiVelocityMagnitude;
    [SerializeField] private float currentFaceAnchorVelocityMagnitude;

    private IFacePositionProvider faceProvider;
    private ActiveFacePositionProviderRouter coordinateRouter;
    private RectTransform parentRect;
    private Canvas canvas;
    private readonly SubtitleFollowState frontCameraFollowState = new SubtitleFollowState();
    private readonly SubtitleFollowState backCameraFollowState = new SubtitleFollowState();
    private MobileARModeController.MobileTrackingMode previousCameraMode;
    private bool hasPreviousCameraMode;
    private float nextLogTime;
    private bool loggedRectAdjustment;
    private string lastLogState;
    private Vector2 selectedFaceCenter;
    private Rect selectedFaceRect;

    public SubtitleFollowSettings ActiveFollowSettings => GetActiveFollowSettings();
    public string ActiveFollowSettingsName => GetActiveFollowSettingsName();
    public Vector2 CurrentRawFaceCenter => currentRawFaceCenter;
    public Vector2 CurrentRawFaceTarget => currentRawFaceTarget;
    public Vector2 CurrentSmoothedFaceAnchor => currentSmoothedFaceAnchor;
    public Vector2 CurrentDesiredNormalizedPosition => currentDesiredNormalizedPosition;
    public Vector2 CurrentDesiredAnchoredPosition => currentDesiredAnchoredPosition;
    public Vector2 CurrentActualAnchoredPosition => currentActualAnchoredPosition;
    public Vector3 CurrentDesiredSubtitleWorldCenter => AnchoredPositionToWorldPoint(currentDesiredAnchoredPosition);
    public bool CurrentPositionWasClamped => currentPositionWasClamped;
    public float CurrentFaceHeight => currentFaceHeight;
    public float CurrentAdaptivePadding => currentAdaptivePadding;
    public float CurrentUiVelocityMagnitude => currentUiVelocityMagnitude;
    public float CurrentFaceAnchorVelocityMagnitude => currentFaceAnchorVelocityMagnitude;

    private void Awake()
    {
        AutoBind();
        DisableTargetRaycasts();
    }

    private void OnEnable()
    {
        AutoBind();
        DisableTargetRaycasts();
    }

    private void OnValidate()
    {
        if (target == null)
            target = GetComponent<RectTransform>();

        frontCameraFollowSettings.Validate();
        backCameraFollowSettings.Validate();
    }

    private void Update()
    {
        AutoBind();

        if (target == null || parentRect == null)
            return;

        MobileARModeController.MobileTrackingMode cameraMode = GetActiveCameraMode();
        SubtitleFollowSettings followSettings = GetFollowSettings(cameraMode);
        SubtitleFollowState followState = GetFollowState(cameraMode);
        ResetSmoothingWhenCameraModeChanges(cameraMode, followState);

        Vector2 normalizedPosition = GetTargetNormalizedPosition(followSettings, followState, out bool hasFace, out string mode);
        Vector2 desiredAnchoredPosition = NormalizedToAnchoredPosition(normalizedPosition);
        currentDesiredNormalizedPosition = normalizedPosition;
        currentDesiredAnchoredPosition = desiredAnchoredPosition;
        target.anchoredPosition = Vector2.SmoothDamp(
            target.anchoredPosition,
            desiredAnchoredPosition,
            ref followState.uiVelocity,
            Mathf.Max(0.001f, followSettings.SubtitleMovementSmoothTime));
        currentActualAnchoredPosition = target.anchoredPosition;
        currentUiVelocityMagnitude = followState.uiVelocity.magnitude;
        currentFaceAnchorVelocityMagnitude = followState.faceAnchorVelocity.magnitude;

        LogPosition(hasFace, normalizedPosition, desiredAnchoredPosition, mode, cameraMode, followSettings, followState);
        LogBackCameraFacePosition(hasFace, normalizedPosition, desiredAnchoredPosition, mode, cameraMode, followSettings);
    }

    private void AutoBind()
    {
        if (target == null)
            target = GetComponent<RectTransform>();

        if (target != null && parentRect == null)
            parentRect = target.parent as RectTransform;

        if (target != null && canvas == null)
            canvas = target.GetComponentInParent<Canvas>();

        faceProvider = faceProviderBehaviour as IFacePositionProvider;
        coordinateRouter = faceProviderBehaviour as ActiveFacePositionProviderRouter;
    }

    private void DisableTargetRaycasts()
    {
        if (target == null)
            return;

        Graphic[] graphics = target.GetComponentsInChildren<Graphic>(true);
        foreach (Graphic graphic in graphics)
            graphic.raycastTarget = false;

        CanvasGroup[] canvasGroups = target.GetComponentsInChildren<CanvasGroup>(true);
        foreach (CanvasGroup canvasGroup in canvasGroups)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    private Vector2 GetTargetNormalizedPosition(SubtitleFollowSettings followSettings, SubtitleFollowState followState, out bool hasFace, out string mode)
    {
        hasFace = TryGetSelectedFace(out selectedFaceCenter, out selectedFaceRect);
        mode = hasFace ? "face" : "fallback";

        Vector2 normalizedPosition = centerFallbackAnchor;
        if (hasFace)
        {
            currentRawFaceCenter = selectedFaceCenter;
            normalizedPosition = GetFaceAnchorPosition(followSettings, out mode);
            currentRawFaceTarget = normalizedPosition;
            normalizedPosition = SmoothFaceAnchor(normalizedPosition, followSettings, followState);
            currentSmoothedFaceAnchor = normalizedPosition;
        }
        else if (!useCenterWhenNoFace)
        {
            normalizedPosition = AnchoredPositionToNormalized(target.anchoredPosition);
            mode = "hold";
        }
        else
        {
            followState.ResetFaceAnchorSmoothing();
            currentFaceHeight = 0f;
            currentAdaptivePadding = 0f;
            currentAnchorPosition = normalizedPosition;
            currentRawFaceCenter = Vector2.zero;
            currentRawFaceTarget = normalizedPosition;
            currentSmoothedFaceAnchor = normalizedPosition;
        }

        return ClampNormalized(normalizedPosition, followSettings, out currentPositionWasClamped);
    }

    private Vector2 GetFaceAnchorPosition(SubtitleFollowSettings followSettings, out string mode)
    {
        Rect transformedBounds = TransformProviderRect(selectedFaceRect);
        if (followSettings.UseTopOfFaceBox && HasValidBounds(transformedBounds))
        {
            mode = "bounds-top";
            currentFaceHeight = transformedBounds.height;
            currentAdaptivePadding = followSettings.ExtraGapAboveFace + (currentFaceHeight * followSettings.FaceBoxHeightGapMultiplier);
            currentAnchorPosition = new Vector2(
                transformedBounds.center.x,
                transformedBounds.yMax + currentAdaptivePadding);
            return currentAnchorPosition;
        }

        mode = "center-offset";
        currentFaceHeight = 0f;
        currentAdaptivePadding = 0f;
        currentAnchorPosition = TransformProviderPoint(selectedFaceCenter) + followSettings.CenterOffsetWhenNoFace;
        return currentAnchorPosition;
    }

    private bool TryGetSelectedFace(out Vector2 center, out Rect bounds)
    {
        if (coordinateRouter != null)
        {
            if (TryGetSelectedPersonTrack(out PersonFaceTrack track))
            {
                center = track.NormalizedCenter;
                bounds = track.NormalizedBounds;
                return true;
            }
        }

        if (faceProvider != null && faceProvider.HasFace)
        {
            center = faceProvider.NormalizedFaceCenter;
            bounds = faceProvider.NormalizedFaceRect;
            return true;
        }

        center = Vector2.zero;
        bounds = Rect.zero;
        return false;
    }

    private bool TryGetSelectedPersonTrack(out PersonFaceTrack track)
    {
        if (coordinateRouter == null)
        {
            track = default;
            return false;
        }

        switch (personToFollow)
        {
            case SubtitlePersonToFollow.P1:
                if (coordinateRouter.TryGetPersonTrack(1, out track))
                    return true;
                break;

            case SubtitlePersonToFollow.P2:
                if (coordinateRouter.TryGetPersonTrack(2, out track))
                    return true;
                break;
        }

        if (coordinateRouter.HasPrimaryPersonTrack)
        {
            track = coordinateRouter.PrimaryPersonTrack;
            return true;
        }

        track = default;
        return false;
    }

    private Vector2 SmoothFaceAnchor(Vector2 normalizedPosition, SubtitleFollowSettings followSettings, SubtitleFollowState followState)
    {
        if (!followSettings.SmoothFaceTracking)
        {
            followState.smoothedFaceAnchor = normalizedPosition;
            followState.hasSmoothedFaceAnchor = true;
            followState.faceAnchorVelocity = Vector2.zero;
            return normalizedPosition;
        }

        if (!followState.hasSmoothedFaceAnchor)
        {
            followState.smoothedFaceAnchor = normalizedPosition;
            followState.hasSmoothedFaceAnchor = true;
            followState.faceAnchorVelocity = Vector2.zero;
            return normalizedPosition;
        }

        followState.smoothedFaceAnchor = Vector2.SmoothDamp(
            followState.smoothedFaceAnchor,
            normalizedPosition,
            ref followState.faceAnchorVelocity,
            Mathf.Max(0.001f, followSettings.FaceTrackingSmoothTime));
        return followState.smoothedFaceAnchor;
    }

    private Vector2 TransformProviderPoint(Vector2 normalizedPosition)
    {
        // Display mapping is intentionally centralized in ActiveFacePositionProviderRouter.
        // Front AR comes from ARCamera/screen projection while Back 2D comes from MediaPipe
        // CPU image coordinates; they do not guarantee the same origin, handedness, or mirror.
        // Do not add another mirror/invert here or the center and bounds will diverge.
        return FaceCoordinateTransform.TransformPoint(normalizedPosition, GetActiveTransformSettings());
    }

    private Rect TransformProviderRect(Rect normalizedRect)
    {
        Rect transformedRect = FaceCoordinateTransform.TransformRect(normalizedRect, GetActiveTransformSettings());
        CameraModeMappingProfile profile = GetActiveRectAdjustmentProfile();
        LogRectAdjustment(profile);
        return profile.ApplyRectAdjustment(transformedRect);
    }

    private FaceCoordinateTransformSettings GetActiveTransformSettings()
    {
        return coordinateRouter != null
            ? coordinateRouter.CurrentTransformSettings
            : FaceCoordinateTransformSettings.CurrentMobileDefault;
    }

    private CameraModeMappingProfile GetActiveRectAdjustmentProfile()
    {
        return coordinateRouter != null && coordinateRouter.CurrentMode == MobileARModeController.MobileTrackingMode.BackFace2D
            ? coordinateRouter.Back2DProfile
            : CameraModeMappingProfile.CreateFrontARDefault();
    }

    private void LogRectAdjustment(CameraModeMappingProfile profile)
    {
        if (loggedRectAdjustment)
            return;

        loggedRectAdjustment = true;
        Debug.Log(
            $"[FaceRectAdjustment] consumer=SubtitleTarget " +
            $"useRectAdjustment={profile.useRectAdjustment} " +
            $"rectWidthMultiplier={profile.rectWidthMultiplier:0.###} " +
            $"rectHeightMultiplier={profile.rectHeightMultiplier:0.###} " +
            $"rectYOffsetNormalized={profile.rectYOffsetNormalized:0.###}",
            this);
    }

    private static bool HasValidBounds(Rect normalizedRect)
    {
        return normalizedRect.width > 0.001f && normalizedRect.height > 0.001f;
    }

    private Vector2 ClampNormalized(Vector2 normalizedPosition, SubtitleFollowSettings followSettings, out bool wasClamped)
    {
        Vector2 unclampedPosition = normalizedPosition;
        float minX = Mathf.Clamp01(followSettings.KeepInsideScreenPadding.x);
        float minY = Mathf.Clamp01(followSettings.KeepInsideScreenPadding.y);
        float maxX = Mathf.Max(minX, 1f - minX);
        float maxY = Mathf.Max(minY, 1f - minY);

        normalizedPosition.x = Mathf.Clamp(normalizedPosition.x, minX, maxX);
        normalizedPosition.y = Mathf.Clamp(normalizedPosition.y, minY, maxY);
        wasClamped = normalizedPosition != unclampedPosition;
        return normalizedPosition;
    }

    private Vector2 NormalizedToAnchoredPosition(Vector2 normalizedPosition)
    {
        Vector2 screenPoint = new Vector2(normalizedPosition.x * Screen.width, normalizedPosition.y * Screen.height);
        Camera eventCamera = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            eventCamera = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPoint, eventCamera, out Vector2 localPoint))
            localPoint = new Vector2(
                Mathf.Lerp(parentRect.rect.xMin, parentRect.rect.xMax, normalizedPosition.x),
                Mathf.Lerp(parentRect.rect.yMin, parentRect.rect.yMax, normalizedPosition.y));

        Vector2 anchorReference = GetAnchorReferencePoint();
        return localPoint - anchorReference;
    }

    private Vector2 AnchoredPositionToNormalized(Vector2 anchoredPosition)
    {
        Vector2 localPoint = anchoredPosition + GetAnchorReferencePoint();
        Rect rect = parentRect.rect;
        return new Vector2(
            Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x),
            Mathf.InverseLerp(rect.yMin, rect.yMax, localPoint.y));
    }

    private Vector2 GetAnchorReferencePoint()
    {
        Rect rect = parentRect.rect;
        Vector2 anchorCenter = (target.anchorMin + target.anchorMax) * 0.5f;
        return new Vector2(
            Mathf.Lerp(rect.xMin, rect.xMax, anchorCenter.x),
            Mathf.Lerp(rect.yMin, rect.yMax, anchorCenter.y));
    }

    private Vector3 AnchoredPositionToWorldPoint(Vector2 anchoredPosition)
    {
        if (parentRect == null || target == null)
            return Vector3.zero;

        Vector2 localPoint = anchoredPosition + GetAnchorReferencePoint();
        return parentRect.TransformPoint(localPoint);
    }

    private MobileARModeController.MobileTrackingMode GetActiveCameraMode()
    {
        return coordinateRouter != null
            ? coordinateRouter.CurrentMode
            : MobileARModeController.MobileTrackingMode.FrontFaceAR;
    }

    private SubtitleFollowSettings GetActiveFollowSettings()
    {
        return GetFollowSettings(GetActiveCameraMode());
    }

    private string GetActiveFollowSettingsName()
    {
        return GetActiveCameraMode() == MobileARModeController.MobileTrackingMode.BackFace2D
            ? "Back Camera Follow Settings"
            : "Front Camera Follow Settings";
    }

    private SubtitleFollowSettings GetFollowSettings(MobileARModeController.MobileTrackingMode cameraMode)
    {
        return cameraMode == MobileARModeController.MobileTrackingMode.BackFace2D
            ? backCameraFollowSettings
            : frontCameraFollowSettings;
    }

    private SubtitleFollowState GetFollowState(MobileARModeController.MobileTrackingMode cameraMode)
    {
        return cameraMode == MobileARModeController.MobileTrackingMode.BackFace2D
            ? backCameraFollowState
            : frontCameraFollowState;
    }

    private void ResetSmoothingWhenCameraModeChanges(MobileARModeController.MobileTrackingMode cameraMode, SubtitleFollowState activeState)
    {
        if (!hasPreviousCameraMode)
        {
            previousCameraMode = cameraMode;
            hasPreviousCameraMode = true;
            return;
        }

        if (previousCameraMode == cameraMode)
            return;

        previousCameraMode = cameraMode;
        activeState.uiVelocity = Vector2.zero;
        activeState.ResetFaceAnchorSmoothing();
    }

    private void LogPosition(bool hasFace, Vector2 normalizedPosition, Vector2 desiredAnchoredPosition, string mode, MobileARModeController.MobileTrackingMode cameraMode, SubtitleFollowSettings followSettings, SubtitleFollowState followState)
    {
        if (!debugLogs || Time.unscaledTime < nextLogTime)
            return;

        nextLogTime = Time.unscaledTime + 0.5f;

        string providerSource = faceProvider != null ? faceProvider.SourceName : "None";
        string activeMode = cameraMode.ToString();
        string routerProvider = coordinateRouter != null ? coordinateRouter.CurrentProviderName : "NoRouter";
        FaceCoordinateTransformSettings transformSettings = GetActiveTransformSettings();
        string targetName = target != null ? target.name : "null";
        Vector2 rawCenter = faceProvider != null ? faceProvider.NormalizedFaceCenter : Vector2.zero;
        Vector2 transformedCenter = faceProvider != null ? TransformProviderPoint(rawCenter) : Vector2.zero;
        Rect rawBounds = faceProvider != null ? faceProvider.NormalizedFaceRect : Rect.zero;
        Rect transformedBounds = faceProvider != null ? TransformProviderRect(rawBounds) : Rect.zero;
        bool boundsValid = HasValidBounds(rawBounds);
        string state =
            $"activeMode={activeMode} followSettings={GetActiveFollowSettingsName()} faceTrackingSmoothTime={followSettings.FaceTrackingSmoothTime:0.###} subtitleMovementSmoothTime={followSettings.SubtitleMovementSmoothTime:0.###} faceBoxHeightGapMultiplier={followSettings.FaceBoxHeightGapMultiplier:0.###} " +
            $"provider={providerSource} routerProvider={routerProvider} usingRouterSettings={(coordinateRouter != null)} transform={transformSettings} hasFace={hasFace} boundsValid={boundsValid} " +
            $"rawCenter={rawCenter} rawCenter.x={rawCenter.x:0.000} transformedCenter={transformedCenter} transformedCenter.x={transformedCenter.x:0.000} " +
            $"rawBounds.center.x={rawBounds.center.x:0.000} transformedBounds.center.x={transformedBounds.center.x:0.000} " +
            $"rawBounds=min({rawBounds.xMin:0.000},{rawBounds.yMin:0.000}) max({rawBounds.xMax:0.000},{rawBounds.yMax:0.000}) size({rawBounds.width:0.000},{rawBounds.height:0.000}) " +
            $"transformedBounds=min({transformedBounds.xMin:0.000},{transformedBounds.yMin:0.000}) max({transformedBounds.xMax:0.000},{transformedBounds.yMax:0.000}) size({transformedBounds.width:0.000},{transformedBounds.height:0.000}) " +
            $"rawFaceCenter={currentRawFaceCenter} rawFaceTarget={currentRawFaceTarget} smoothedFaceAnchor={currentSmoothedFaceAnchor} " +
            $"desiredSubtitlePosition={desiredAnchoredPosition} actualSubtitlePosition={target.anchoredPosition} positionError={(desiredAnchoredPosition - target.anchoredPosition).magnitude:0.###} " +
            $"uiVelocityMagnitude={followState.uiVelocity.magnitude:0.###} faceAnchorVelocityMagnitude={followState.faceAnchorVelocity.magnitude:0.###} clamped={currentPositionWasClamped} " +
            $"normalized={normalizedPosition} mode={mode} useTopOfFaceBox={followSettings.UseTopOfFaceBox} " +
            $"faceHeight={currentFaceHeight:0.000} adaptivePadding={currentAdaptivePadding:0.000} anchor={currentAnchorPosition} " +
            $"extraGapAboveFace={followSettings.ExtraGapAboveFace} heightGapMultiplier={followSettings.FaceBoxHeightGapMultiplier} centerOffset={followSettings.CenterOffsetWhenNoFace} target={targetName}";
        if (state == lastLogState)
            return;

        lastLogState = state;
        Debug.Log($"[FaceCoordinateMapping] consumer=SubtitleTarget {state}", this);
    }

    private void LogBackCameraFacePosition(bool hasFace, Vector2 normalizedPosition, Vector2 desiredAnchoredPosition, string mode, MobileARModeController.MobileTrackingMode cameraMode, SubtitleFollowSettings followSettings)
    {
        if (!debugBackCameraFacePosition || !hasFace || coordinateRouter == null)
            return;

        if (cameraMode != MobileARModeController.MobileTrackingMode.BackFace2D)
            return;

        Vector2 screenPosition = new Vector2(normalizedPosition.x * Screen.width, normalizedPosition.y * Screen.height);
        Debug.Log(
            $"[BackFace2DAudit] FinalUIDebug " +
            $"activeMode={cameraMode} followSettings={GetActiveFollowSettingsName()} faceTrackingSmoothTime={followSettings.FaceTrackingSmoothTime:0.###} subtitleMovementSmoothTime={followSettings.SubtitleMovementSmoothTime:0.###} faceBoxHeightGapMultiplier={followSettings.FaceBoxHeightGapMultiplier:0.###} " +
            $"mode={mode} " +
            $"rawProviderCenter={faceProvider.NormalizedFaceCenter} " +
            $"rawProviderRect={faceProvider.NormalizedFaceRect} " +
            $"transform={GetActiveTransformSettings()} " +
            $"correctedUiNormalized={normalizedPosition} " +
            $"screenPosition={screenPosition} " +
            $"desiredAnchoredPosition={desiredAnchoredPosition} " +
            $"currentAnchoredPosition={target.anchoredPosition} " +
            $"screenSize={Screen.width}x{Screen.height} " +
            $"screenOrientation={Screen.orientation}",
            this);
    }
}
