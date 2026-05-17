using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

namespace VRoom.Gestures
{
    public class ZoomController : MonoBehaviour
    {
        private enum ZoomTargetMode
        {
            RectTransformScale,
            CameraFeedCrop,
            CameraFieldOfView,
            ARCameraBackgroundCrop
        }

        [Header("Input")]
        [SerializeField]
        private PinchZoomGesture pinchZoomGesture;

        [Header("Target")]
        [SerializeField]
        private ZoomTargetMode targetMode = ZoomTargetMode.RectTransformScale;

        [SerializeField]
        private RectTransform targetPreview;

        [SerializeField]
        private RawImage targetCameraFeed;

        [SerializeField]
        private Camera targetCamera;

        [SerializeField]
        private ARCameraBackground targetArCameraBackground;

        [Header("Preview Zoom Settings")]
        [SerializeField]
        private float previewZoomSpeed = 3f;

        [SerializeField]
        private float minPreviewScale = 0.5f;

        [SerializeField]
        private float maxPreviewScale = 3f;

        [SerializeField]
        private float maxPreviewScaleStep = 0.1f;

        [Header("Camera Zoom Settings")]
        [SerializeField]
        private float minFieldOfView = 30f;

        [SerializeField]
        private float maxFieldOfView = 70f;

        [SerializeField]
        private float maxFieldOfViewStep = 1.5f;

        [Header("AR Background Zoom Settings")]
        [SerializeField]
        private float minArBackgroundZoom = 1f;

        [SerializeField]
        private float maxArBackgroundZoom = 3f;

        [SerializeField]
        private float arBackgroundZoomSpeed = 3f;

        [SerializeField]
        private float maxArBackgroundZoomStep = 0.1f;

        private float currentPreviewScale = 1f;
        private float currentCameraZoom = 1f;
        private float currentFieldOfView;
        private float currentArBackgroundZoom = 1f;
        private Matrix4x4 latestArDisplayMatrix = Matrix4x4.identity;
        private bool hasArDisplayMatrix;
        private Rect baseCameraUvRect;
        private bool hasBaseCameraUvRect;
        private static readonly int UnityDisplayTransformId = Shader.PropertyToID("_UnityDisplayTransform");

        void Awake()
        {
            if (targetPreview == null)
            {
                GameObject previewObject = GameObject.Find("PreviewImage");
                if (previewObject != null)
                    targetPreview = previewObject.GetComponent<RectTransform>();
            }

            if (targetPreview != null)
                currentPreviewScale = targetPreview.localScale.x;

            if (targetCameraFeed != null)
                CaptureBaseCameraUvRect();

            if (targetCamera == null)
                targetCamera = Camera.main;

            if (targetCamera != null)
                currentFieldOfView = targetCamera.fieldOfView;

            if (targetArCameraBackground == null && targetCamera != null)
                targetArCameraBackground = targetCamera.GetComponent<ARCameraBackground>();

            if (pinchZoomGesture == null)
            {
                Debug.LogError("PinchZoomGesture is not assigned.", this);
                enabled = false;
                return;
            }

            if (targetMode == ZoomTargetMode.RectTransformScale && targetPreview == null)
            {
                Debug.LogError("Target preview is not assigned and PreviewImage was not found.", this);
                enabled = false;
                return;
            }

            if (targetMode == ZoomTargetMode.CameraFeedCrop && targetCameraFeed == null)
            {
                Debug.LogError("Target camera feed RawImage is not assigned.", this);
                enabled = false;
            }

            if (targetMode == ZoomTargetMode.CameraFieldOfView && targetCamera == null)
            {
                Debug.LogError("Target camera is not assigned and MainCamera was not found.", this);
                enabled = false;
            }

            if (targetMode == ZoomTargetMode.ARCameraBackgroundCrop && targetArCameraBackground == null)
            {
                Debug.LogError("Target AR camera background is not assigned and was not found on the target camera.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            ARCameraManager cameraManager = targetArCameraBackground != null
                ? targetArCameraBackground.GetComponent<ARCameraManager>()
                : null;

            if (cameraManager != null)
                cameraManager.frameReceived += OnArCameraFrameReceived;
        }

        private void OnDisable()
        {
            ARCameraManager cameraManager = targetArCameraBackground != null
                ? targetArCameraBackground.GetComponent<ARCameraManager>()
                : null;

            if (cameraManager != null)
                cameraManager.frameReceived -= OnArCameraFrameReceived;
        }

        void Update()
        {
            if (!pinchZoomGesture.IsTracking)
                return;

            float zoomDelta = pinchZoomGesture.ZoomDelta;

            if (Mathf.Approximately(zoomDelta, 0f))
                return;

            if (targetMode == ZoomTargetMode.CameraFeedCrop)
                ApplyCameraFeedZoom(zoomDelta);
            else if (targetMode == ZoomTargetMode.CameraFieldOfView)
                ApplyCameraFieldOfViewZoom(zoomDelta);
            else if (targetMode == ZoomTargetMode.ARCameraBackgroundCrop)
                ApplyArBackgroundZoom(zoomDelta);
            else
                ApplyPreviewZoom(zoomDelta);
        }

        private void LateUpdate()
        {
            if (targetMode == ZoomTargetMode.ARCameraBackgroundCrop)
                ApplyArBackgroundDisplayTransform();
        }

        private void ApplyPreviewZoom(float zoomDelta)
        {
            float scaleDelta = Mathf.Clamp(
                zoomDelta * previewZoomSpeed,
                -maxPreviewScaleStep,
                maxPreviewScaleStep
            );

            currentPreviewScale = Mathf.Clamp(
                currentPreviewScale + scaleDelta,
                minPreviewScale,
                maxPreviewScale
            );

            targetPreview.localScale = Vector3.one * currentPreviewScale;
        }

        private void ApplyCameraFeedZoom(float zoomDelta)
        {
            if (!hasBaseCameraUvRect)
                CaptureBaseCameraUvRect();

            float zoomDeltaStep = Mathf.Clamp(
                zoomDelta * previewZoomSpeed,
                -maxPreviewScaleStep,
                maxPreviewScaleStep
            );

            currentCameraZoom = Mathf.Clamp(
                currentCameraZoom + zoomDeltaStep,
                1f,
                maxPreviewScale
            );

            float centerX = baseCameraUvRect.x + baseCameraUvRect.width * 0.5f;
            float centerY = baseCameraUvRect.y + baseCameraUvRect.height * 0.5f;
            float croppedWidth = baseCameraUvRect.width / currentCameraZoom;
            float croppedHeight = baseCameraUvRect.height / currentCameraZoom;

            targetCameraFeed.uvRect = new Rect(
                centerX - croppedWidth * 0.5f,
                centerY - croppedHeight * 0.5f,
                croppedWidth,
                croppedHeight
            );
        }

        private void ApplyCameraFieldOfViewZoom(float zoomDelta)
        {
            float fovDelta = Mathf.Clamp(
                zoomDelta * previewZoomSpeed,
                -maxFieldOfViewStep,
                maxFieldOfViewStep
            );

            currentFieldOfView = Mathf.Clamp(
                currentFieldOfView - fovDelta,
                minFieldOfView,
                maxFieldOfView
            );

            targetCamera.fieldOfView = currentFieldOfView;
        }

        private void ApplyArBackgroundZoom(float zoomDelta)
        {
            float zoomStep = Mathf.Clamp(
                zoomDelta * arBackgroundZoomSpeed,
                -maxArBackgroundZoomStep,
                maxArBackgroundZoomStep
            );

            currentArBackgroundZoom = Mathf.Clamp(
                currentArBackgroundZoom + zoomStep,
                minArBackgroundZoom,
                maxArBackgroundZoom
            );

            ApplyArBackgroundDisplayTransform();
        }

        private void ApplyArBackgroundDisplayTransform()
        {
            if (targetArCameraBackground == null || targetArCameraBackground.material == null || !hasArDisplayMatrix)
                return;

            targetArCameraBackground.material.SetMatrix(
                UnityDisplayTransformId,
                latestArDisplayMatrix * BuildArBackgroundCropMatrix(currentArBackgroundZoom)
            );
        }

        private void OnArCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
        {
            if (!eventArgs.displayMatrix.HasValue)
                return;

            latestArDisplayMatrix = eventArgs.displayMatrix.Value;
            hasArDisplayMatrix = true;
        }

        private static Matrix4x4 BuildArBackgroundCropMatrix(float zoom)
        {
            float scale = 1f / Mathf.Max(1f, zoom);
            float offset = (1f - scale) * 0.5f;

            Matrix4x4 matrix = Matrix4x4.identity;
            matrix.m00 = scale;
            matrix.m11 = scale;
            matrix.m20 = offset;
            matrix.m21 = offset;
            return matrix;
        }

        private void CaptureBaseCameraUvRect()
        {
            baseCameraUvRect = targetCameraFeed.uvRect;
            hasBaseCameraUvRect = true;
        }
    }
}
