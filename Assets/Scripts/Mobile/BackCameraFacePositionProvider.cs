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

public class BackCameraFacePositionProvider : MonoBehaviour, IFacePositionProvider
{
    private const string LogTag = "[BackFace2DAudit]";
    private const string ModelPath = "blaze_face_short_range.bytes";

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
    [SerializeField, Range(0f, 1f)] private float minDetectionConfidence = 0.5f;
    [SerializeField, Range(0f, 1f)] private float minSuppressionThreshold = 0.3f;

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
    private string modelError = "";
    private readonly System.Diagnostics.Stopwatch stopwatch = new();

    public bool HasFace => hasFace;
    public Vector2 NormalizedFaceCenter => normalizedFaceCenter;
    public UnityRect NormalizedFaceRect => normalizedFaceRect;
    public string SourceName => "BackFace2D";

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

        yield return PrepareModel();

        if (!string.IsNullOrEmpty(modelError))
        {
            LogAudit(false, 0);
            enabled = false;
            yield break;
        }

        var options = new FaceDetectorOptions(
            new BaseOptions(delegateMode, modelAssetPath: ModelPath),
            runningMode: TasksRunningMode.VIDEO,
            minDetectionConfidence: minDetectionConfidence,
            minSuppressionThreshold: minSuppressionThreshold,
            numFaces: maxFaces);

        faceDetector = FaceDetector.CreateFromOptions(options, GpuManager.GpuResources);
        result = FaceDetectionResult.Alloc(maxFaces);
        stopwatch.Restart();
        isReady = true;
        LogAudit(false, 0);
    }

    private IEnumerator PrepareModel()
    {
        IEnumerator prepareRoutine = null;
        try
        {
            prepareRoutine = AssetLoader.PrepareAssetAsync(ModelPath);
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
        if (!isReady || Time.unscaledTime < nextFrameTime)
            return;

        nextFrameTime = Time.unscaledTime + 1f / Mathf.Max(1f, targetFps);

        bool inferenceRan = false;
        int faceCount = 0;

        if (cameraManager.currentFacingDirection != CameraFacingDirection.World)
        {
            ClearFace();
            LogAudit(false, 0);
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
            return;
        }

        using (cpuImage)
        {
            if (!TryUpdateInputTexture(cpuImage))
            {
                ClearFace();
                LogAudit(false, 0);
                return;
            }
        }

        using var image = new Image(inputTexture);
        long timestamp = stopwatch.ElapsedTicks / TimeSpan.TicksPerMillisecond;
        inferenceRan = faceDetector.TryDetectForVideo(image, timestamp, GetImageProcessingOptions(), ref result);
        faceCount = inferenceRan && result.detections != null ? result.detections.Count : 0;

        if (inferenceRan && faceCount > 0)
            SetFaceFromDetection(result.detections[0].boundingBox);
        else
            ClearFace();

        LogAudit(inferenceRan, faceCount);
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
        hasFace = true;
    }

    private void ClearFace()
    {
        hasFace = false;
        normalizedFaceCenter = Vector2.zero;
        normalizedFaceRect = new UnityRect(0f, 0f, 0f, 0f);
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
            $"normalizedCenter={normalizedFaceCenter} " +
            $"normalizedRect={normalizedFaceRect} " +
            $"status={status} " +
            $"modelError={modelError}";

        if (state == lastAuditState)
            return;

        lastAuditState = state;
        Debug.Log($"{LogTag} {state}", this);
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
