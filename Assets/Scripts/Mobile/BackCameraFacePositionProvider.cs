using System;
using System.Collections;
using System.Collections.Generic;
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
#if UNITY_ANDROID
using UnityEngine.Android;
#endif
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

public class BackCameraFacePositionProvider : MonoBehaviour, IFacePositionProvider, IMultiFacePositionProvider
{
    private const string LogTag = "[BackFace2DAudit]";
    private const string ModelLogTag = "[BackFaceModelAudit]";

    [Header("References")]
    [SerializeField] private ARCameraManager cameraManager;
    [SerializeField] private MobileCamFeed cameraPreviewFeed;

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
    [SerializeField, Range(1, 4)] private int maxFaces = 2;
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

    [Header("Debug")]
    [SerializeField] private bool debugBackCameraFacePosition = false;

    private FaceDetector faceDetector;
    private Texture2D inputTexture;
    private FaceDetectionResult result;
    private bool isReady;
    private bool hasFace;
    private Vector2 normalizedFaceCenter;
    private UnityRect normalizedFaceRect;
    private float nextFrameTime;
    private float nextMissingCpuImageWarningTime;
    private float nextRuntimeAuditLogTime;
    private int imageWidth;
    private int imageHeight;
    private string lastAuditState;
    private string lastModelAuditState;
    private string modelError = "";
    private string activeModelAssetName = "";
    private BackFaceModelMode activeModelKind = BackFaceModelMode.ShortRange;
    private float bestFaceConfidence;
    private float normalizedFaceArea;
    private MpRect lastRawMediaPipeBox;
    private bool hasLastRawMediaPipeBox;
    private int nearStableFrameCount;
    private int farStableFrameCount;
    private float lastModelSwitchTime = -999f;
    private bool isSwitchingModel;
    private bool loggedCpuImageSuccess;
    private bool loggedMediaPipeImageReceived;
    private bool loggedDetectionCountPositive;
    private readonly List<FaceTrackCandidate> faceTrackCandidates = new();
    private readonly System.Diagnostics.Stopwatch stopwatch = new();

    public bool HasFace => hasFace;
    public Vector2 NormalizedFaceCenter => normalizedFaceCenter;
    public UnityRect NormalizedFaceRect => normalizedFaceRect;
    public string SourceName => "BackFace2D";
    public IReadOnlyList<FaceTrackCandidate> FaceTrackCandidates => faceTrackCandidates;
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

    private void OnEnable()
    {
        Debug.Log($"{LogTag} ProviderStarted enabled={enabled} activeInHierarchy={gameObject.activeInHierarchy}", this);
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

        yield return EnsureCameraPermission();

        if (cameraManager == null)
        {
            modelError = "ARCameraManager is not assigned";
            LogAudit(false, 0);
            enabled = false;
            yield break;
        }

        if (!HasCameraPermission())
        {
            modelError = "Camera permission is not granted";
            Debug.LogWarning($"{LogTag} cameraPermission=not_granted provider cannot acquire AR CPU images.", this);
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

    private IEnumerator EnsureCameraPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        bool alreadyGranted = Permission.HasUserAuthorizedPermission(Permission.Camera);
        Debug.Log($"{LogTag} cameraPermission beforeRequest={(alreadyGranted ? "granted" : "not_granted")}", this);

        if (!alreadyGranted)
        {
            Permission.RequestUserPermission(Permission.Camera);

            float deadline = Time.realtimeSinceStartup + 8f;
            while (!Permission.HasUserAuthorizedPermission(Permission.Camera) &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        Debug.Log($"{LogTag} cameraPermission afterRequest={GetCameraPermissionStatus()}", this);
#else
        Debug.Log($"{LogTag} cameraPermission editor_or_non_android", this);
#endif
        yield break;
    }

    private static bool HasCameraPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return Permission.HasUserAuthorizedPermission(Permission.Camera);
#else
        return true;
#endif
    }

    private static string GetCameraPermissionStatus()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return Permission.HasUserAuthorizedPermission(Permission.Camera) ? "granted" : "not_granted";
#else
        return "editor_or_non_android";
#endif
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
        if (!isReady || isSwitchingModel)
        {
            LogRuntimeAudit("Waiting", false, 0);
            return;
        }

        if (Time.unscaledTime < nextFrameTime)
            return;

        nextFrameTime = Time.unscaledTime + 1f / Mathf.Max(1f, targetFps);

        bool inferenceRan = false;
        int faceCount = 0;
        bestFaceConfidence = 0f;
        normalizedFaceArea = 0f;

        if (cameraManager.currentFacingDirection != CameraFacingDirection.World)
        {
            ClearFace();
            LogRuntimeAudit("WrongFacingDirection", false, 0);
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
            LogRuntimeAudit("NoCpuImage", false, 0);
            LogAudit(false, 0);
            LogModelAudit(false, 0);
            return;
        }

        LogOnce(
            ref loggedCpuImageSuccess,
            $"TryAcquireLatestCpuImageSucceeded cpuWidth={cpuImage.width} cpuHeight={cpuImage.height}");

        using (cpuImage)
        {
            if (!TryUpdateInputTexture(cpuImage))
            {
                ClearFace();
                LogRuntimeAudit("ImageConversionFailed", false, 0);
                LogAudit(false, 0);
                LogModelAudit(false, 0);
                return;
            }
        }

        using var image = new Image(inputTexture);
        LogOnce(
            ref loggedMediaPipeImageReceived,
            $"MediaPipeReceivesImage textureWidth={inputTexture.width} textureHeight={inputTexture.height} rotationDegrees={GetMediaPipeRotationDegrees()}");

        long timestamp = stopwatch.ElapsedTicks / TimeSpan.TicksPerMillisecond;
        inferenceRan = faceDetector.TryDetectForVideo(image, timestamp, GetImageProcessingOptions(), ref result);
        faceCount = inferenceRan && result.detections != null ? result.detections.Count : 0;
        if (faceCount > 0)
            LogOnce(ref loggedDetectionCountPositive, $"DetectionCountPositive count={faceCount}");

        UpdateFaceTrackCandidates(inferenceRan);

        if (inferenceRan && TryGetBestDetection(out MpRect bestBox, out bestFaceConfidence))
            SetFaceFromDetection(bestBox);
        else
            ClearFace();

        UpdateAutoSwitchScaffold();
        LogRuntimeAudit("Processed", inferenceRan, faceCount);
        LogAudit(inferenceRan, faceCount);
        LogModelAudit(inferenceRan, faceCount);
    }

    private void OnDisable()
    {
        ClearFace();
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
        return new ImageProcessingOptions(rotationDegrees: GetMediaPipeRotationDegrees());
    }

    private int GetMediaPipeRotationDegrees()
    {
        // Keep MediaPipe output in raw texture space. Back-camera display rotation is
        // applied exactly once below using WebCamTexture.videoRotationAngle.
        return 0;
    }

    private void SetFaceFromDetection(MpRect box)
    {
        lastRawMediaPipeBox = box;
        hasLastRawMediaPipeBox = true;

        int videoRotationAngle = GetVideoRotationAngle();
        bool videoVerticallyMirrored = GetVideoVerticallyMirrored();
        UnityRect rawMediaPipeRect = GetDetectorSpaceRect(box);
        UnityRect displaySpaceTopLeftRect = RotateTextureRectToDisplaySpace(rawMediaPipeRect, videoRotationAngle);
        // Back-camera output is raw MediaPipe texture coordinates rotated once into
        // the displayed preview orientation. It remains top-left-origin and unmirrored;
        // ActiveFacePositionProviderRouter.Back2DProfile.faceCoordinateTransform performs
        // the display-space to Unity screen/UI-space conversion used by consumers.
        normalizedFaceRect = displaySpaceTopLeftRect;
        normalizedFaceCenter = normalizedFaceRect.center;
        normalizedFaceArea = normalizedFaceRect.width * normalizedFaceRect.height;
        hasFace = true;

        LogBackCameraFacePosition(box, rawMediaPipeRect, displaySpaceTopLeftRect, videoRotationAngle, videoVerticallyMirrored);
    }

    private void UpdateFaceTrackCandidates(bool inferenceRan)
    {
        faceTrackCandidates.Clear();

        if (!inferenceRan || result.detections == null || result.detections.Count == 0)
            return;

        int videoRotationAngle = GetVideoRotationAngle();
        for (int i = 0; i < result.detections.Count; i++)
        {
            var detection = result.detections[i];
            UnityRect rawMediaPipeRect = GetDetectorSpaceRect(detection.boundingBox);
            UnityRect displaySpaceTopLeftRect = RotateTextureRectToDisplaySpace(rawMediaPipeRect, videoRotationAngle);
            float confidence = detection.categories != null && detection.categories.Count > 0
                ? detection.categories[0].score
                : 0f;

            faceTrackCandidates.Add(new FaceTrackCandidate
            {
                DetectionIndex = i,
                NormalizedCenter = displaySpaceTopLeftRect.center,
                NormalizedBounds = displaySpaceTopLeftRect,
                Confidence = confidence,
                HasBounds = displaySpaceTopLeftRect.width > 0.001f && displaySpaceTopLeftRect.height > 0.001f
            });
        }
    }

    private UnityRect GetDetectorSpaceRect(MpRect box)
    {
        float invDetectorWidth = imageWidth > 0f ? 1f / imageWidth : 0f;
        float invDetectorHeight = imageHeight > 0f ? 1f / imageHeight : 0f;

        return UnityRect.MinMaxRect(
            Mathf.Clamp01(box.left * invDetectorWidth),
            Mathf.Clamp01(box.top * invDetectorHeight),
            Mathf.Clamp01(box.right * invDetectorWidth),
            Mathf.Clamp01(box.bottom * invDetectorHeight));
    }

    private static UnityRect RotateTextureRectToDisplaySpace(UnityRect textureTopLeftRect, int videoRotationAngle)
    {
        Vector2 a = RotateTexturePointToDisplaySpace(new Vector2(textureTopLeftRect.xMin, textureTopLeftRect.yMin), videoRotationAngle);
        Vector2 b = RotateTexturePointToDisplaySpace(new Vector2(textureTopLeftRect.xMin, textureTopLeftRect.yMax), videoRotationAngle);
        Vector2 c = RotateTexturePointToDisplaySpace(new Vector2(textureTopLeftRect.xMax, textureTopLeftRect.yMin), videoRotationAngle);
        Vector2 d = RotateTexturePointToDisplaySpace(new Vector2(textureTopLeftRect.xMax, textureTopLeftRect.yMax), videoRotationAngle);

        float minX = Mathf.Min(a.x, b.x, c.x, d.x);
        float minY = Mathf.Min(a.y, b.y, c.y, d.y);
        float maxX = Mathf.Max(a.x, b.x, c.x, d.x);
        float maxY = Mathf.Max(a.y, b.y, c.y, d.y);

        return UnityRect.MinMaxRect(
            Mathf.Clamp01(minX),
            Mathf.Clamp01(minY),
            Mathf.Clamp01(maxX),
            Mathf.Clamp01(maxY));
    }

    private static Vector2 RotateTexturePointToDisplaySpace(Vector2 point, int videoRotationAngle)
    {
        point.x = Mathf.Clamp01(point.x);
        point.y = Mathf.Clamp01(point.y);

        return NormalizeRotationDegrees(videoRotationAngle) switch
        {
            90 => new Vector2(1f - point.y, point.x),
            180 => new Vector2(1f - point.x, 1f - point.y),
            270 => new Vector2(point.y, 1f - point.x),
            _ => point
        };
    }

    private static int NormalizeRotationDegrees(int rotationDegrees)
    {
        rotationDegrees %= 360;
        if (rotationDegrees < 0)
            rotationDegrees += 360;

        return rotationDegrees;
    }

    private int GetVideoRotationAngle()
    {
        WebCamTexture texture = GetPreviewWebCamTexture();
        return texture != null ? NormalizeRotationDegrees(texture.videoRotationAngle) : 0;
    }

    private bool GetVideoVerticallyMirrored()
    {
        WebCamTexture texture = GetPreviewWebCamTexture();
        return texture != null && texture.videoVerticallyMirrored;
    }

    private WebCamTexture GetPreviewWebCamTexture()
    {
        if (cameraPreviewFeed == null)
            cameraPreviewFeed = FindFirstObjectByType<MobileCamFeed>(FindObjectsInactive.Include);

        if (cameraPreviewFeed == null)
            return null;

        return cameraPreviewFeed.CurrentTexture as WebCamTexture;
    }

    private void LogBackCameraFacePosition(
        MpRect box,
        UnityRect rawMediaPipeRect,
        UnityRect displaySpaceTopLeftRect,
        int videoRotationAngle,
        bool videoVerticallyMirrored)
    {
        if (!debugBackCameraFacePosition)
            return;

        string facing = cameraManager != null ? cameraManager.currentFacingDirection.ToString() : "NoCameraManager";
        float boundingBoxPixelWidth = Mathf.Max(0f, box.right - box.left);
        float boundingBoxPixelHeight = Mathf.Max(0f, box.bottom - box.top);
        Vector2 rawCenter = rawMediaPipeRect.center;
        Vector2 displayCenter = displaySpaceTopLeftRect.center;
        Vector2 finalUiCenter = new Vector2(displayCenter.x, 1f - displayCenter.y);
        WebCamTexture texture = GetPreviewWebCamTexture();
        string cameraTextureSize = texture != null ? $"{texture.width}x{texture.height}" : "no_WebCamTexture";
        Debug.Log(
            $"{LogTag} FacePositionDebug " +
            $"rawMediaPipeBox=left:{box.left},top:{box.top},right:{box.right},bottom:{box.bottom} " +
            $"boundingBoxPixels={boundingBoxPixelWidth:0.0}x{boundingBoxPixelHeight:0.0} " +
            $"rawMediaPipeCenter={rawCenter} " +
            $"rawMediaPipeRectSize={rawMediaPipeRect.width:0.0000}x{rawMediaPipeRect.height:0.0000} " +
            $"displaySpaceTopLeftRectSize={displaySpaceTopLeftRect.width:0.0000}x{displaySpaceTopLeftRect.height:0.0000} " +
            $"normalizedFaceRectSize={normalizedFaceRect.width:0.0000}x{normalizedFaceRect.height:0.0000} " +
            $"inputImageSize={imageWidth}x{imageHeight} " +
            $"cameraTextureSize={cameraTextureSize} " +
            $"screenSize={UnityScreen.width}x{UnityScreen.height} " +
            $"screenOrientation={UnityScreen.orientation} " +
            $"webCamTexture.videoRotationAngle={videoRotationAngle} " +
            $"webCamTexture.videoVerticallyMirrored={videoVerticallyMirrored} " +
            $"cameraFacing={facing} " +
            $"mediaPipeRotationDegrees={GetMediaPipeRotationDegrees()} " +
            $"rotatedDisplaySpaceCenter={displayCenter} " +
            $"finalUiNormalizedCenter={finalUiCenter} " +
            $"publishedRect={FormatRect(normalizedFaceRect)} " +
            $"publishedCenter={normalizedFaceCenter}",
            this);
    }

    private void ClearFace()
    {
        hasFace = false;
        normalizedFaceCenter = Vector2.zero;
        normalizedFaceRect = new UnityRect(0f, 0f, 0f, 0f);
        faceTrackCandidates.Clear();
        normalizedFaceArea = 0f;
        bestFaceConfidence = 0f;
        hasLastRawMediaPipeBox = false;
    }

    private void LogOnce(ref bool flag, string message)
    {
        if (flag)
            return;

        flag = true;
        Debug.Log($"{LogTag} {message}", this);
    }

    private void LogRuntimeAudit(string stage, bool inferenceRan, int faceCount)
    {
        if (Time.unscaledTime < nextRuntimeAuditLogTime)
            return;

        nextRuntimeAuditLogTime = Time.unscaledTime + 1f;
        Debug.Log(
            $"{LogTag} Runtime " +
            $"stage={stage} " +
            $"enabled={enabled} " +
            $"activeInHierarchy={gameObject.activeInHierarchy} " +
            $"cameraPermission={GetCameraPermissionStatus()} " +
            $"isReady={isReady} " +
            $"isSwitchingModel={isSwitchingModel} " +
            $"facing={(cameraManager != null ? cameraManager.currentFacingDirection.ToString() : "NoCameraManager")} " +
            $"mediaPipeRotationDegrees={GetMediaPipeRotationDegrees()} " +
            $"webCamTexture.videoRotationAngle={GetVideoRotationAngle()} " +
            $"webCamTexture.videoVerticallyMirrored={GetVideoVerticallyMirrored()} " +
            $"publishedCenter={normalizedFaceCenter} " +
            $"publishedBounds={FormatRect(normalizedFaceRect)} " +
            $"hasFace={hasFace} " +
            $"inferenceRan={inferenceRan} " +
            $"faceCount={faceCount}",
            this);
    }

    private void LogAudit(bool inferenceRan, int faceCount)
    {
        string status = hasFace ? "DETECTED" : "NOT DETECTED";
        string rawBox = hasLastRawMediaPipeBox
            ? $"left:{lastRawMediaPipeBox.left},top:{lastRawMediaPipeBox.top},right:{lastRawMediaPipeBox.right},bottom:{lastRawMediaPipeBox.bottom}"
            : "none";
        string state =
            $"detector=MediaPipe.BlazeFace " +
            $"imageSource=ARCameraManager.TryAcquireLatestCpuImage " +
            $"cpuImageTransformation={XRCpuImage.Transformation.MirrorY} " +
            $"mediaPipeRotationDegrees={GetMediaPipeRotationDegrees()} " +
            $"webCamTexture.videoRotationAngle={GetVideoRotationAngle()} " +
            $"webCamTexture.videoVerticallyMirrored={GetVideoVerticallyMirrored()} " +
            $"imageWidth={imageWidth} " +
            $"imageHeight={imageHeight} " +
            $"inferenceRan={inferenceRan} " +
            $"faceCount={faceCount} " +
            $"activeModel={activeModelAssetName} " +
            $"rawMediaPipeBox={rawBox} " +
            $"normalizedCenter={normalizedFaceCenter} " +
            $"normalizedRect={normalizedFaceRect} " +
            $"status={status} " +
            $"modelError={modelError}";

        if (state == lastAuditState)
            return;

        lastAuditState = state;
        Debug.Log($"{LogTag} {state}", this);
    }

    private static string FormatRect(UnityRect rect)
    {
        return $"min({rect.xMin:0.000},{rect.yMin:0.000}) max({rect.xMax:0.000},{rect.yMax:0.000}) size({rect.width:0.000},{rect.height:0.000})";
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

        if (cameraPreviewFeed == null)
            cameraPreviewFeed = FindFirstObjectByType<MobileCamFeed>(FindObjectsInactive.Include);
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
