using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class MobileARFaceTrackingRunner : MonoBehaviour
{
    public enum TrackingSource
    {
        Disabled,
        ScreenCenterFallback,
        TouchPosition,
        ARFaceManager
    }

    [Serializable]
    public struct MobileFaceDetection
    {
        public bool hasDetection;
        public Rect normalizedRect;
        public Vector2 normalizedCenter;

        public MobileFaceDetection(Rect normalizedRect)
        {
            this.normalizedRect = normalizedRect;
            normalizedCenter = normalizedRect.center;
            hasDetection = true;
        }
    }

    [Header("Tracking")]
    public TrackingSource trackingSource = TrackingSource.ScreenCenterFallback;
    public Camera trackingCamera;
    public ARFaceManager faceManager;

    [Header("Fallback Target")]
    [Range(0f, 1f)] public float fallbackX = 0.5f;
    [Range(0f, 1f)] public float fallbackTopY = 0.35f;
    [Range(0.05f, 1f)] public float fallbackWidth = 0.28f;
    [Range(0.05f, 1f)] public float fallbackHeight = 0.28f;

    [Header("Touch Target")]
    public float touchTargetWidth = 0.24f;
    public float touchTargetHeight = 0.24f;

    private MobileFaceDetection latestDetection;
    private bool hasLatestDetection;

    public int ImageWidth => Mathf.Max(1, Screen.width);
    public int ImageHeight => Mathf.Max(1, Screen.height);

    private void Awake()
    {
        AutoBind();
    }

    private void Update()
    {
        switch (trackingSource)
        {
            case TrackingSource.Disabled:
                hasLatestDetection = false;
                break;

            case TrackingSource.TouchPosition:
                UpdateFromTouch();
                break;

            case TrackingSource.ARFaceManager:
                UpdateFromARFaceManager();
                break;

            case TrackingSource.ScreenCenterFallback:
                SetFallbackDetection();
                break;
        }
    }

    public bool TryGetLatestResult(ref MobileFaceDetection result)
    {
        if (!hasLatestDetection)
            return false;

        result = latestDetection;
        return true;
    }

    public bool TryGetLatestFace(out MobileFaceDetection result)
    {
        result = latestDetection;
        return hasLatestDetection;
    }

    public void SetNormalizedTarget(Vector2 normalizedPosition)
    {
        float width = Mathf.Clamp01(touchTargetWidth);
        float height = Mathf.Clamp01(touchTargetHeight);
        Rect rect = Rect.MinMaxRect(
            Mathf.Clamp01(normalizedPosition.x - width * 0.5f),
            Mathf.Clamp01(normalizedPosition.y - height * 0.5f),
            Mathf.Clamp01(normalizedPosition.x + width * 0.5f),
            Mathf.Clamp01(normalizedPosition.y + height * 0.5f)
        );

        latestDetection = new MobileFaceDetection(rect);
        hasLatestDetection = true;
    }

    private void UpdateFromTouch()
    {
        if (TryReadPointerPosition(out Vector2 screenPosition))
        {
            SetNormalizedTarget(new Vector2(
                Mathf.Clamp01(screenPosition.x / ImageWidth),
                Mathf.Clamp01(1f - screenPosition.y / ImageHeight)
            ));
            return;
        }

        if (!hasLatestDetection)
            SetFallbackDetection();
    }

    private void UpdateFromARFaceManager()
    {
        AutoBind();

        if (faceManager == null || trackingCamera == null)
        {
            SetFallbackDetection();
            return;
        }

        foreach (ARFace face in faceManager.trackables)
        {
            Vector3 screenPosition = trackingCamera.WorldToScreenPoint(face.transform.position);
            if (screenPosition.z < 0f)
                continue;

            SetNormalizedTarget(new Vector2(
                Mathf.Clamp01(screenPosition.x / ImageWidth),
                Mathf.Clamp01(1f - screenPosition.y / ImageHeight)
            ));
            return;
        }

        SetFallbackDetection();
    }

    private void SetFallbackDetection()
    {
        float width = Mathf.Clamp01(fallbackWidth);
        float height = Mathf.Clamp01(fallbackHeight);
        float centerY = Mathf.Clamp01(fallbackTopY + height * 0.5f);
        Rect rect = Rect.MinMaxRect(
            Mathf.Clamp01(fallbackX - width * 0.5f),
            Mathf.Clamp01(centerY - height * 0.5f),
            Mathf.Clamp01(fallbackX + width * 0.5f),
            Mathf.Clamp01(centerY + height * 0.5f)
        );

        latestDetection = new MobileFaceDetection(rect);
        hasLatestDetection = true;
    }

    private void AutoBind()
    {
        if (trackingCamera == null)
            trackingCamera = Camera.main;

        if (faceManager == null)
            faceManager = FindFirstObjectByType<ARFaceManager>(FindObjectsInactive.Include);
    }

    private static bool TryReadPointerPosition(out Vector2 position)
    {
#if ENABLE_INPUT_SYSTEM
        if (Touchscreen.current != null)
        {
            foreach (var touch in Touchscreen.current.touches)
            {
                if (touch.press.isPressed)
                {
                    position = touch.position.ReadValue();
                    return true;
                }
            }
        }

        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            position = Mouse.current.position.ReadValue();
            return true;
        }
#endif

        try
        {
            if (Input.touchCount > 0)
            {
                position = Input.GetTouch(0).position;
                return true;
            }

            if (Input.GetMouseButton(0))
            {
                position = Input.mousePosition;
                return true;
            }
        }
        catch (InvalidOperationException)
        {
        }

        position = default;
        return false;
    }
}
