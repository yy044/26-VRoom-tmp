using System.Collections;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class MobileARModeController : MonoBehaviour
{
    public enum MobileTrackingMode
    {
        WorldHands,
        FaceSubtitle,
        FrontFaceAR,
        BackFace2D
    }

    private const string LogTag = "[MobileARMode]";

    [Header("Mode")]
    public MobileTrackingMode startupMode = MobileTrackingMode.FrontFaceAR;
    public bool applyStartupModeOnStart = true;
    public bool restartSessionOnModeChange = true;
    public float sessionResetDelaySeconds = 0.15f;
    public bool fixedSubtitleFallbackWhenFaceMissing = false;

    [Header("References")]
    public GameObject facePrefab;
    public ARSession arSession;
    public XROrigin xrOrigin;
    public ARCameraManager cameraManager;
    public ARFaceManager faceManager;
    public ARPlaneManager planeManager;
    public ARRaycastManager raycastManager;
    public ARCameraHandLandmarkerRunner handLandmarkerRunner;
    public MobileARFaceTrackingRunner faceTrackingRunner;
    [SerializeField] private BackCameraFacePositionProvider backFacePositionProvider;
    public MobileARHeadTracker headTracker;
    public SpeechToTextManager speechToTextManager;
    public TMP_Text statusText;

    [Header("Debug")]
    public bool showDebugStatus = true;
    public float statusRefreshSeconds = 0.5f;

    private GameObject runtimeFacePrefab;
    private MobileTrackingMode currentMode;
    private Coroutine switchRoutine;
    private float nextStatusTime;
    private string lastBackCameraFaceAuditState;
    private string lastProviderFaceUIStatus;
    private string lastBackFaceProviderSourceAudit;

    public MobileTrackingMode CurrentMode => currentMode;

    private void Awake()
    {
        AutoBind();
        EnsureFaceManager();
        EnsureFacePrefab();
    }

    private void Start()
    {
        if (applyStartupModeOnStart)
            SetMode(startupMode);
    }

    private void Update()
    {
        if (!showDebugStatus || Time.unscaledTime < nextStatusTime)
            return;

        nextStatusTime = Time.unscaledTime + Mathf.Max(0.1f, statusRefreshSeconds);
        RefreshStatus();
    }

    [ContextMenu("Set World Hands Mode")]
    public void SetWorldHandsMode()
    {
        SetMode(MobileTrackingMode.WorldHands);
    }

    [ContextMenu("Set Face Subtitle Mode")]
    public void SetFaceSubtitleMode()
    {
        SetMode(MobileTrackingMode.FrontFaceAR);
    }

    [ContextMenu("Set Front Face AR Mode")]
    public void SetFrontFaceARMode()
    {
        SetMode(MobileTrackingMode.FrontFaceAR);
    }

    [ContextMenu("Set Back Face 2D Mode")]
    public void SetBackFace2DMode()
    {
        SetMode(MobileTrackingMode.BackFace2D);
    }

    [ContextMenu("Toggle Mode")]
    public void ToggleMode()
    {
        SetMode(currentMode == MobileTrackingMode.FaceSubtitle
            || currentMode == MobileTrackingMode.FrontFaceAR
            ? MobileTrackingMode.BackFace2D
            : MobileTrackingMode.FrontFaceAR);
    }

    public void SetMode(MobileTrackingMode mode)
    {
        AutoBind();
        EnsureFaceManager();
        EnsureFacePrefab();

        if (switchRoutine != null)
            StopCoroutine(switchRoutine);

        switchRoutine = StartCoroutine(ApplyModeRoutine(mode));
    }

    private IEnumerator ApplyModeRoutine(MobileTrackingMode mode)
    {
        currentMode = mode;
        bool useFrontFaceMode = mode == MobileTrackingMode.FaceSubtitle || mode == MobileTrackingMode.FrontFaceAR;
        bool useBackFaceMode = mode == MobileTrackingMode.BackFace2D;
        bool useFaceMode = useFrontFaceMode || useBackFaceMode;

        SetEnabled(faceManager, useFrontFaceMode);
        SetEnabled(backFacePositionProvider, useBackFaceMode);
        SetEnabled(handLandmarkerRunner, !useFaceMode);
        SetEnabled(planeManager, !useFaceMode);
        SetEnabled(raycastManager, !useFaceMode);

        if (faceTrackingRunner != null)
        {
            faceTrackingRunner.faceManager = faceManager;
            faceTrackingRunner.trackingCamera = cameraManager != null ? cameraManager.GetComponent<Camera>() : Camera.main;
            faceTrackingRunner.fallbackWhenARFaceMissing = fixedSubtitleFallbackWhenFaceMissing;
            faceTrackingRunner.trackingSource = useFrontFaceMode
                ? MobileARFaceTrackingRunner.TrackingSource.ARFaceManager
                : MobileARFaceTrackingRunner.TrackingSource.Disabled;
        }

        if (cameraManager != null)
            cameraManager.requestedFacingDirection = useFrontFaceMode ? CameraFacingDirection.User : CameraFacingDirection.World;

        if (restartSessionOnModeChange && arSession != null)
        {
            arSession.enabled = false;
            float delay = Mathf.Max(0f, sessionResetDelaySeconds);
            if (delay > 0f)
                yield return new WaitForSecondsRealtime(delay);
            else
                yield return null;

            arSession.Reset();
            arSession.enabled = true;
        }

        RefreshStatus();
        switchRoutine = null;
    }

    private void EnsureFaceManager()
    {
        if (faceManager != null)
            return;

        if (xrOrigin == null)
            xrOrigin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);

        if (xrOrigin == null)
        {
            Debug.LogWarning($"{LogTag} No XROrigin found; cannot create ARFaceManager.", this);
            return;
        }

        faceManager = xrOrigin.GetComponent<ARFaceManager>();
        if (faceManager == null)
            faceManager = xrOrigin.gameObject.AddComponent<ARFaceManager>();
    }

    private void EnsureFacePrefab()
    {
        if (faceManager == null || faceManager.facePrefab != null)
            return;

        if (facePrefab != null)
        {
            faceManager.facePrefab = facePrefab;
            return;
        }

        runtimeFacePrefab = new GameObject("Runtime Invisible AR Face");
        runtimeFacePrefab.SetActive(false);
        runtimeFacePrefab.hideFlags = HideFlags.HideAndDontSave;
        runtimeFacePrefab.AddComponent<ARFace>();
        faceManager.facePrefab = runtimeFacePrefab;
    }

    private void AutoBind()
    {
        if (arSession == null)
            arSession = FindFirstObjectByType<ARSession>(FindObjectsInactive.Include);

        if (xrOrigin == null)
            xrOrigin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);

        if (cameraManager == null)
            cameraManager = FindFirstObjectByType<ARCameraManager>(FindObjectsInactive.Include);

        if (faceManager == null)
            faceManager = FindFirstObjectByType<ARFaceManager>(FindObjectsInactive.Include);

        if (planeManager == null)
            planeManager = FindFirstObjectByType<ARPlaneManager>(FindObjectsInactive.Include);

        if (raycastManager == null)
            raycastManager = FindFirstObjectByType<ARRaycastManager>(FindObjectsInactive.Include);

        if (handLandmarkerRunner == null)
            handLandmarkerRunner = FindFirstObjectByType<ARCameraHandLandmarkerRunner>(FindObjectsInactive.Include);

        if (faceTrackingRunner == null)
            faceTrackingRunner = FindFirstObjectByType<MobileARFaceTrackingRunner>(FindObjectsInactive.Include);

        BindBackFacePositionProvider();

        if (statusText == null)
            statusText = FindStatusText();
    }

    private void BindBackFacePositionProvider()
    {
        string providerSource = "Serialized";

        if (backFacePositionProvider == null && cameraManager != null)
        {
            backFacePositionProvider = cameraManager.GetComponent<BackCameraFacePositionProvider>();
            providerSource = backFacePositionProvider != null ? "ExistingOnCamera" : providerSource;
        }

        if (backFacePositionProvider == null)
        {
            backFacePositionProvider = FindFirstObjectByType<BackCameraFacePositionProvider>(FindObjectsInactive.Include);
            providerSource = backFacePositionProvider != null ? "ExistingInScene" : providerSource;
        }

        if (backFacePositionProvider == null && cameraManager != null)
        {
            backFacePositionProvider = cameraManager.gameObject.AddComponent<BackCameraFacePositionProvider>();
            providerSource = "RuntimeCreated";
        }

        if (backFacePositionProvider == null)
            providerSource = "Missing";

        string state = $"providerSource={providerSource} modelMode={(backFacePositionProvider != null ? backFacePositionProvider.ModelMode.ToString() : "None")}";
        if (state == lastBackFaceProviderSourceAudit)
            return;

        lastBackFaceProviderSourceAudit = state;
        Debug.Log($"[BackFaceModelAudit] {state}", this);
    }

    private void RefreshStatus()
    {
        IFacePositionProvider provider = GetActiveFaceProvider();
        bool hasFace = provider != null && provider.HasFace;
        string detectionState = hasFace ? "DETECTED" : "NOT DETECTED";
        string message = $"{GetStatusPrefix(provider)}: {detectionState}";

        if (statusText != null)
        {
            statusText.text = message;
            statusText.color = hasFace ? Color.green : Color.red;
        }

        LogBackCameraFaceAudit(message);
        LogProviderFaceUIStatus(provider, message);
    }

    private static TMP_Text FindStatusText()
    {
        TMP_Text[] texts = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (TMP_Text text in texts)
        {
            if (text.name.Contains("Debug") || text.name.Contains("Status"))
                return text;
        }

        return null;
    }

    private static void SetEnabled(Behaviour behaviour, bool enabled)
    {
        if (behaviour != null)
            behaviour.enabled = enabled;
    }

    private void LogBackCameraFaceAudit(string statusTextValue)
    {
        IFacePositionProvider provider = GetActiveFaceProvider();
        string requestedFacing = cameraManager != null ? cameraManager.requestedFacingDirection.ToString() : "No ARCameraManager";
        string currentFacing = cameraManager != null ? cameraManager.currentFacingDirection.ToString() : "No ARCameraManager";
        bool faceSubsystemPresent = faceManager != null && faceManager.subsystem != null;
        bool faceSubsystemRunning = faceSubsystemPresent && faceManager.subsystem.running;
        int faceCount = CountFaceTrackables();
        bool hasRealFaceTracking = faceTrackingRunner != null && faceTrackingRunner.HasRealFaceTracking;
        bool hasProviderFace = provider != null && provider.HasFace;
        string state =
            $"requestedFacingDirection={requestedFacing} " +
            $"currentFacingDirection={currentFacing} " +
            $"activeProvider={(provider != null ? provider.SourceName : "None")} " +
            $"ARSession.state={ARSession.state} " +
            $"ARSession.notTrackingReason={ARSession.notTrackingReason} " +
            $"ARFaceManager.enabled={(faceManager != null && faceManager.enabled)} " +
            $"ARFaceManager.subsystemPresent={faceSubsystemPresent} " +
            $"ARFaceManager.subsystemRunning={faceSubsystemRunning} " +
            $"ARFaceManager.trackables.count={faceCount} " +
            $"HasRealFaceTracking={hasRealFaceTracking} " +
            $"provider.HasFace={hasProviderFace} " +
            $"provider.NormalizedFaceCenter={(provider != null ? provider.NormalizedFaceCenter : Vector2.zero)} " +
            $"provider.NormalizedFaceRect={(provider != null ? provider.NormalizedFaceRect : new Rect(0f, 0f, 0f, 0f))} " +
            $"statusText={statusTextValue}";

        if (state == lastBackCameraFaceAuditState)
            return;

        lastBackCameraFaceAuditState = state;
        Debug.Log($"[BackCameraFaceAudit] {state}", this);

        if (currentFacing == CameraFacingDirection.World.ToString() && faceManager != null && faceManager.enabled)
            Debug.LogWarning("[BackCameraFaceAudit] ARCore ARFaceManager/Augmented Faces is expected to work with the User/front camera, not World/back camera.", this);
    }

    private IFacePositionProvider GetActiveFaceProvider()
    {
        if (currentMode == MobileTrackingMode.BackFace2D)
            return backFacePositionProvider;

        if (currentMode == MobileTrackingMode.FaceSubtitle || currentMode == MobileTrackingMode.FrontFaceAR)
            return faceTrackingRunner;

        return null;
    }

    private string GetStatusPrefix(IFacePositionProvider provider)
    {
        if (currentMode == MobileTrackingMode.BackFace2D)
            return "BACK 2D";

        if (currentMode == MobileTrackingMode.FaceSubtitle || currentMode == MobileTrackingMode.FrontFaceAR)
            return "FRONT AR";

        return provider != null ? provider.SourceName : "NO FACE PROVIDER";
    }

    private void LogProviderFaceUIStatus(IFacePositionProvider provider, string statusTextValue)
    {
        string state =
            $"provider={(provider != null ? provider.SourceName : "None")} " +
            $"hasFace={(provider != null && provider.HasFace)} " +
            $"normalizedCenter={(provider != null ? provider.NormalizedFaceCenter : Vector2.zero)} " +
            $"normalizedRect={(provider != null ? provider.NormalizedFaceRect : new Rect(0f, 0f, 0f, 0f))} " +
            $"statusText={statusTextValue}";

        if (state == lastProviderFaceUIStatus)
            return;

        lastProviderFaceUIStatus = state;
        Debug.Log($"[FaceUIStatus] {state}", this);
    }

    private int CountFaceTrackables()
    {
        if (faceManager == null || faceManager.subsystem == null || !faceManager.subsystem.running)
            return 0;

        int count = 0;
        foreach (ARFace face in faceManager.trackables)
            count++;

        return count;
    }

    private void OnDestroy()
    {
        if (runtimeFacePrefab != null)
            Destroy(runtimeFacePrefab);
    }
}
