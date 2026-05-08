using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mediapipe;
using Mediapipe.Unity;
using Mediapipe.Tasks.Vision.FaceDetector;
using FaceDetectionResult = Mediapipe.Tasks.Components.Containers.DetectionResult;

public class HeadTracker : MonoBehaviour
{
    [Header("References")]
    public RawImage webcamDisplay;
    public RectTransform headLabel;
    public TextMeshProUGUI headLabelText;
    public Canvas canvas;

    [Header("Settings")]
    public float minConfidence = 0.5f;
    [Range(0.01f, 0.15f)] public float labelOffsetAboveHead = 0.08f;

    private WebCamTexture _webcam;
    private FaceDetector _detector;
    private FaceDetectionResult _result;
    private Texture2D _cpuTexture;
    private Color32[] _pixels;
    private bool _ready;

    IEnumerator Start()
    {
        headLabel.gameObject.SetActive(false);

        // Start webcam
        _webcam = new WebCamTexture(1280, 720, 30);
        webcamDisplay.texture = _webcam;
        _webcam.Play();
        webcamDisplay.rectTransform.localScale = new Vector3(-1, 1, 1);

        while (_webcam.width < 100)
            yield return null;

        Debug.Log("Webcam started: " + _webcam.width + "x" + _webcam.height);

        // Prepare model asset
        yield return AssetLoader.PrepareAssetAsync("blaze_face_short_range.bytes");

        // Create detector
        var baseOptions = new Mediapipe.Tasks.Core.BaseOptions(
            Mediapipe.Tasks.Core.BaseOptions.Delegate.CPU,
            modelAssetPath: "blaze_face_short_range.bytes"
        );

        var options = new FaceDetectorOptions(
            baseOptions,
            runningMode: Mediapipe.Tasks.Vision.Core.RunningMode.IMAGE,
            numFaces: 1,
            minDetectionConfidence: minConfidence,
            minSuppressionThreshold: 0.3f
        );

        _detector = FaceDetector.CreateFromOptions(options);
        _result = FaceDetectionResult.Alloc(1);

        _cpuTexture = new Texture2D(_webcam.width, _webcam.height, TextureFormat.RGBA32, false);
        _pixels = new Color32[_webcam.width * _webcam.height];

        _ready = true;
        Debug.Log("Face detector ready");
    }

    void Update()
    {
        if (!_ready || !_webcam.didUpdateThisFrame)
            return;

        _webcam.GetPixels32(_pixels);
        _cpuTexture.SetPixels32(_pixels);
        _cpuTexture.Apply();

        byte[] rawData = _cpuTexture.GetRawTextureData();

        var imageFrame = new ImageFrame(
            ImageFormat.Types.Format.Srgba,
            _webcam.width,
            _webcam.height,
            _webcam.width * 4,
            rawData
        );

        var image = new Mediapipe.Image(imageFrame);

        if (_detector.TryDetect(image, null, ref _result) && _result.detections != null && _result.detections.Count > 0)
        {
            var detection = _result.detections[0];
            Debug.Log("Detection found. Keypoints: " + (detection.keypoints != null ? detection.keypoints.Count.ToString() : "null"));
            Debug.Log("BoundingBox: " + detection.boundingBox);

            PositionLabel(detection);
            headLabel.gameObject.SetActive(true);
        }
        else
        {
            headLabel.gameObject.SetActive(false);
        }

        imageFrame.Dispose();
    }

    void PositionLabel(Mediapipe.Tasks.Components.Containers.Detection detection)
    {
        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        float canvasW = canvasRect.rect.width;
        float canvasH = canvasRect.rect.height;

        float faceCenterX;
        float faceTopY;

        if (detection.keypoints != null && detection.keypoints.Count >= 2)
        {
            // Keypoints: 0=right eye, 1=left eye, 2=nose, 3=mouth, 4=right ear, 5=left ear
            var rightEye = detection.keypoints[0];
            var leftEye = detection.keypoints[1];
            faceCenterX = (rightEye.x + leftEye.x) / 2f;
            faceTopY = Mathf.Min(rightEye.y, leftEye.y) - labelOffsetAboveHead;
        }
        else
        {
            // Fallback: bounding box
            var box = detection.boundingBox;
            faceCenterX = box.xMin + box.width / 2f;
            faceTopY = box.yMin;
        }

        // Mirror X for selfie view, convert to canvas anchored position
        float screenX = (1f - faceCenterX) * canvasW - canvasW / 2f;
        float screenY = (1f - faceTopY) * canvasH - canvasH / 2f;

        headLabel.anchoredPosition = new Vector2(screenX, screenY);
        headLabelText.text = "Hello!";
    }

    void OnDestroy()
    {
        if (_detector != null) _detector.Close();
        if (_webcam != null && _webcam.isPlaying) _webcam.Stop();
        if (_cpuTexture != null) Destroy(_cpuTexture);
    }
}