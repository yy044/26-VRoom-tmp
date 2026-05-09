using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class MobileCamFeed : MonoBehaviour
{
    [Header("UI")]
    public RawImage display;

    [Header("Camera Switch Button")]
    public Button nextRearCameraButton;
    public TMP_Text nextRearCameraButtonText;

    [Header("Settings")]
    public bool mirrorX = false;
    public int requestedWidth = 1920;
    public int requestedHeight = 1080;
    public int requestedFPS = 30;

    private WebCamTexture webcamTexture;
    private RectTransform displayRect;
    private RectTransform parentRect;

    private List<WebCamDevice> rearCameras = new List<WebCamDevice>();
    private int currentRearIndex = 0;
    private string currentCameraName = "";

    IEnumerator Start()
    {
        displayRect = display.GetComponent<RectTransform>();
        parentRect = displayRect.parent.GetComponent<RectTransform>();

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Permission.RequestUserPermission(Permission.Camera);
            yield return new WaitForSeconds(1f);
        }
#endif

        FindRearCameras();

        if (rearCameras.Count == 0)
        {
            SetButtonText("No rear camera");
            yield break;
        }

        if (nextRearCameraButton != null)
            nextRearCameraButton.onClick.AddListener(NextRearCamera);

        StartCamera(rearCameras[currentRearIndex].name);
    }

    void Update()
    {
        if (webcamTexture == null || !webcamTexture.isPlaying)
            return;

        UpdateCameraTransform();
    }

    void FindRearCameras()
    {
        rearCameras.Clear();

        WebCamDevice[] devices = WebCamTexture.devices;

        for (int i = 0; i < devices.Length; i++)
        {
            if (!devices[i].isFrontFacing)
            {
                rearCameras.Add(devices[i]);
                Debug.Log($"Rear camera added: {devices[i].name}");
            }
        }
    }

    public void NextRearCamera()
    {
        if (rearCameras.Count == 0)
        {
            SetButtonText("No rear camera");
            return;
        }

        currentRearIndex++;

        if (currentRearIndex >= rearCameras.Count)
            currentRearIndex = 0;

        StartCamera(rearCameras[currentRearIndex].name);
    }

    void StartCamera(string cameraName)
    {
        StopCamera();

        currentCameraName = cameraName;

        webcamTexture = new WebCamTexture(
            cameraName,
            requestedWidth,
            requestedHeight,
            requestedFPS
        );

        display.texture = webcamTexture;
        webcamTexture.Play();

        SetButtonText($"Cam {currentRearIndex}: {ShortName(cameraName)}");
        Debug.Log($"Started rear camera [{currentRearIndex}]: {cameraName}");
    }

    void StopCamera()
    {
        if (webcamTexture != null)
        {
            if (webcamTexture.isPlaying)
                webcamTexture.Stop();

            Destroy(webcamTexture);
            webcamTexture = null;
        }
    }

    string ShortName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "Unknown";

        if (name.Length <= 22)
            return name;

        return name.Substring(0, 22) + "...";
    }

    void SetButtonText(string text)
    {
        if (nextRearCameraButtonText != null)
            nextRearCameraButtonText.text = text;
    }

    void UpdateCameraTransform()
    {
        int rotation = webcamTexture.videoRotationAngle;
        bool rotated = rotation == 90 || rotation == 270;

        displayRect.localEulerAngles = new Vector3(0f, 0f, -rotation);

        float camW = webcamTexture.width;
        float camH = webcamTexture.height;

        if (rotated)
        {
            float temp = camW;
            camW = camH;
            camH = temp;
        }

        float screenW = parentRect.rect.width;
        float screenH = parentRect.rect.height;

        float camAspect = camW / camH;
        float screenAspect = screenW / screenH;

        float width;
        float height;

        if (camAspect > screenAspect)
        {
            height = screenH;
            width = height * camAspect;
        }
        else
        {
            width = screenW;
            height = width / camAspect;
        }

        displayRect.sizeDelta = new Vector2(width, height);
        displayRect.anchoredPosition = Vector2.zero;

        float xScale = mirrorX ? -1f : 1f;
        float yScale = webcamTexture.videoVerticallyMirrored ? -1f : 1f;

        displayRect.localScale = new Vector3(xScale, yScale, 1f);
    }

    void OnDestroy()
    {
        StopCamera();
    }
}