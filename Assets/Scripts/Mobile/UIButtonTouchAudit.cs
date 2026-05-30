using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIButtonTouchAudit : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    [SerializeField] private bool debugLogs = true;
    [SerializeField] private bool logGlobalTouches = true;

    private readonly List<RaycastResult> raycastResults = new List<RaycastResult>();

    public void OnPointerDown(PointerEventData eventData)
    {
        LogPointerEvent("PointerDown", eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        LogPointerEvent("PointerUp", eventData);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        LogPointerEvent("PointerClick", eventData);
    }

    private void Awake()
    {
        LogSceneState();
    }

    private void Update()
    {
        if (!debugLogs || !logGlobalTouches || EventSystem.current == null)
            return;

        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
                LogRaycastStack("GlobalTouchBegan", touch.position);
            else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                LogRaycastStack("GlobalTouchEnded", touch.position);
        }
        else
        {
            if (Input.GetMouseButtonDown(0))
                LogRaycastStack("GlobalPointerDown", Input.mousePosition);
            else if (Input.GetMouseButtonUp(0))
                LogRaycastStack("GlobalPointerUp", Input.mousePosition);
        }
    }

    private void LogSceneState()
    {
        if (!debugLogs)
            return;

        EventSystem[] eventSystems = FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        GraphicRaycaster[] raycasters = FindObjectsByType<GraphicRaycaster>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Button button = GetComponent<Button>();

        Debug.Log(
            $"[UIButtonTouchAudit] scene eventSystems={eventSystems.Length} graphicRaycasters={raycasters.Length} " +
            $"button={GetPath(transform)} interactable={(button != null && button.interactable)} activeInHierarchy={gameObject.activeInHierarchy}",
            this);
    }

    private void LogPointerEvent(string eventName, PointerEventData eventData)
    {
        if (!debugLogs)
            return;

        LogRaycastStack(eventName, eventData.position);
    }

    private void LogRaycastStack(string eventName, Vector2 position)
    {
        raycastResults.Clear();
        PointerEventData eventData = new PointerEventData(EventSystem.current)
        {
            position = position
        };
        EventSystem.current?.RaycastAll(eventData, raycastResults);

        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < raycastResults.Count; i++)
        {
            RaycastResult result = raycastResults[i];
            if (i > 0)
                builder.Append(" | ");

            GameObject resultObject = result.gameObject;
            builder.Append('#').Append(i)
                .Append(':').Append(resultObject != null ? GetPath(resultObject.transform) : "null")
                .Append(" module=").Append(result.module != null ? result.module.name : "null");

            Graphic graphic = resultObject != null ? resultObject.GetComponent<Graphic>() : null;
            if (graphic != null)
                builder.Append(" raycastTarget=").Append(graphic.raycastTarget);

            CanvasGroup canvasGroup = resultObject != null ? resultObject.GetComponent<CanvasGroup>() : null;
            if (canvasGroup != null)
                builder.Append(" blocksRaycasts=").Append(canvasGroup.blocksRaycasts);
        }

        string topObject = raycastResults.Count > 0 && raycastResults[0].gameObject != null
            ? GetPath(raycastResults[0].gameObject.transform)
            : "none";
        Debug.Log(
            $"[UIButtonTouchAudit] {eventName} button={GetPath(transform)} pointer={position} " +
            $"top={topObject} raycastStack={builder}",
            this);
    }

    private static string GetPath(Transform transform)
    {
        if (transform == null)
            return "null";

        StringBuilder builder = new StringBuilder(transform.name);
        Transform current = transform.parent;
        while (current != null)
        {
            builder.Insert(0, current.name + "/");
            current = current.parent;
        }

        return builder.ToString();
    }
}
