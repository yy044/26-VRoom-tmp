using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

public class MobileSceneAutoBinder : MonoBehaviour
{
    [SerializeField] private bool bindOnAwake = true;

    private void Awake()
    {
        if (bindOnAwake)
            BindScene();
    }

    [ContextMenu("Bind Scene")]
    public void BindScene()
    {
        RawImage preview = FindPreviewImage();
        Button cameraButton = FindFirstObjectByType<Button>(FindObjectsInactive.Include);
        TMP_Text cameraButtonText = FindTextByName("CameraButton", "Camera Change", "Cam");
        TMP_Text statusText = FindTextByName("DebugText", "Status", "Debug");

        MobileCamFeed mobileCamFeed = FindFirstObjectByType<MobileCamFeed>(FindObjectsInactive.Include);
        if (mobileCamFeed != null)
        {
            if (mobileCamFeed.display == null)
                mobileCamFeed.display = preview;

            if (mobileCamFeed.nextRearCameraButton == null)
                mobileCamFeed.nextRearCameraButton = cameraButton;

            if (mobileCamFeed.nextRearCameraButtonText == null)
                mobileCamFeed.nextRearCameraButtonText = cameraButtonText;

            if (mobileCamFeed.statusText == null)
                mobileCamFeed.statusText = statusText;
        }

        MobileARFaceTrackingRunner faceRunner = FindFirstObjectByType<MobileARFaceTrackingRunner>(FindObjectsInactive.Include);
        if (faceRunner != null)
        {
            if (faceRunner.trackingCamera == null)
                faceRunner.trackingCamera = Camera.main;

            if (faceRunner.faceManager == null)
                faceRunner.faceManager = FindFirstObjectByType<ARFaceManager>(FindObjectsInactive.Include);
        }

        MobileARHeadTracker headTracker = FindFirstObjectByType<MobileARHeadTracker>(FindObjectsInactive.Include);
        if (headTracker != null)
        {
            if (headTracker.runner == null)
                headTracker.runner = faceRunner;

            if (headTracker.displayRect == null && preview != null)
                headTracker.displayRect = preview.rectTransform;
        }

        ARCameraConfigSwitcher configSwitcher = FindFirstObjectByType<ARCameraConfigSwitcher>(FindObjectsInactive.Include);
        if (configSwitcher != null)
        {
            if (configSwitcher.cameraManager == null)
                configSwitcher.cameraManager = FindFirstObjectByType<ARCameraManager>(FindObjectsInactive.Include);

            if (configSwitcher.statusText == null)
                configSwitcher.statusText = statusText;
        }

        MobileARModeController modeController = FindFirstObjectByType<MobileARModeController>(FindObjectsInactive.Include);
        if (modeController != null)
        {
            if (modeController.arSession == null)
                modeController.arSession = FindFirstObjectByType<ARSession>(FindObjectsInactive.Include);

            if (modeController.xrOrigin == null)
                modeController.xrOrigin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include);

            if (modeController.cameraManager == null)
                modeController.cameraManager = FindFirstObjectByType<ARCameraManager>(FindObjectsInactive.Include);

            if (modeController.faceManager == null)
                modeController.faceManager = FindFirstObjectByType<ARFaceManager>(FindObjectsInactive.Include);

            if (modeController.planeManager == null)
                modeController.planeManager = FindFirstObjectByType<ARPlaneManager>(FindObjectsInactive.Include);

            if (modeController.raycastManager == null)
                modeController.raycastManager = FindFirstObjectByType<ARRaycastManager>(FindObjectsInactive.Include);

            if (modeController.handLandmarkerRunner == null)
                modeController.handLandmarkerRunner = FindFirstObjectByType<ARCameraHandLandmarkerRunner>(FindObjectsInactive.Include);

            if (modeController.faceTrackingRunner == null)
                modeController.faceTrackingRunner = faceRunner;

            if (modeController.statusText == null)
                modeController.statusText = statusText;
        }
    }

    private static RawImage FindPreviewImage()
    {
        RawImage[] images = FindObjectsByType<RawImage>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (RawImage image in images)
        {
            if (image.name.Contains("Preview") || image.name.Contains("Camera"))
                return image;
        }

        return images.Length > 0 ? images[0] : null;
    }

    private static TMP_Text FindTextByName(params string[] tokens)
    {
        TMP_Text[] texts = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (TMP_Text text in texts)
        {
            foreach (string token in tokens)
            {
                if (text.name.Contains(token) || text.text.Contains(token))
                    return text;
            }
        }

        return texts.Length > 0 ? texts[0] : null;
    }

}
