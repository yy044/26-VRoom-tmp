using UnityEngine;
using UnityEngine.UI;

public class FaceDebugOverlay : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Any MonoBehaviour implementing IFacePositionProvider. Use the same provider as FaceAnchoredSubtitleTarget.")]
    [SerializeField] private MonoBehaviour faceProviderBehaviour;

    [Tooltip("Actual subtitle RectTransform to visualize.")]
    [SerializeField] private RectTransform subtitleTarget;

    [Header("Visibility")]
    [SerializeField] private bool showFaceCenter = true;
    [SerializeField] private bool showSubtitleTarget = true;
    [SerializeField] private bool showBoundingBox = true;
    [SerializeField] private bool showConnectionLine = true;
    [SerializeField] private bool showBoundsTopCenter = true;
    [SerializeField] private bool showBoundsAnchor = true;
    [SerializeField] private bool showAdaptivePadding = true;
    [SerializeField] private bool showFallbackOffsetAnchor = true;
    [SerializeField] private bool showCenterFallbackWhenNoFace = true;
    [SerializeField] private bool autoHideWhenNoFace = true;

    [Header("Subtitle Anchor Preview")]
    [Tooltip("Match FaceAnchoredSubtitleTarget.useBoundsTopAnchor.")]
    [SerializeField] private bool useBoundsTopAnchor = true;

    [Tooltip("Match FaceAnchoredSubtitleTarget.baseBoundsPaddingNormalized.")]
    [SerializeField] private float baseBoundsPaddingNormalized = 0.02f;

    [Tooltip("Match FaceAnchoredSubtitleTarget.boundsHeightPaddingMultiplier.")]
    [SerializeField] private float boundsHeightPaddingMultiplier = 0.15f;

    [Tooltip("Match FaceAnchoredSubtitleTarget.fallbackCenterOffsetNormalized.")]
    [SerializeField] private Vector2 fallbackCenterOffsetNormalized = new Vector2(0f, 0.12f);

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

    [Header("Debug Colors")]
    [SerializeField] private Color faceCenterColor = new Color(0f, 1f, 0.2f, 0.9f);
    [SerializeField] private Color subtitleTargetColor = new Color(1f, 0.85f, 0f, 0.9f);
    [SerializeField] private Color boundingBoxColor = new Color(0f, 0.75f, 1f, 0.9f);
    [SerializeField] private Color connectionLineColor = new Color(1f, 1f, 1f, 0.65f);
    [SerializeField] private Color boundsTopCenterColor = new Color(0.35f, 1f, 1f, 0.9f);
    [SerializeField] private Color boundsAnchorColor = new Color(1f, 0.45f, 0f, 0.95f);
    [SerializeField] private Color adaptivePaddingColor = new Color(1f, 0.55f, 0f, 0.85f);
    [SerializeField] private Color fallbackOffsetAnchorColor = new Color(1f, 0f, 1f, 0.85f);
    [SerializeField] private Color fallbackCenterColor = new Color(1f, 0.25f, 0.25f, 0.8f);

    [Header("Sizing")]
    [SerializeField] private Vector2 faceCenterSize = new Vector2(18f, 18f);
    [SerializeField] private Vector2 subtitleTargetSize = new Vector2(22f, 22f);
    [SerializeField] private Vector2 boundsTopCenterSize = new Vector2(12f, 12f);
    [SerializeField] private Vector2 boundsAnchorSize = new Vector2(16f, 16f);
    [SerializeField] private Vector2 fallbackOffsetAnchorSize = new Vector2(14f, 14f);
    [SerializeField] private Vector2 fallbackCenterSize = new Vector2(14f, 14f);
    [SerializeField] private float boundingBoxThickness = 3f;
    [SerializeField] private float connectionLineThickness = 2f;
    [SerializeField] private float adaptivePaddingLineThickness = 2f;

    [Header("Logging")]
    [SerializeField] private bool debugLogs = false;

    private IFacePositionProvider faceProvider;
    private RectTransform overlayRoot;
    private RectTransform parentRect;
    private Canvas canvas;
    private Image faceCenterImage;
    private Image subtitleTargetImage;
    private Image boundsTopCenterImage;
    private Image boundsAnchorImage;
    private Image adaptivePaddingLineImage;
    private Image fallbackOffsetAnchorImage;
    private Image fallbackCenterImage;
    private Image connectionLineImage;
    private readonly Image[] boundingBoxEdges = new Image[4];
    private float nextLogTime;
    private string lastLogState;

    private void Awake()
    {
        AutoBind();
        EnsureOverlay();
    }

    private void OnValidate()
    {
        boundingBoxThickness = Mathf.Max(1f, boundingBoxThickness);
        connectionLineThickness = Mathf.Max(1f, connectionLineThickness);
        adaptivePaddingLineThickness = Mathf.Max(1f, adaptivePaddingLineThickness);
        baseBoundsPaddingNormalized = Mathf.Max(0f, baseBoundsPaddingNormalized);
        boundsHeightPaddingMultiplier = Mathf.Max(0f, boundsHeightPaddingMultiplier);
    }

    private void LateUpdate()
    {
        AutoBind();
        EnsureOverlay();

        if (overlayRoot == null || parentRect == null)
            return;

        faceProvider = faceProviderBehaviour as IFacePositionProvider;
        bool hasFace = faceProvider != null && faceProvider.HasFace;
        bool hasBounds = hasFace && HasBoundingBox(faceProvider.NormalizedFaceRect);
        bool usingBoundsAnchor = hasBounds && useBoundsTopAnchor;

        SetVisible(faceCenterImage, showFaceCenter && hasFace);
        SetVisible(subtitleTargetImage, showSubtitleTarget && (hasFace || !autoHideWhenNoFace));
        SetVisible(connectionLineImage, showConnectionLine && hasFace && subtitleTarget != null);
        SetVisible(boundsTopCenterImage, showBoundsTopCenter && usingBoundsAnchor);
        SetVisible(boundsAnchorImage, showBoundsAnchor && usingBoundsAnchor);
        SetVisible(adaptivePaddingLineImage, showAdaptivePadding && usingBoundsAnchor);
        SetVisible(fallbackOffsetAnchorImage, showFallbackOffsetAnchor && hasFace && !usingBoundsAnchor);
        SetVisible(fallbackCenterImage, showCenterFallbackWhenNoFace && !hasFace);

        for (int i = 0; i < boundingBoxEdges.Length; i++)
            SetVisible(boundingBoxEdges[i], showBoundingBox && hasBounds);

        if (hasFace)
        {
            Vector2 faceCenter = NormalizedToAnchoredPosition(TransformProviderPoint(faceProvider.NormalizedFaceCenter));
            Vector2 anchorPoint = GetSubtitleAnchorPoint(
                faceProvider.NormalizedFaceCenter,
                faceProvider.NormalizedFaceRect,
                out string anchorMode,
                out Vector2 boundsTopCenter,
                out float adaptivePadding);
            PositionImage(faceCenterImage, faceCenter, faceCenterSize, faceCenterColor);
            PositionBoundingBox(faceProvider.NormalizedFaceRect);

            if (anchorMode == "bounds-top")
            {
                Vector2 topCenterPosition = NormalizedToAnchoredPosition(boundsTopCenter);
                Vector2 anchorPosition = NormalizedToAnchoredPosition(anchorPoint);
                PositionImage(boundsTopCenterImage, topCenterPosition, boundsTopCenterSize, boundsTopCenterColor);
                PositionLine(adaptivePaddingLineImage, topCenterPosition, anchorPosition, adaptivePaddingColor, adaptivePaddingLineThickness);
                PositionImage(boundsAnchorImage, NormalizedToAnchoredPosition(anchorPoint), boundsAnchorSize, boundsAnchorColor);
            }
            else
            {
                PositionImage(fallbackOffsetAnchorImage, NormalizedToAnchoredPosition(anchorPoint), fallbackOffsetAnchorSize, fallbackOffsetAnchorColor);
            }

            if (subtitleTarget != null)
            {
                Vector2 subtitlePosition = WorldToOverlayAnchoredPosition(subtitleTarget.TransformPoint(subtitleTarget.rect.center));
                PositionImage(subtitleTargetImage, subtitlePosition, subtitleTargetSize, subtitleTargetColor);
                PositionLine(connectionLineImage, faceCenter, subtitlePosition, connectionLineColor);
            }
        }
        else
        {
            Vector2 fallbackCenter = NormalizedToAnchoredPosition(new Vector2(0.5f, 0.5f));
            PositionImage(fallbackCenterImage, fallbackCenter, fallbackCenterSize, fallbackCenterColor);

            if (!autoHideWhenNoFace && subtitleTarget != null)
            {
                Vector2 subtitlePosition = WorldToOverlayAnchoredPosition(subtitleTarget.TransformPoint(subtitleTarget.rect.center));
                PositionImage(subtitleTargetImage, subtitlePosition, subtitleTargetSize, subtitleTargetColor);
            }
        }

        LogState(hasFace);
    }

    private void AutoBind()
    {
        if (faceProviderBehaviour == null)
            faceProviderBehaviour = FindFirstObjectByType<ActiveFacePositionProviderRouter>(FindObjectsInactive.Include);

        if (subtitleTarget == null)
        {
            FaceAnchoredSubtitleTarget targetController = FindFirstObjectByType<FaceAnchoredSubtitleTarget>(FindObjectsInactive.Include);
            if (targetController != null)
                subtitleTarget = targetController.GetComponent<RectTransform>();
        }

        if (subtitleTarget != null)
        {
            parentRect = subtitleTarget.parent as RectTransform;
            canvas = subtitleTarget.GetComponentInParent<Canvas>();
        }
    }

    private void EnsureOverlay()
    {
        if (parentRect == null || overlayRoot != null)
            return;

        overlayRoot = CreateRect("FaceDebugOverlayRoot", parentRect);
        overlayRoot.anchorMin = Vector2.zero;
        overlayRoot.anchorMax = Vector2.one;
        overlayRoot.offsetMin = Vector2.zero;
        overlayRoot.offsetMax = Vector2.zero;
        overlayRoot.pivot = new Vector2(0.5f, 0.5f);
        overlayRoot.SetAsLastSibling();

        faceCenterImage = CreateImage("FaceCenter", overlayRoot);
        subtitleTargetImage = CreateImage("SubtitleTarget", overlayRoot);
        boundsTopCenterImage = CreateImage("BoundsTopCenter", overlayRoot);
        boundsAnchorImage = CreateImage("BoundsTopAnchor", overlayRoot);
        adaptivePaddingLineImage = CreateImage("AdaptivePaddingLine", overlayRoot);
        fallbackOffsetAnchorImage = CreateImage("FallbackOffsetAnchor", overlayRoot);
        fallbackCenterImage = CreateImage("CenterFallback", overlayRoot);
        connectionLineImage = CreateImage("FaceToSubtitleLine", overlayRoot);

        for (int i = 0; i < boundingBoxEdges.Length; i++)
            boundingBoxEdges[i] = CreateImage($"FaceBoundsEdge{i}", overlayRoot);

        SetAllHidden();
    }

    private static RectTransform CreateRect(string objectName, RectTransform parent)
    {
        GameObject child = new GameObject(objectName, typeof(RectTransform));
        RectTransform rectTransform = child.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        return rectTransform;
    }

    private static Image CreateImage(string objectName, RectTransform parent)
    {
        RectTransform rectTransform = CreateRect(objectName, parent);
        Image image = rectTransform.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    private void SetAllHidden()
    {
        SetVisible(faceCenterImage, false);
        SetVisible(subtitleTargetImage, false);
        SetVisible(boundsTopCenterImage, false);
        SetVisible(boundsAnchorImage, false);
        SetVisible(adaptivePaddingLineImage, false);
        SetVisible(fallbackOffsetAnchorImage, false);
        SetVisible(fallbackCenterImage, false);
        SetVisible(connectionLineImage, false);

        for (int i = 0; i < boundingBoxEdges.Length; i++)
            SetVisible(boundingBoxEdges[i], false);
    }

    private static void SetVisible(Image image, bool visible)
    {
        if (image != null && image.gameObject.activeSelf != visible)
            image.gameObject.SetActive(visible);
    }

    private static void PositionImage(Image image, Vector2 anchoredPosition, Vector2 size, Color color)
    {
        if (image == null)
            return;

        RectTransform rectTransform = image.rectTransform;
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;
        rectTransform.localRotation = Quaternion.identity;
        image.color = color;
    }

    private void PositionBoundingBox(Rect normalizedRect)
    {
        if (!HasBoundingBox(normalizedRect))
            return;

        Rect transformedRect = TransformProviderRect(normalizedRect);
        Vector2 min = NormalizedToAnchoredPosition(new Vector2(transformedRect.xMin, transformedRect.yMin));
        Vector2 max = NormalizedToAnchoredPosition(new Vector2(transformedRect.xMax, transformedRect.yMax));
        float width = Mathf.Abs(max.x - min.x);
        float height = Mathf.Abs(max.y - min.y);
        Vector2 center = (min + max) * 0.5f;

        PositionImage(boundingBoxEdges[0], new Vector2(center.x, max.y), new Vector2(width, boundingBoxThickness), boundingBoxColor);
        PositionImage(boundingBoxEdges[1], new Vector2(center.x, min.y), new Vector2(width, boundingBoxThickness), boundingBoxColor);
        PositionImage(boundingBoxEdges[2], new Vector2(min.x, center.y), new Vector2(boundingBoxThickness, height), boundingBoxColor);
        PositionImage(boundingBoxEdges[3], new Vector2(max.x, center.y), new Vector2(boundingBoxThickness, height), boundingBoxColor);
    }

    private Vector2 GetSubtitleAnchorPoint(
        Vector2 faceCenter,
        Rect faceBounds,
        out string anchorMode,
        out Vector2 boundsTopCenter,
        out float adaptivePadding)
    {
        Rect transformedBounds = TransformProviderRect(faceBounds);
        if (useBoundsTopAnchor && HasBoundingBox(transformedBounds))
        {
            anchorMode = "bounds-top";
            adaptivePadding = baseBoundsPaddingNormalized + (transformedBounds.height * boundsHeightPaddingMultiplier);
            boundsTopCenter = new Vector2(transformedBounds.center.x, transformedBounds.yMax);
            return boundsTopCenter + new Vector2(0f, adaptivePadding);
        }

        anchorMode = "center-offset";
        adaptivePadding = 0f;
        boundsTopCenter = Vector2.zero;
        return TransformProviderPoint(faceCenter) + fallbackCenterOffsetNormalized;
    }

    private void PositionLine(Image image, Vector2 start, Vector2 end, Color color)
    {
        PositionLine(image, start, end, color, connectionLineThickness);
    }

    private void PositionLine(Image image, Vector2 start, Vector2 end, Color color, float thickness)
    {
        if (image == null)
            return;

        Vector2 delta = end - start;
        RectTransform rectTransform = image.rectTransform;
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = (start + end) * 0.5f;
        rectTransform.sizeDelta = new Vector2(delta.magnitude, thickness);
        rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        image.color = color;
    }

    private Vector2 NormalizedToAnchoredPosition(Vector2 normalizedPosition)
    {
        Vector2 screenPoint = new Vector2(normalizedPosition.x * Screen.width, normalizedPosition.y * Screen.height);
        Camera eventCamera = GetEventCamera();

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRoot, screenPoint, eventCamera, out Vector2 localPoint))
            localPoint = new Vector2(
                Mathf.Lerp(overlayRoot.rect.xMin, overlayRoot.rect.xMax, normalizedPosition.x),
                Mathf.Lerp(overlayRoot.rect.yMin, overlayRoot.rect.yMax, normalizedPosition.y));

        return localPoint;
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

    private Vector2 WorldToOverlayAnchoredPosition(Vector3 worldPosition)
    {
        Camera eventCamera = GetEventCamera();
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(eventCamera, worldPosition);

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRoot, screenPoint, eventCamera, out Vector2 localPoint))
            return localPoint;

        return Vector2.zero;
    }

    private Camera GetEventCamera()
    {
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;

        return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
    }

    private static bool HasBoundingBox(Rect normalizedRect)
    {
        return normalizedRect.width > 0.001f && normalizedRect.height > 0.001f;
    }

    private void LogState(bool hasFace)
    {
        if (!debugLogs || Time.unscaledTime < nextLogTime)
            return;

        nextLogTime = Time.unscaledTime + 0.5f;

        string providerSource = faceProvider != null ? faceProvider.SourceName : "None";
        string targetName = subtitleTarget != null ? subtitleTarget.name : "null";
        Vector2 center = faceProvider != null ? faceProvider.NormalizedFaceCenter : Vector2.zero;
        Vector2 transformedCenter = faceProvider != null ? TransformProviderPoint(center) : Vector2.zero;
        Rect rect = faceProvider != null ? faceProvider.NormalizedFaceRect : Rect.zero;
        Rect transformedRect = faceProvider != null ? TransformProviderRect(rect) : Rect.zero;
        float faceHeight = transformedRect.height;
        float adaptivePadding = faceHeight > 0.001f ? baseBoundsPaddingNormalized + (faceHeight * boundsHeightPaddingMultiplier) : 0f;
        string state =
            $"provider={providerSource} hasFace={hasFace} " +
            $"rawCenter={center} transformedCenter={transformedCenter} " +
            $"rawBounds=min({rect.xMin:0.000},{rect.yMin:0.000}) max({rect.xMax:0.000},{rect.yMax:0.000}) size({rect.width:0.000},{rect.height:0.000}) " +
            $"transformedBounds=min({transformedRect.xMin:0.000},{transformedRect.yMin:0.000}) max({transformedRect.xMax:0.000},{transformedRect.yMax:0.000}) size({transformedRect.width:0.000},{transformedRect.height:0.000}) " +
            $"faceHeight={faceHeight:0.000} adaptivePadding={adaptivePadding:0.000} useBoundsTopAnchor={useBoundsTopAnchor} " +
            $"basePadding={baseBoundsPaddingNormalized} heightPaddingMultiplier={boundsHeightPaddingMultiplier} fallbackOffset={fallbackCenterOffsetNormalized} " +
            $"mirrorX={mirrorX} invertY={invertY} swapXY={swapXY} rotate90Clockwise={rotate90Clockwise} rotate90CounterClockwise={rotate90CounterClockwise} subtitleTarget={targetName}";
        if (state == lastLogState)
            return;

        lastLogState = state;
        Debug.Log($"[FaceDebugOverlay] {state}", this);
    }
}
