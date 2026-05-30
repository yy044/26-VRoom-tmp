using TMPro;
using UnityEngine;

public class MobileARHeadTracker : MonoBehaviour
{
    [Header("References")]
    public MobileARFaceTrackingRunner runner;
    public RectTransform displayRect;
    public RectTransform headLabel;
    public TextMeshProUGUI headLabelText;

    [Header("Label")]
    public string labelText = "Hello!";
    public bool useFaceTrackingPosition = true;
    public Vector2 fixedAnchoredPosition = new Vector2(0f, -260f);
    public float yOffset = 40f;
    public bool mirrorX = false;
    public float smoothing = 15f;
    public bool useFixedPositionWhenFaceMissing = true;
    public bool setFallbackTextWhenEmpty = false;

    private MobileARFaceTrackingRunner.MobileFaceDetection result;
    private string lastPositionAuditState;

    private void Awake()
    {
        AutoBind();
    }

    private void Start()
    {
        if (headLabel != null && useFaceTrackingPosition)
            headLabel.gameObject.SetActive(false);
    }

    private void Update()
    {
        AutoBind();

        if (runner == null || headLabel == null)
            return;

        if (!useFaceTrackingPosition)
        {
            ApplyFixedPosition();
            LogPositionSource("fixed-mode");
            return;
        }

        if (!runner.TryGetLatestResult(ref result) || !result.hasDetection)
        {
            if (useFixedPositionWhenFaceMissing)
            {
                ApplyFixedPosition();
                LogPositionSource("fixed-missing-face");
            }
            else
            {
                headLabel.gameObject.SetActive(false);
                LogPositionSource("hidden-missing-face");
            }
            return;
        }

        if (useFixedPositionWhenFaceMissing && result.isFallback && runner.trackingSource == MobileARFaceTrackingRunner.TrackingSource.ARFaceManager)
        {
            ApplyFixedPosition();
            LogPositionSource("fixed-fallback");
            return;
        }

        RectTransform targetRect = displayRect != null ? displayRect : GetCanvasRect();
        if (targetRect == null)
            return;

        Vector2 normalizedCenter = result.normalizedCenter;
        if (mirrorX)
            normalizedCenter.x = 1f - normalizedCenter.x;

        float faceTopY = result.normalizedRect.yMin;
        float localX = (normalizedCenter.x - 0.5f) * targetRect.rect.width;
        float localY = (0.5f - faceTopY) * targetRect.rect.height + yOffset;
        Vector3 targetWorldPos = targetRect.TransformPoint(new Vector3(localX, localY, 0f));

        float t = 1f - Mathf.Exp(-Mathf.Max(0.1f, smoothing) * Time.deltaTime);
        headLabel.position = Vector3.Lerp(headLabel.position, targetWorldPos, t);
        headLabel.gameObject.SetActive(true);
        LogPositionSource("face-tracking");

        if (setFallbackTextWhenEmpty && headLabelText != null && string.IsNullOrWhiteSpace(headLabelText.text))
            headLabelText.text = labelText;
    }

    private void ApplyFixedPosition()
    {
        headLabel.gameObject.SetActive(true);

        headLabel.anchorMin = new Vector2(0.5f, 0.5f);
        headLabel.anchorMax = new Vector2(0.5f, 0.5f);
        headLabel.pivot = new Vector2(0.5f, 0.5f);
        headLabel.anchoredPosition = fixedAnchoredPosition;

        if (setFallbackTextWhenEmpty && headLabelText != null && string.IsNullOrWhiteSpace(headLabelText.text))
            headLabelText.text = labelText;
    }

    private void AutoBind()
    {
        if (runner == null)
            runner = FindFirstObjectByType<MobileARFaceTrackingRunner>(FindObjectsInactive.Include);

        if (headLabel == null)
        {
            Transform found = FindTransformByName("HeadLabel");
            if (found != null)
                headLabel = found as RectTransform;
        }

        if (headLabelText == null && headLabel != null)
            headLabelText = headLabel.GetComponentInChildren<TextMeshProUGUI>(true);

        if (displayRect == null)
        {
            MobileCamFeed feed = FindFirstObjectByType<MobileCamFeed>(FindObjectsInactive.Include);
            if (feed != null && feed.display != null)
                displayRect = feed.display.rectTransform;
        }
    }

    private RectTransform GetCanvasRect()
    {
        if (headLabel == null)
            return null;

        Canvas canvas = headLabel.GetComponentInParent<Canvas>();
        return canvas != null ? canvas.transform as RectTransform : null;
    }

    private static Transform FindTransformByName(string targetName)
    {
        Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (Transform current in transforms)
        {
            if (current.name == targetName)
                return current;
        }

        return null;
    }

    private void LogPositionSource(string source)
    {
        string labelName = headLabel != null ? headLabel.name : "null";
        string runnerName = runner != null ? runner.name : "null";
        string state = $"source={source}, headLabel={labelName}, runner={runnerName}";

        if (state == lastPositionAuditState)
            return;

        lastPositionAuditState = state;
        Debug.Log($"[SubtitlePositionAudit] {state}", this);
    }
}
