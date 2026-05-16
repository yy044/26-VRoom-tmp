using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    public TMP_Text statusText;

    [Header("Settings")]
    public bool startOnEnable = true;
    public bool preferRearCamera = true;
    public bool allowFrontCameraFallback = true;
    public bool mirrorX = false;
    public int requestedWidth = 1920;
    public int requestedHeight = 1080;
    public int requestedFPS = 30;

    private readonly List<WebCamDevice> cameraDevices = new List<WebCamDevice>();
    private WebCamTexture webcamTexture;
    private RectTransform displayRect;
    private RectTransform parentRect;
    private int currentCameraIndex;
    private bool isStarting;

    public Texture CurrentTexture => webcamTexture;
    public string CurrentCameraName { get; private set; } = "";
    public int Width => webcamTexture != null ? webcamTexture.width : 0;
    public int Height => webcamTexture != null ? webcamTexture.height : 0;
    public bool IsReady => webcamTexture != null && webcamTexture.isPlaying && webcamTexture.width > 16;

    private void Awake()
    {
        AutoBind();
    }

    private void OnEnable()
    {
        if (nextRearCameraButton != null)
            nextRearCameraButton.onClick.AddListener(NextRearCamera);

        if (startOnEnable)
            StartCoroutine(StartFeed());
    }

    private void OnDisable()
    {
        if (nextRearCameraButton != null)
            nextRearCameraButton.onClick.RemoveListener(NextRearCamera);
    }

    private IEnumerator StartFeed()
    {
        if (isStarting || webcamTexture != null)
            yield break;

        isStarting = true;
        AutoBind();

        if (display == null)
        {
            SetStatus("Camera preview RawImage is not assigned.");
            isStarting = false;
            yield break;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            SetStatus("Waiting for camera permission...");
            Permission.RequestUserPermission(Permission.Camera);

            float timeout = Time.realtimeSinceStartup + 5f;
            while (!Permission.HasUserAuthorizedPermission(Permission.Camera) && Time.realtimeSinceStartup < timeout)
                yield return null;
        }

        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            SetStatus("Camera permission denied.");
            isStarting = false;
            yield break;
        }
#endif

        RefreshCameraList();

        if (cameraDevices.Count == 0)
        {
            SetStatus("No usable camera devices found.");
            SetButtonText("No camera");
            isStarting = false;
            yield break;
        }

        currentCameraIndex = Mathf.Clamp(currentCameraIndex, 0, cameraDevices.Count - 1);
        StartCamera(cameraDevices[currentCameraIndex].name);
        isStarting = false;
    }

    private void Update()
    {
        if (!IsReady)
            return;

        UpdateCameraTransform();
    }

    public void NextRearCamera()
    {
        if (display == null)
        {
            ARCameraConfigSwitcher configSwitcher = FindFirstObjectByType<ARCameraConfigSwitcher>(FindObjectsInactive.Include);
            if (configSwitcher != null)
            {
                configSwitcher.NextCameraConfig();
                return;
            }
        }

        if (cameraDevices.Count == 0)
            RefreshCameraList();

        if (cameraDevices.Count == 0)
        {
            SetStatus("No usable camera devices found.");
            SetButtonText("No camera");
            return;
        }

        currentCameraIndex = (currentCameraIndex + 1) % cameraDevices.Count;
        StartCamera(cameraDevices[currentCameraIndex].name);
    }

    public void StopCamera()
    {
        if (webcamTexture == null)
            return;

        if (webcamTexture.isPlaying)
            webcamTexture.Stop();

        Destroy(webcamTexture);
        webcamTexture = null;
        CurrentCameraName = "";
    }

    private void RefreshCameraList()
    {
        cameraDevices.Clear();

        WebCamDevice[] devices = WebCamTexture.devices;

        if (devices == null || devices.Length == 0)
            return;

        for (int i = 0; i < devices.Length; i++)
        {
            bool isPreferred = preferRearCamera ? !devices[i].isFrontFacing : devices[i].isFrontFacing;
            if (isPreferred)
                cameraDevices.Add(devices[i]);
        }

        if (cameraDevices.Count == 0 && allowFrontCameraFallback)
            cameraDevices.AddRange(devices);
    }

    private void StartCamera(string cameraName)
    {
        StopCamera();

        CurrentCameraName = cameraName;
        webcamTexture = new WebCamTexture(cameraName, requestedWidth, requestedHeight, requestedFPS);
        display.texture = webcamTexture;
        webcamTexture.Play();

        SetButtonText($"Cam {currentCameraIndex + 1}: {ShortName(cameraName)}");
        SetStatus($"Started camera: {cameraName}");
    }

    private void AutoBind()
    {
        if (display == null)
        {
            RawImage[] rawImages = FindObjectsByType<RawImage>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (RawImage rawImage in rawImages)
            {
                if (rawImage.name.Contains("Preview") || rawImage.name.Contains("Camera"))
                {
                    display = rawImage;
                    break;
                }
            }

            if (display == null && rawImages.Length > 0)
                display = rawImages[0];
        }

        if (display != null)
        {
            displayRect = display.rectTransform;
            parentRect = displayRect.parent as RectTransform;
        }

        if (statusText == null)
        {
            TMP_Text[] texts = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (TMP_Text text in texts)
            {
                if (text.name.Contains("Debug") || text.name.Contains("Status"))
                {
                    statusText = text;
                    break;
                }
            }
        }
    }

    private static string ShortName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "Unknown";

        return name.Length <= 22 ? name : name.Substring(0, 22) + "...";
    }

    private void SetButtonText(string text)
    {
        if (nextRearCameraButtonText != null)
            nextRearCameraButtonText.text = text;
    }

    private void SetStatus(string text)
    {
        Debug.Log(text);

        if (statusText != null)
            statusText.text = text;
    }

    private void UpdateCameraTransform()
    {
        if (displayRect == null)
            return;

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

        Rect rect = parentRect != null ? parentRect.rect : displayRect.rect;
        float screenW = Mathf.Max(1f, rect.width);
        float screenH = Mathf.Max(1f, rect.height);
        float camAspect = Mathf.Max(1f, camW) / Mathf.Max(1f, camH);
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

    private void OnDestroy()
    {
        StopCamera();
    }
}
