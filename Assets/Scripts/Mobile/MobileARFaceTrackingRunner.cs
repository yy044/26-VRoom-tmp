using System.Collections;
using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class MobileARFaceTrackingRunner : MonoBehaviour, IFacePositionProvider
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
        public bool isFallback;

        public MobileFaceDetection(Rect normalizedRect, bool isFallback = false)
        {
            this.normalizedRect = normalizedRect;
            normalizedCenter = normalizedRect.center;
            hasDetection = true;
            this.isFallback = isFallback;
        }

        public MobileFaceDetection(Vector2 normalizedCenter, Rect normalizedRect, bool isFallback = false)
        {
            this.normalizedRect = normalizedRect;
            this.normalizedCenter = normalizedCenter;
            hasDetection = true;
            this.isFallback = isFallback;
        }
    }

    [Header("Tracking")]
    public TrackingSource trackingSource = TrackingSource.ScreenCenterFallback;
    public Camera trackingCamera;
    public ARFaceManager faceManager;
    public bool fallbackWhenARFaceMissing = true;

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
    private float nextWarningTime;
    private bool availabilityCheckStarted;
    private bool availabilityCheckCompleted;
    private string availabilityResult = "not started";
    private bool installAttempted;
    private string installResult = "not attempted";
    private ARSessionState previousSessionState = ARSessionState.None;
    private string lastFaceAuditState;
    private string lastSessionAuditState;
    private string lastFaceUIStatus;

    public int ImageWidth => Mathf.Max(1, Screen.width);
    public int ImageHeight => Mathf.Max(1, Screen.height);
    public bool HasRealFaceTracking { get; private set; }
    public int FaceTrackableCount { get; private set; }
    public bool HasFace => HasRealFaceTracking && hasLatestDetection;
    public Vector2 NormalizedFaceCenter => hasLatestDetection ? latestDetection.normalizedCenter : Vector2.zero;
    public Rect NormalizedFaceRect => hasLatestDetection ? latestDetection.normalizedRect : new Rect(0f, 0f, 0f, 0f);
    public string SourceName => "FrontFaceAR";

    private void Awake()
    {
        AutoBind();
    }

    private void OnEnable()
    {
        previousSessionState = ARSession.state;
        ARSession.stateChanged += OnARSessionStateChanged;

        if (!availabilityCheckStarted)
            StartCoroutine(RunAvailabilityAudit());
    }

    private void OnDisable()
    {
        ARSession.stateChanged -= OnARSessionStateChanged;
        hasLatestDetection = false;
        HasRealFaceTracking = false;
        FaceTrackableCount = 0;
    }

    private void Update()
    {
        switch (trackingSource)
        {
            case TrackingSource.Disabled:
                hasLatestDetection = false;
                HasRealFaceTracking = false;
                FaceTrackableCount = 0;
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

        LogFaceAudit();
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
        HasRealFaceTracking = false;
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

        if (faceManager == null || faceManager.subsystem == null || !faceManager.subsystem.running)
        {
            FaceTrackableCount = 0;
            HasRealFaceTracking = false;
            WarnThrottled("AR face tracking requested, but ARFaceManager subsystem is not running.");
            SetMissingARFaceDetection();
            return;
        }

        FaceTrackableCount = 0;

        foreach (ARFace face in faceManager.trackables)
        {
            FaceTrackableCount++;
            HasRealFaceTracking = true;

            if (trackingCamera == null)
            {
                latestDetection = new MobileFaceDetection();
                hasLatestDetection = true;
                return;
            }

            Vector3 screenPosition = trackingCamera.WorldToScreenPoint(face.transform.position);
            if (screenPosition.z < 0f)
            {
                latestDetection = new MobileFaceDetection();
                hasLatestDetection = true;
                return;
            }

            Vector2 normalizedCenter = new Vector2(
                Mathf.Clamp01(screenPosition.x / ImageWidth),
                Mathf.Clamp01(1f - screenPosition.y / ImageHeight)
            );

            Rect normalizedBounds = TryGetProjectedFaceBounds(face, out Rect projectedBounds)
                ? projectedBounds
                : Rect.zero;

            latestDetection = new MobileFaceDetection(normalizedCenter, normalizedBounds);
            hasLatestDetection = true;
            HasRealFaceTracking = true;
            return;
        }

        HasRealFaceTracking = false;
        SetMissingARFaceDetection();
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

        latestDetection = new MobileFaceDetection(rect, true);
        hasLatestDetection = true;
    }

    private void SetMissingARFaceDetection()
    {
        if (fallbackWhenARFaceMissing)
        {
            SetFallbackDetection();
            return;
        }

        hasLatestDetection = false;
    }

    private bool TryGetProjectedFaceBounds(ARFace face, out Rect normalizedBounds)
    {
        normalizedBounds = Rect.zero;

        if (face == null || trackingCamera == null || !face.vertices.IsCreated || face.vertices.Length == 0)
            return false;

        float minX = 1f;
        float minY = 1f;
        float maxX = 0f;
        float maxY = 0f;
        bool foundVisibleVertex = false;

        for (int i = 0; i < face.vertices.Length; i++)
        {
            Vector3 worldPosition = face.transform.TransformPoint(face.vertices[i]);
            Vector3 screenPosition = trackingCamera.WorldToScreenPoint(worldPosition);
            if (screenPosition.z < 0f)
                continue;

            float x = Mathf.Clamp01(screenPosition.x / ImageWidth);
            float y = Mathf.Clamp01(1f - screenPosition.y / ImageHeight);
            minX = Mathf.Min(minX, x);
            minY = Mathf.Min(minY, y);
            maxX = Mathf.Max(maxX, x);
            maxY = Mathf.Max(maxY, y);
            foundVisibleVertex = true;
        }

        if (!foundVisibleVertex || maxX - minX <= 0.001f || maxY - minY <= 0.001f)
            return false;

        normalizedBounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return true;
    }

    private void WarnThrottled(string message)
    {
        if (Time.unscaledTime < nextWarningTime)
            return;

        Debug.LogWarning($"[MobileARFaceTrackingRunner] {message}", this);
        nextWarningTime = Time.unscaledTime + 3f;
    }

    private void AutoBind()
    {
        if (trackingCamera == null)
            trackingCamera = Camera.main;

        if (faceManager == null)
            faceManager = FindFirstObjectByType<ARFaceManager>(FindObjectsInactive.Include);
    }

    private void LogFaceAudit()
    {
        ARCameraManager cameraManager = trackingCamera != null
            ? trackingCamera.GetComponent<ARCameraManager>()
            : FindFirstObjectByType<ARCameraManager>(FindObjectsInactive.Include);

        bool hasFaceManager = faceManager != null;
        bool subsystemNull = faceManager == null || faceManager.subsystem == null;
        bool subsystemRunning = faceManager != null && faceManager.subsystem != null && faceManager.subsystem.running;
        int faceCount = CountTrackables();
        string facing = cameraManager != null ? cameraManager.currentFacingDirection.ToString() : "No ARCameraManager";
        string state =
            $"ARSession.state={ARSession.state} " +
            $"ARSession.notTrackingReason={ARSession.notTrackingReason} " +
            $"CheckAvailability.result={availabilityResult} " +
            $"cameraPermission={GetCameraPermissionStatus()} " +
            $"ARCameraManager.currentFacingDirection={facing} " +
            $"ARFaceManager.subsystemRunning={subsystemRunning} " +
            $"ARFaceManager.trackables.count={faceCount} " +
            $"HasRealFaceTracking={HasRealFaceTracking}";

        if (state == lastFaceAuditState)
            return;

        lastFaceAuditState = state;

        Debug.Log(
            $"[ARFaceAudit] {state} " +
            $"ARFaceManager.exists={hasFaceManager} " +
            $"ARFaceManager.enabled={(faceManager != null && faceManager.enabled)} " +
            $"ARFaceManager.subsystemNull={subsystemNull} " +
            $"Application.platform={Application.platform} " +
            $"deviceModel={SystemInfo.deviceModel}",
            this);

        LogSessionAudit();
        LogFaceUIStatus(faceCount);
    }

    private IEnumerator RunAvailabilityAudit()
    {
        availabilityCheckStarted = true;
        availabilityResult = $"started from state {ARSession.state}";

        yield return ARSession.CheckAvailability();
        availabilityCheckCompleted = true;
        availabilityResult = ARSession.state.ToString();
        LogFaceAudit();

        if (ARSession.state == ARSessionState.NeedsInstall)
        {
            installAttempted = true;
            installResult = "started";

            yield return ARSession.Install();
            installResult = ARSession.state.ToString();
            LogFaceAudit();
        }
    }

    private void OnARSessionStateChanged(ARSessionStateChangedEventArgs args)
    {
        Debug.Log(
            $"[ARSessionAudit] stateChanged: {previousSessionState} -> {args.state}, " +
            $"notTrackingReason={ARSession.notTrackingReason}",
            this);

        previousSessionState = args.state;
    }

    private void LogSessionAudit()
    {
        ARCameraManager cameraManager = trackingCamera != null
            ? trackingCamera.GetComponent<ARCameraManager>()
            : FindFirstObjectByType<ARCameraManager>(FindObjectsInactive.Include);

        bool subsystemPresent = faceManager != null && faceManager.subsystem != null;
        bool subsystemRunning = subsystemPresent && faceManager.subsystem.running;
        int faceCount = CountTrackables();
        string facing = cameraManager != null ? cameraManager.currentFacingDirection.ToString() : "No ARCameraManager";
        string state =
            $"ARSession.state={ARSession.state} " +
            $"ARSession.notTrackingReason={ARSession.notTrackingReason} " +
            $"CheckAvailability.result={availabilityResult} " +
            $"cameraPermission={GetCameraPermissionStatus()} " +
            $"ARCameraManager.currentFacingDirection={facing} " +
            $"ARFaceManager.subsystemRunning={subsystemRunning} " +
            $"ARFaceManager.trackables.count={faceCount} " +
            $"HasRealFaceTracking={HasRealFaceTracking}";

        if (state == lastSessionAuditState)
            return;

        lastSessionAuditState = state;

        Debug.Log(
            $"[ARSessionAudit] {state} " +
            $"CheckAvailability.started={availabilityCheckStarted} " +
            $"CheckAvailability.completed={availabilityCheckCompleted} " +
            $"Install.attempted={installAttempted} " +
            $"Install.result={installResult} " +
            $"Application.platform={Application.platform} " +
            $"deviceModel={SystemInfo.deviceModel} " +
            $"ARFaceManager.subsystemPresent={subsystemPresent}",
            this);
    }

    private void LogFaceUIStatus(int faceCount)
    {
        string status = HasRealFaceTracking ? "DETECTED" : "NOT DETECTED";
        string state = $"hasRealFaceTracking={HasRealFaceTracking} trackables.count={faceCount} statusText={status}";

        if (state == lastFaceUIStatus)
            return;

        lastFaceUIStatus = state;
        Debug.Log($"[FaceUIStatus] {state}", this);
    }

    private static string GetCameraPermissionStatus()
    {
#if UNITY_ANDROID
        return Permission.HasUserAuthorizedPermission(Permission.Camera) ? "granted" : "not granted";
#else
        return "not applicable";
#endif
    }

    private int CountTrackables()
    {
        if (faceManager == null || faceManager.subsystem == null || !faceManager.subsystem.running)
            return 0;

        int count = 0;
        foreach (ARFace face in faceManager.trackables)
            count++;

        return count;
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
