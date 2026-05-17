using UnityEngine;
using UnityEngine.UI;

namespace VRoom.Gestures
{
    public class ZoomController : MonoBehaviour
    {
        private enum ZoomTargetMode
        {
            RectTransformScale,
            CameraFeedCrop
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

        [Header("Preview Zoom Settings")]
        [SerializeField]
        private float previewZoomSpeed = 1.2f;

        [SerializeField]
        private float minPreviewScale = 0.5f;

        [SerializeField]
        private float maxPreviewScale = 3f;

        [SerializeField]
        private float maxPreviewScaleStep = 0.015f;

        private float currentPreviewScale = 1f;
        private float currentCameraZoom = 1f;
        private Rect baseCameraUvRect;
        private bool hasBaseCameraUvRect;

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
            else
                ApplyPreviewZoom(zoomDelta);
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

        private void CaptureBaseCameraUvRect()
        {
            baseCameraUvRect = targetCameraFeed.uvRect;
            hasBaseCameraUvRect = true;
        }
    }
}
