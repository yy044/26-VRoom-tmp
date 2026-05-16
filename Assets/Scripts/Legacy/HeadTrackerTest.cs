using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HeadTrackerTest : MonoBehaviour
{
    public WebCamFeed webcamFeed;
    public RectTransform headLabel;
    public TextMeshProUGUI headLabelText;
    public Canvas canvas;

    [Header("Simulated Face (drag sliders in Inspector)")]
    [Range(0f, 1f)] public float faceX = 0.5f;
    [Range(0f, 1f)] public float faceY = 0.35f;
    [Range(0.05f, 0.4f)] public float faceSize = 0.2f;

    void Update()
    {
        if (!webcamFeed.IsReady) return;

        headLabel.gameObject.SetActive(true);
        headLabelText.text = "Hello!";

        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        float canvasW = canvasRect.rect.width;
        float canvasH = canvasRect.rect.height;

        // Position label using simulated face coords
        float screenX = (1f - faceX) * canvasW;  // mirrored
        float screenY = (1f - faceY) * canvasH;
        float offset = faceSize * canvasH * 0.15f;

        headLabel.anchoredPosition = new Vector2(
            screenX - canvasW / 2f,
            screenY - canvasH / 2f + offset
        );
    }
}
