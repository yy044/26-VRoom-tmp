using UnityEngine;

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

    [Header("Face Position")]
    [Tooltip("Normalized offset added to the detected face center when bounds anchoring is off or bounds are unavailable.")]
    [SerializeField] private Vector2 fallbackCenterOffsetNormalized = new Vector2(0f, 0.12f);

    [Tooltip("Use the transformed face bounds top center as the subtitle anchor when bounds are available.")]
    [SerializeField] private bool useBoundsTopAnchor = true;

    [Tooltip("Constant normalized spacing above the transformed face box.")]
    [SerializeField] private float baseBoundsPaddingNormalized = 0.02f;

    [Tooltip("Additional normalized spacing derived from transformed face height.")]
    [SerializeField] private float boundsHeightPaddingMultiplier = 0.15f;

    [Tooltip("Smooth the normalized bounds/fallback face anchor before converting to UI space.")]
    [SerializeField] private bool smoothBoundsAnchor = true;

    [Tooltip("SmoothDamp time for the normalized bounds/fallback face anchor.")]
    [SerializeField] private float boundsAnchorSmoothTime = 0.06f;

    [Tooltip("Normalized padding that clamps the target inside the screen.")]
    [SerializeField] private Vector2 screenPaddingNormalized = new Vector2(0.05f, 0.08f);

    [Tooltip("SmoothDamp time used when moving the subtitle target.")]
    [SerializeField] private float smoothTime = 0.08f;

    [Header("Coordinate Mapping")]
    [Tooltip("Mirror the provider's normalized X coordinate before mapping to UI.")]
    [SerializeField] private bool mirrorX = true;

    [Tooltip("Invert the provider's normalized Y coordinate before mapping to UI.")]
    [SerializeField] private bool invertY = true;

    [Tooltip("Swap normalized X/Y before optional 90 degree rotations.")]
    [SerializeField] private bool swapXY = false;

    [Tooltip("Rotate normalized coordinates 90 degrees clockwise around the center.")]
    [SerializeField] private bool rotate90Clockwise = false;

    [Tooltip("Rotate normalized coordinates 90 degrees counter-clockwise around the center.")]
    [SerializeField] private bool rotate90CounterClockwise = false;

    [Header("Debug")]
    [Tooltip("Emit throttled subtitle positioning logs.")]
    [SerializeField] private bool debugLogs = false;

    [Header("Diagnostics")]
    [SerializeField] private float currentFaceHeight;
    [SerializeField] private float currentAdaptivePadding;
    [SerializeField] private Vector2 currentAnchorPosition;

    private IFacePositionProvider faceProvider;
    private RectTransform parentRect;
    private Canvas canvas;
    private Vector2 velocity;
    private Vector2 anchorVelocity;
    private Vector2 smoothedAnchor;
    private bool hasSmoothedAnchor;
    private float nextLogTime;
    private string lastLogState;

    private void Awake()
    {
        AutoBind();
    }

    private void OnValidate()
    {
        if (target == null)
            target = GetComponent<RectTransform>();

        smoothTime = Mathf.Max(0f, smoothTime);
        baseBoundsPaddingNormalized = Mathf.Max(0f, baseBoundsPaddingNormalized);
        boundsHeightPaddingMultiplier = Mathf.Max(0f, boundsHeightPaddingMultiplier);
        boundsAnchorSmoothTime = Mathf.Max(0f, boundsAnchorSmoothTime);
    }

    private void Update()
    {
        AutoBind();

        if (target == null || parentRect == null)
            return;

        Vector2 normalizedPosition = GetTargetNormalizedPosition(out bool hasFace, out string mode);
        Vector2 desiredAnchoredPosition = NormalizedToAnchoredPosition(normalizedPosition);
        target.anchoredPosition = Vector2.SmoothDamp(
            target.anchoredPosition,
            desiredAnchoredPosition,
            ref velocity,
            Mathf.Max(0.001f, smoothTime));

        LogPosition(hasFace, normalizedPosition, mode);
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
    }

    private Vector2 GetTargetNormalizedPosition(out bool hasFace, out string mode)
    {
        hasFace = faceProvider != null && faceProvider.HasFace;
        mode = hasFace ? "face" : "fallback";

        Vector2 normalizedPosition = centerFallbackAnchor;
        if (hasFace)
        {
            normalizedPosition = GetFaceAnchorPosition(out mode);
            normalizedPosition = SmoothFaceAnchor(normalizedPosition);
        }
        else if (!useCenterWhenNoFace)
        {
            normalizedPosition = AnchoredPositionToNormalized(target.anchoredPosition);
            mode = "hold";
        }
        else
        {
            hasSmoothedAnchor = false;
            anchorVelocity = Vector2.zero;
            currentFaceHeight = 0f;
            currentAdaptivePadding = 0f;
            currentAnchorPosition = normalizedPosition;
        }

        return ClampNormalized(normalizedPosition);
    }

    private Vector2 GetFaceAnchorPosition(out string mode)
    {
        Rect transformedBounds = TransformProviderRect(faceProvider.NormalizedFaceRect);
        if (useBoundsTopAnchor && HasValidBounds(transformedBounds))
        {
            mode = "bounds-top";
            currentFaceHeight = transformedBounds.height;
            currentAdaptivePadding = baseBoundsPaddingNormalized + (currentFaceHeight * boundsHeightPaddingMultiplier);
            currentAnchorPosition = new Vector2(
                transformedBounds.center.x,
                transformedBounds.yMax + currentAdaptivePadding);
            return currentAnchorPosition;
        }

        mode = "center-offset";
        currentFaceHeight = 0f;
        currentAdaptivePadding = 0f;
        currentAnchorPosition = TransformProviderPoint(faceProvider.NormalizedFaceCenter) + fallbackCenterOffsetNormalized;
        return currentAnchorPosition;
    }

    private Vector2 SmoothFaceAnchor(Vector2 normalizedPosition)
    {
        if (!smoothBoundsAnchor)
        {
            smoothedAnchor = normalizedPosition;
            hasSmoothedAnchor = true;
            anchorVelocity = Vector2.zero;
            return normalizedPosition;
        }

        if (!hasSmoothedAnchor)
        {
            smoothedAnchor = normalizedPosition;
            hasSmoothedAnchor = true;
            anchorVelocity = Vector2.zero;
            return normalizedPosition;
        }

        smoothedAnchor = Vector2.SmoothDamp(
            smoothedAnchor,
            normalizedPosition,
            ref anchorVelocity,
            Mathf.Max(0.001f, boundsAnchorSmoothTime));
        return smoothedAnchor;
    }

    private Vector2 TransformProviderPoint(Vector2 normalizedPosition)
    {
        return FaceCoordinateTransform.TransformPoint(
            normalizedPosition,
            mirrorX,
            invertY,
            swapXY,
            rotate90Clockwise,
            rotate90CounterClockwise);
    }

    private Rect TransformProviderRect(Rect normalizedRect)
    {
        return FaceCoordinateTransform.TransformRect(
            normalizedRect,
            mirrorX,
            invertY,
            swapXY,
            rotate90Clockwise,
            rotate90CounterClockwise);
    }

    private static bool HasValidBounds(Rect normalizedRect)
    {
        return normalizedRect.width > 0.001f && normalizedRect.height > 0.001f;
    }

    private Vector2 ClampNormalized(Vector2 normalizedPosition)
    {
        float minX = Mathf.Clamp01(screenPaddingNormalized.x);
        float minY = Mathf.Clamp01(screenPaddingNormalized.y);
        float maxX = Mathf.Max(minX, 1f - minX);
        float maxY = Mathf.Max(minY, 1f - minY);

        normalizedPosition.x = Mathf.Clamp(normalizedPosition.x, minX, maxX);
        normalizedPosition.y = Mathf.Clamp(normalizedPosition.y, minY, maxY);
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

    private void LogPosition(bool hasFace, Vector2 normalizedPosition, string mode)
    {
        if (!debugLogs || Time.unscaledTime < nextLogTime)
            return;

        nextLogTime = Time.unscaledTime + 0.5f;

        string providerSource = faceProvider != null ? faceProvider.SourceName : "None";
        string targetName = target != null ? target.name : "null";
        Vector2 rawCenter = faceProvider != null ? faceProvider.NormalizedFaceCenter : Vector2.zero;
        Vector2 transformedCenter = faceProvider != null ? TransformProviderPoint(rawCenter) : Vector2.zero;
        Rect rawBounds = faceProvider != null ? faceProvider.NormalizedFaceRect : Rect.zero;
        Rect transformedBounds = faceProvider != null ? TransformProviderRect(rawBounds) : Rect.zero;
        string state =
            $"provider={providerSource} hasFace={hasFace} " +
            $"rawCenter={rawCenter} transformedCenter={transformedCenter} " +
            $"rawBounds=min({rawBounds.xMin:0.000},{rawBounds.yMin:0.000}) max({rawBounds.xMax:0.000},{rawBounds.yMax:0.000}) size({rawBounds.width:0.000},{rawBounds.height:0.000}) " +
            $"transformedBounds=min({transformedBounds.xMin:0.000},{transformedBounds.yMin:0.000}) max({transformedBounds.xMax:0.000},{transformedBounds.yMax:0.000}) size({transformedBounds.width:0.000},{transformedBounds.height:0.000}) " +
            $"normalized={normalizedPosition} mode={mode} useBoundsTopAnchor={useBoundsTopAnchor} " +
            $"faceHeight={currentFaceHeight:0.000} adaptivePadding={currentAdaptivePadding:0.000} anchor={currentAnchorPosition} " +
            $"basePadding={baseBoundsPaddingNormalized} heightPaddingMultiplier={boundsHeightPaddingMultiplier} fallbackOffset={fallbackCenterOffsetNormalized} " +
            $"mirrorX={mirrorX} invertY={invertY} swapXY={swapXY} rotate90Clockwise={rotate90Clockwise} rotate90CounterClockwise={rotate90CounterClockwise} target={targetName}";
        if (state == lastLogState)
            return;

        lastLogState = state;
        Debug.Log($"[SubtitlePositionAudit] {state}", this);
    }
}
