using System.Collections;
using Mediapipe;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Unity;
using Mediapipe.Unity.Sample;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using VRoom.Gestures;

public class ARCameraHandLandmarkerRunner : MonoBehaviour
{
    private const string LogTag = "[ARHandLandmarker]";

    [Header("References")]
    [SerializeField] private ARCameraManager cameraManager;
    [SerializeField] private MediaPipeHandTrackingBridge handTrackingBridge;
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
    [SerializeField, Range(1, 2)] private int numHands = 1;
    [SerializeField, Range(0f, 1f)] private float minHandDetectionConfidence = 0.5f;
    [SerializeField, Range(0f, 1f)] private float minHandPresenceConfidence = 0.5f;
    [SerializeField, Range(0f, 1f)] private float minTrackingConfidence = 0.5f;

    private const string ModelPath = "hand_landmarker.bytes";

    private HandLandmarker handLandmarker;
    private Texture2D inputTexture;
    private HandLandmarkerResult result;
    private bool isReady;
    private float nextFrameTime;
    private float nextMissingCpuImageWarningTime;
    private readonly System.Diagnostics.Stopwatch stopwatch = new();

    private IEnumerator Start()
    {
        Debug.Log($"{LogTag} starting.", this);
        AutoBind();

        if (cameraManager == null)
        {
            Debug.LogError($"{LogTag} ARCameraManager is not assigned.", this);
            enabled = false;
            yield break;
        }

        if (handTrackingBridge == null)
        {
            Debug.LogError($"{LogTag} MediaPipeHandTrackingBridge is not assigned.", this);
            enabled = false;
            yield break;
        }

        Debug.Log($"{LogTag} preparing MediaPipe resources.", this);
        AssetLoader.Provide(new StreamingAssetsResourceManager());

        if (delegateMode == BaseOptions.Delegate.GPU && !GpuManager.IsInitialized)
        {
            Debug.Log($"{LogTag} initializing GPU resources.", this);
            yield return GpuManager.Initialize();

            if (!GpuManager.IsInitialized)
            {
                Debug.LogWarning($"{LogTag} GPU resources are not available. Falling back to CPU.", this);
                delegateMode = BaseOptions.Delegate.CPU;
            }
        }

        Debug.Log($"{LogTag} preparing model: {ModelPath}.", this);
        yield return AssetLoader.PrepareAssetAsync(ModelPath);

        var options = new HandLandmarkerOptions(
            new BaseOptions(delegateMode, modelAssetPath: ModelPath),
            runningMode: Mediapipe.Tasks.Vision.Core.RunningMode.VIDEO,
            numHands: numHands,
            minHandDetectionConfidence: minHandDetectionConfidence,
            minHandPresenceConfidence: minHandPresenceConfidence,
            minTrackingConfidence: minTrackingConfidence
        );

        handLandmarker = HandLandmarker.CreateFromOptions(options, GpuManager.GpuResources);
        result = HandLandmarkerResult.Alloc(numHands);
        stopwatch.Restart();
        isReady = true;

        Debug.Log($"{LogTag} ready. targetFps={targetFps}, maxInputWidth={maxInputWidth}, delegate={delegateMode}.", this);
    }

    private void Update()
    {
        if (!isReady || Time.unscaledTime < nextFrameTime)
            return;

        nextFrameTime = Time.unscaledTime + 1f / Mathf.Max(1f, targetFps);

        if (!cameraManager.TryAcquireLatestCpuImage(out var cpuImage))
        {
            if (Time.unscaledTime >= nextMissingCpuImageWarningTime)
            {
                Debug.LogWarning($"{LogTag} no AR CPU image yet.", this);
                nextMissingCpuImageWarningTime = Time.unscaledTime + 3f;
            }
            return;
        }

        using (cpuImage)
        {
            if (!TryUpdateInputTexture(cpuImage))
                return;
        }

        using var image = new Image(inputTexture);
        var timestamp = stopwatch.ElapsedTicks / System.TimeSpan.TicksPerMillisecond;

        if (handLandmarker.TryDetectForVideo(image, timestamp, GetImageProcessingOptions(), ref result))
        {
            handTrackingBridge.SubmitResult(result);
        }
        else
        {
            handTrackingBridge.SubmitResult(default);
        }
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
        return true;
    }

    private ImageProcessingOptions GetImageProcessingOptions()
    {
        int rotationDegrees = 0;

        if (cameraManager.TryGetIntrinsics(out _))
        {
            switch (UnityEngine.Screen.orientation)
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
        }

        return new ImageProcessingOptions(rotationDegrees: rotationDegrees);
    }

    private void AutoBind()
    {
        if (cameraManager == null)
            cameraManager = FindFirstObjectByType<ARCameraManager>(FindObjectsInactive.Include);

        if (handTrackingBridge == null)
            handTrackingBridge = FindFirstObjectByType<MediaPipeHandTrackingBridge>(FindObjectsInactive.Include);
    }

    private void OnDestroy()
    {
        handLandmarker?.Close();
        handLandmarker = null;

        if (inputTexture != null)
        {
            Destroy(inputTexture);
            inputTexture = null;
        }
    }
}
