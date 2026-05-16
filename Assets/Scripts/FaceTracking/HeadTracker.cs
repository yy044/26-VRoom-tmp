using TMPro;
using UnityEngine;
using Mediapipe.Unity.Sample.FaceDetection;
using FaceDetectionResult = Mediapipe.Tasks.Components.Containers.DetectionResult;

public class HeadTracker : MonoBehaviour
{
    [Header("References")]
    public FaceTrackingRunner runner;
    public RectTransform displayRect;
    public RectTransform headLabel;
    public TextMeshProUGUI headLabelText;

    [Header("Label")]
    public string labelText = "Hello!";
    public float yOffset = 40f;
    public bool mirrorX = false;
    public float smoothing = 15f;

    private FaceDetectionResult _result;

    void Start()
    {
        if (headLabel != null)
            headLabel.gameObject.SetActive(false);
    }

    void Update()
    {
        if (runner == null || displayRect == null || headLabel == null)
            return;

        if (!runner.TryGetLatestResult(ref _result))
        {
            headLabel.gameObject.SetActive(false);
            return;
        }

        if (_result.detections == null || _result.detections.Count == 0)
        {
            headLabel.gameObject.SetActive(false);
            return;
        }

        if (runner.ImageWidth <= 0 || runner.ImageHeight <= 0)
            return;

        var detection = _result.detections[0];
        var box = detection.boundingBox;

        float faceCenterX = (box.left + box.right) * 0.5f / runner.ImageWidth;
        float faceTopY = box.top / (float)runner.ImageHeight;

        if (mirrorX)
            faceCenterX = 1f - faceCenterX;

        float localX = (faceCenterX - 0.5f) * displayRect.rect.width;
        float localY = (0.5f - faceTopY) * displayRect.rect.height + yOffset;

        Vector3 targetWorldPos = displayRect.TransformPoint(new Vector3(localX, localY, 0f));

        headLabel.position = Vector3.Lerp(
            headLabel.position,
            targetWorldPos,
            1f - Mathf.Exp(-smoothing * Time.deltaTime)
        );

        headLabel.gameObject.SetActive(true);

        //if (headLabelText != null)
        //headLabelText.text = labelText;
    }
}