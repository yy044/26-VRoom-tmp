using System;
using System.Collections;
using Mediapipe;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.FaceDetector;
using Mediapipe.Unity;
using Mediapipe.Unity.Sample;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using FaceDetectionResult = Mediapipe.Tasks.Components.Containers.DetectionResult;
using MpRect = Mediapipe.Tasks.Components.Containers.Rect;
using TasksRunningMode = Mediapipe.Tasks.Vision.Core.RunningMode;
using UnityRect = UnityEngine.Rect;
using UnityScreen = UnityEngine.Screen;

public enum BackFaceModelMode
{
    [InspectorName("ShortRange (Recommended)")]
    ShortRange,

    [InspectorName("FullRange (Experimental - not validated with current MediaPipe package)")]
    FullRange,

    [InspectorName("AutoByFaceSize (Experimental - depends on validated FullRange support)")]
    AutoByFaceSize
}

public class BackCameraFacePositionProvider : MonoBehaviour, IFacePositionProvider
{
    private const string LogTag = "[BackFace2DAudit]";
    private const string ModelLogTag = "[BackFaceModelAudit]";

    [Header("References")]
    [SerializeField] private ARCameraManager cameraManager;

    [Header("Performance")]
    [SerializeField, Range(1f, 15f)] private float targetFps = 8f;
    [SerializeField, Range(160, 640)] private int maxInputWidth = 320;

    [Header("Detection")]
    [SerializeField] private BaseOptions.Delegate delegateMode =
#if UNITY_ANDROID && !UNITY_EDITOR
        BaseOptions.Delegate.GPU;
#else
        BaseOptions.Delegate.CPU;
#endif
    [SerializeField, Range(1, 4)] private int maxFaces = 1;
    [SerializeField, Range(0f, 1f)] private float shortRangeMinDetectionConfidence = 0.5f;
    [SerializeField, Range(0f, 1f)] private float fullRangeMinDetectionConfidence = 0.2f;
    [SerializeField, Range(0f, 1f)] private float minSuppressionThreshold = 0.3f;

    [Header("Model Selection")]
    [SerializeField] private BackFaceModelMode modelMode = BackFaceModelMode.ShortRange;
    [SerializeField] private string shortRangeModelAssetName = "blaze_face_short_range.bytes";
    [SerializeField] private string fullRangeModelAssetName = "blaze_face_full_range.bytes";

    [Header("Auto Switch Scaffold")]
    [SerializeField] private float nearFaceAreaThreshold = 0.12f;
    [SerializeField] private float farFaceAreaThreshold = 0.06f;
    [SerializeField] private int stableFrameCountRequired = 15;
    [SerializeField] private float minSwitchIntervalSeconds = 2.0f;

    private FaceDetector faceDetector;
    private Texture2D inputTexture;
    private FaceDetectionResult result;
    private bool isReady;
    private bool hasFace;
    private Vector2 normalizedFaceCenter;
    private UnityRect normalizedFaceRect;
    private float nextFrameTime;
    private float nextMissingCpuImageWarningTime;
    private int imageWidth;
    private int imageHeight;
    private string lastAuditState;
    private string lastModelAuditState;
    private string modelError = "";
    private string activeModelAssetName = "";
    private BackFaceModelMode activeModelKind = BackFaceModelMode.ShortRange;
    private float bestFaceConfidence;
    private float normalizedFaceArea;
    private int nearStableFrameCount;
    private int farStableFrameCount;
    private float lastModelSwitchTime = -999f;
    private bool isSwitchingModel;
    private readonly System.Diagnostics.Stopwatch stopwatch = new();

    public bool HasFace => hasFace;
    public Vector2 NormalizedFaceCenter => normalizedFaceCenter;
    public UnityRect NormalizedFaceRect => normalizedFaceRect;
    public string SourceName => "BackFace2D";
    public BackFaceModelMode ModelMode => modelMode;
    public BackFaceModelMode ConfiguredModelMode => modelMode;
    public string ActiveModelName => string.IsNullOrEmpty(activeModelAssetName) ? GetModelAssetName(GetInitialModelKind()) : activeModelAssetName;
    public string ActiveModelLabel
    {
        get
        {
            if (modelMode == BackFaceModelMode.AutoByFaceSize)
                return $"AUTO({GetModelLabel(ActiveModelName)})";

            return GetModelLabel(ActiveModelName);
        }
    }

    // Disabled pending validated FullRange support.
    public void ToggleModelMode()
    {
        BackFaceModelMode oldMode = modelMode;
        BackFaceModelMode newMode = modelMode switch
        {
            BackFaceModelMode.ShortRange => BackFaceModelMode.FullRange,
            BackFaceModelMode.FullRange => BackFaceModelMode.ShortRange,
            _ => BackFaceModelMode.FullRange
        };

        SetModelMode(newMode, $"buttonToggle oldMode={oldMode} newMode={newMode}");
    }

    public void SetShortRange()
    {
        SetModelMode(BackFaceModelMode.ShortRange, "SetShortRange");
    }

    public void SetFullRange()
    {
        SetModelMode(BackFaceModelMode.FullRange, "SetFullRange");
    }

    private IEnumerator Start()
    {
        AutoBind();

        if (cameraManager == null)
        {
            modelError = "ARCameraManager is not assigned";
            LogAudit(false, 0);
            enabled = false;
            yield break;
        }

        AssetLoader.Provide(new StreamingAssetsResourceManager());

        if (delegateMode == BaseOptions.Delegate.GPU && !GpuManager.IsInitialized)
        {
            yield return GpuManager.Initialize();

            if (!GpuManager.IsInitialized)
                delegateMode = BaseOptions.Delegate.CPU;
        }

        activeModelKind = GetInitialModelKind();
        activeModelAssetName = GetModelAssetName(activeModelKind);
        yield return InitializeDetector(activeModelAssetName, activeModelKind, "startup");

        if (!string.IsNullOrEmpty(modelError))
        {
            LogAudit(false, 0);
            LogModelAudit(false, 0);
            enabled = false;
            yield break;
        }
    }

    private IEnumerator InitializeDetector(string modelAssetName, BackFaceModelMode modelKind, string reason)
    {
        modelError = "";
        isReady = false;
        ClearFace();

        faceDetector?.Close();
        faceDetector = null;

        yield return PrepareModel(modelAssetName);

        if (!string.IsNullOrEmpty(modelError))
        {
            Debug.LogError($"{ModelLogTag} failed to load model={modelAssetName} reason={reason} error={modelError}", this);
            yield break;
        }

        var options = new FaceDetectorOptions(
            new BaseOptions(delegateMode, modelAssetPath: modelAssetName),
            runningMode: TasksRunningMode.VIDEO,
            minDetectionConfidence: GetMinDetectionConfidence(modelKind),
            minSuppressionThreshold: minSuppressionThreshold,
            numFaces: maxFaces);

        try
        {
            faceDetector = FaceDetector.CreateFromOptions(options, GpuManager.GpuResources);
        }
        catch (Exception ex)
        {
            modelError = ex.GetType().Name + ": " + ex.Message;
            Debug.LogError($"{ModelLogTag} failed to create detector model={modelAssetName} reason={reason} error={modelError}", this);
            yield break;
        }

        activeModelKind = modelKind;
        activeModelAssetName = modelAssetName;
        result = FaceDetectionResult.Alloc(maxFaces);
        stopwatch.Restart();
        isReady = true;
        nearStableFrameCount = 0;
        farStableFrameCount = 0;
        lastModelSwitchTime = Time.unscaledTime;
        Debug.Log($"{ModelLogTag} loaded model={activeModelAssetName} kind={activeModelKind} reason={reason}", this);
        LogAudit(false, 0);
        LogModelAudit(false, 0);
    }

    private IEnumerator PrepareModel(string modelAssetName)
    {
        IEnumerator prepareRoutine = null;
        try
        {
            prepareRoutine = AssetLoader.PrepareAssetAsync(modelAssetName);
        }
        catch (Exception ex)
        {
            modelError = ex.GetType().Name + ": " + ex.Message;
            yield break;
        }

        while (true)
        {
            object current;
            try
            {
                if (!prepareRoutine.MoveNext())
                    yield break;

                current = prepareRoutine.Current;
            }
            catch (Exception ex)
            {
                modelError = ex.GetType().Name + ": " + ex.Message;
                yield break;
            }

            yield return current;
        }
    }

    private void Update()
    {
        if (!isReady || isSwitchingModel || Time.unscaledTime < nextFrameTime)
            return;

        nextFrameTime = Time.unscaledTime + 1f / Mathf.Max(1f, targetFps);

        bool inferenceRan = false;
        int faceCount = 0;
        bestFaceConfidence = 0f;
        normalizedFaceArea = 0f;

        if (cameraManager.currentFacingDirection != CameraFacingDirection.World)
        {
            ClearFace();
            LogAudit(false, 0);
            LogModelAudit(false, 0);
            return;
        }

        if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
        {
            if (Time.unscaledTime >= nextMissingCpuImageWarningTime)
            {
                Debug.LogWarning($"{LogTag} no AR CPU image yet.", this);
                nextMissingCpuImageWarningTime = Time.unscaledTime + 3f;
            }

            ClearFace();
            LogAudit(false, 0);
            LogModelAudit(false, 0);
            return;
        }

        using (cpuImage)
        {
            if (!TryUpdateInputTexture(cpuImage))
            {
                ClearFace();
                LogAudit(false, 0);
                LogModelAudit(false, 0);
                return;
            }
        }

        using var image = new Image(inputTexture);
        long timestamp = stopwatch.ElapsedTicks / TimeSpan.TicksPerMillisecond;
        inferenceRan = faceDetector.TryDetectForVideo(image, timestamp, GetImageProcessingOptions(), ref result);
        faceCount = inferenceRan && result.detections != null ? result.detections.Count : 0;

        if (inferenceRan && TryGetBestDetection(out MpRect bestBox, out bestFaceConfidence))
            SetFaceFromDetection(bestBox);
        else
            ClearFace();

        UpdateAutoSwitchScaffold();
        LogAudit(inferenceRan, faceCount);
        LogModelAudit(inferenceRan, faceCount);
    }

    private bool TryUpdateInputTexture(XRCpuImage cpuImage)
    {
        int width = cpuImage.width;
        int height = cpuImage.height;

        if (width <= 0 || height <= 0)
            return false;

        if (width > maxInputWidth)
        {
            height = Mathf.Max(1, Mathf.RoundToInt(height * (maxInputWidth / (float)width)));
            width = maxInputWidth;
        }

        var conversionParams = new XRCpuImage.ConversionParams
        {
            inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
            outputDimensions = new Vector2Int(width, height),
            outputFormat = TextureFormat.RGBA32,
            transformation = XRCpuImage.Transformation.MirrorY
        };

        int size = cpuImage.GetConvertedDataSize(conversionParams);
        using var buffer = new NativeArray<byte>(size, Allocator.Temp);

        cpuImage.Convert(conversionParams, new NativeSlice<byte>(buffer));

        if (inputTexture == null || inputTexture.width != width || inputTexture.height != height)
            inputTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);

        inputTexture.LoadRawTextureData(buffer);
        inputTexture.Apply(false);
        imageWidth = width;
        imageHeight = height;
        return true;
    }

    private ImageProcessingOptions GetImageProcessingOptions()
    {
        int rotationDegrees = 0;

        switch (UnityScreen.orientation)
        {
            case ScreenOrientation.Portrait:
                rotationDegrees = 90;
                break;
            case ScreenOrientation.PortraitUpsideDown:
                rotationDegrees = 270;
                break;
            case ScreenOrientation.LandscapeRight:
                rotationDegrees = 180;
                break;
        }

        return new ImageProcessingOptions(rotationDegrees: rotationDegrees);
    }

    private void SetFaceFromDetection(MpRect box)
    {
        float invWidth = imageWidth > 0 ? 1f / imageWidth : 0f;
        float invHeight = imageHeight > 0 ? 1f / imageHeight : 0f;
        float left = Mathf.Clamp01(box.left * invWidth);
        float right = Mathf.Clamp01(box.right * invWidth);
        float bottom = Mathf.Clamp01(1f - box.bottom * invHeight);
        float top = Mathf.Clamp01(1f - box.top * invHeight);

        normalizedFaceRect = UnityRect.MinMaxRect(left, bottom, right, top);
        normalizedFaceCenter = normalizedFaceRect.center;
        normalizedFaceArea = normalizedFaceRect.width * normalizedFaceRect.height;
        hasFace = true;
    }

    private void ClearFace()
    {
        hasFace = false;
        normalizedFaceCenter = Vector2.zero;
        normalizedFaceRect = new UnityRect(0f, 0f, 0f, 0f);
        normalizedFaceArea = 0f;
        bestFaceConfidence = 0f;
    }

    private void LogAudit(bool inferenceRan, int faceCount)
    {
        string status = hasFace ? "DETECTED" : "NOT DETECTED";
        string state =
            $"detector=MediaPipe.BlazeFace " +
            $"imageSource=ARCameraManager.TryAcquireLatestCpuImage " +
            $"imageWidth={imageWidth} " +
            $"imageHeight={imageHeight} " +
            $"inferenceRan={inferenceRan} " +
            $"faceCount={faceCount} " +
            $"activeModel={activeModelAssetName} " +
            $"normalizedCenter={normalizedFaceCenter} " +
            $"normalizedRect={normalizedFaceRect} " +
            $"status={status} " +
            $"modelError={modelError}";

        if (state == lastAuditState)
            return;

        lastAuditState = state;
        Debug.Log($"{LogTag} {state}", this);
    }

    private void LogModelAudit(bool inferenceRan, int faceCount)
    {
        string state =
            $"mode={modelMode} " +
            $"activeModel={activeModelAssetName} " +
            $"initialized={isReady} " +
            $"inferenceRan={inferenceRan} " +
            $"faceCount={faceCount} " +
            $"confidence={bestFaceConfidence:0.000} " +
            $"minDetectionConfidence={GetMinDetectionConfidence(activeModelKind):0.000} " +
            $"rect={normalizedFaceRect} " +
            $"area={normalizedFaceArea:0.0000} " +
            $"autoState=activeKind:{activeModelKind},nearFrames:{nearStableFrameCount},farFrames:{farStableFrameCount},switching:{isSwitchingModel} " +
            $"modelError={modelError}";

        if (state == lastModelAuditState)
            return;

        lastModelAuditState = state;
        Debug.Log($"{ModelLogTag} {state}", this);
    }

    private bool TryGetBestDetection(out MpRect bestBox, out float confidence)
    {
        bestBox = default;
        confidence = 0f;

        if (result.detections == null || result.detections.Count == 0)
            return false;

        bool found = false;
        int bestArea = -1;

        foreach (var detection in result.detections)
        {
            MpRect box = detection.boundingBox;
            int area = Mathf.Max(0, box.right - box.left) * Mathf.Max(0, box.bottom - box.top);
            if (!found || area > bestArea)
            {
                found = true;
                bestArea = area;
                bestBox = box;
                confidence = detection.categories != null && detection.categories.Count > 0
                    ? detection.categories[0].score
                    : 0f;
            }
        }

        return found;
    }

    private void UpdateAutoSwitchScaffold()
    {
        if (modelMode != BackFaceModelMode.AutoByFaceSize || !hasFace || isSwitchingModel)
            return;

        if (Time.unscaledTime - lastModelSwitchTime < minSwitchIntervalSeconds)
            return;

        int requiredFrames = Mathf.Max(1, stableFrameCountRequired);

        if (activeModelKind == BackFaceModelMode.FullRange)
        {
            nearStableFrameCount = normalizedFaceArea > nearFaceAreaThreshold ? nearStableFrameCount + 1 : 0;
            farStableFrameCount = 0;

            if (nearStableFrameCount >= requiredFrames)
                StartCoroutine(SwitchModel(shortRangeModelAssetName, BackFaceModelMode.ShortRange, $"faceArea {normalizedFaceArea:0.0000} > near {nearFaceAreaThreshold:0.0000}"));
        }
        else if (activeModelKind == BackFaceModelMode.ShortRange)
        {
            farStableFrameCount = normalizedFaceArea < farFaceAreaThreshold ? farStableFrameCount + 1 : 0;
            nearStableFrameCount = 0;

            if (farStableFrameCount >= requiredFrames)
                StartCoroutine(SwitchModel(fullRangeModelAssetName, BackFaceModelMode.FullRange, $"faceArea {normalizedFaceArea:0.0000} < far {farFaceAreaThreshold:0.0000}"));
        }
    }

    private IEnumerator SwitchModel(string modelAssetName, BackFaceModelMode modelKind, string reason)
    {
        if (isSwitchingModel)
            yield break;

        isSwitchingModel = true;
        Debug.Log($"{ModelLogTag} switching from={activeModelAssetName} to={modelAssetName} reason={reason}", this);
        yield return InitializeDetector(modelAssetName, modelKind, reason);
        isSwitchingModel = false;
        LogModelAudit(false, 0);
    }

    private void SetModelMode(BackFaceModelMode newMode, string reason)
    {
        BackFaceModelMode oldMode = modelMode;
        if (newMode == BackFaceModelMode.AutoByFaceSize)
            newMode = BackFaceModelMode.FullRange;

        modelMode = newMode;
        BackFaceModelMode nextModelKind = GetInitialModelKind();
        string nextModelName = GetModelAssetName(nextModelKind);
        ClearFace();

        if (!isActiveAndEnabled)
        {
            activeModelKind = nextModelKind;
            activeModelAssetName = nextModelName;
            isReady = false;
            LogModelAudit(false, 0);
            return;
        }

        StartCoroutine(SwitchModel(nextModelName, nextModelKind, reason));
    }

    private BackFaceModelMode GetInitialModelKind()
    {
        return modelMode == BackFaceModelMode.AutoByFaceSize
            ? BackFaceModelMode.FullRange
            : modelMode;
    }

    private string GetModelAssetName(BackFaceModelMode mode)
    {
        return mode == BackFaceModelMode.FullRange
            ? fullRangeModelAssetName
            : shortRangeModelAssetName;
    }

    private float GetMinDetectionConfidence(BackFaceModelMode mode)
    {
        return mode == BackFaceModelMode.FullRange
            ? fullRangeMinDetectionConfidence
            : shortRangeMinDetectionConfidence;
    }

    private string GetModelLabel(string modelAssetName)
    {
        if (string.Equals(modelAssetName, fullRangeModelAssetName, StringComparison.OrdinalIgnoreCase))
            return "LONG";

        if (string.Equals(modelAssetName, shortRangeModelAssetName, StringComparison.OrdinalIgnoreCase))
            return "SHORT";

        return modelAssetName;
    }

    private void AutoBind()
    {
        if (cameraManager == null)
            cameraManager = FindFirstObjectByType<ARCameraManager>(FindObjectsInactive.Include);
    }

    private void OnDestroy()
    {
        faceDetector?.Close();
        faceDetector = null;

        if (inputTexture != null)
        {
            Destroy(inputTexture);
            inputTexture = null;
        }
    }
}
