using UnityEngine;

[ExecuteAlways]
public class SafeAreaFitter : MonoBehaviour
{
    [SerializeField] private RectTransform target;

    private Rect lastSafeArea;
    private Vector2Int lastScreenSize;
    private ScreenOrientation lastOrientation;
    private bool hasApplied;

    private void Awake()
    {
        AutoBind();
        ApplyIfNeeded(true);
    }

    private void OnEnable()
    {
        AutoBind();
        ApplyIfNeeded(true);
    }

    private void Update()
    {
        ApplyIfNeeded(false);
    }

    private void OnValidate()
    {
        AutoBind();
        ApplyIfNeeded(true);
    }

    private void AutoBind()
    {
        if (target == null)
            target = GetComponent<RectTransform>();
    }

    private void ApplyIfNeeded(bool force)
    {
        if (target == null)
            return;

        Rect safeArea = Screen.safeArea;
        Vector2Int screenSize = new Vector2Int(Screen.width, Screen.height);
        ScreenOrientation orientation = Screen.orientation;

        if (hasApplied &&
            safeArea == lastSafeArea &&
            screenSize == lastScreenSize &&
            orientation == lastOrientation)
        {
            return;
        }

        lastSafeArea = safeArea;
        lastScreenSize = screenSize;
        lastOrientation = orientation;
        hasApplied = true;

        float width = Mathf.Max(1f, screenSize.x);
        float height = Mathf.Max(1f, screenSize.y);
        Vector2 anchorMin = new Vector2(safeArea.xMin / width, safeArea.yMin / height);
        Vector2 anchorMax = new Vector2(safeArea.xMax / width, safeArea.yMax / height);

        target.anchorMin = anchorMin;
        target.anchorMax = anchorMax;
        target.offsetMin = Vector2.zero;
        target.offsetMax = Vector2.zero;

        Debug.Log($"[SafeArea] Applied safeArea={safeArea} orientation={orientation}", this);
    }
}
