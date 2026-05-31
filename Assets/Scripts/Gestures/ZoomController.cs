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

        [Header("Zoom Response")]
        [SerializeField]
        private float zoomFollowSpeed = 18f;

        [Header("Preview Zoom Settings")]
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
        private float maxArBackgroundZoomStep = 0.05f;

        private float currentPreviewScale = 1f;
        private float currentCameraZoom = 1f;
        private float currentFieldOfView;
        private float currentArBackgroundZoom = 1f;
        private float initialPreviewScale = 1f;
        private float initialFieldOfView;
        private float previewScaleAtGestureStart = 1f;
        private float cameraZoomAtGestureStart = 1f;
        private float fieldOfViewAtGestureStart;
        private float arBackgroundZoomAtGestureStart = 1f;
        private int activeGestureSessionId = -1;
        private Matrix4x4 latestArDisplayMatrix = Matrix4x4.identity;
        private bool hasArDisplayMatrix;
        private Rect baseCameraUvRect;
        private bool hasBaseCameraUvRect;
        private bool resetZoomInProgress;
        private static readonly int UnityDisplayTransformId = Shader.PropertyToID("_UnityDisplayTransform");
        private static readonly Vector2 ScreenCenter = new Vector2(0.5f, 0.5f);

        void Awake()
        {
            if (targetPreview == null)
            {
                GameObject previewObject = GameObject.Find("PreviewImage");
                if (previewObject != null)
                    targetPreview = previewObject.GetComponent<RectTransform>();
            }

            if (targetPreview != null)
            {
                currentPreviewScale = targetPreview.localScale.x;
                initialPreviewScale = currentPreviewScale;
            }

            if (targetCameraFeed != null)
                CaptureBaseCameraUvRect();

            if (targetCamera == null)
                targetCamera = Camera.main;

            if (targetCamera != null)
            {
                currentFieldOfView = targetCamera.fieldOfView;
                initialFieldOfView = currentFieldOfView;
            }

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
            if (pinchZoomGesture.ResetZoomRequested)
                resetZoomInProgress = true;

            if (!pinchZoomGesture.IsTracking && !resetZoomInProgress)
                return;

            if (pinchZoomGesture.IsTracking && !resetZoomInProgress)
                CaptureGestureStartIfNeeded();

            if (targetMode == ZoomTargetMode.CameraFeedCrop)
                ApplyCameraFeedZoom();
            else if (targetMode == ZoomTargetMode.CameraFieldOfView)
                ApplyCameraFieldOfViewZoom();
            else if (targetMode == ZoomTargetMode.ARCameraBackgroundCrop)
                ApplyArBackgroundZoom();
            else
                ApplyPreviewZoom();
        }

        private void LateUpdate()
        {
            if (targetMode == ZoomTargetMode.ARCameraBackgroundCrop)
                ApplyArBackgroundDisplayTransform();
        }

        private void ApplyPreviewZoom()
        {
            float targetScale = resetZoomInProgress
                ? Mathf.Clamp(initialPreviewScale, minPreviewScale, maxPreviewScale)
                : Mathf.Clamp(
                    previewScaleAtGestureStart * pinchZoomGesture.ZoomScaleRatio,
                    minPreviewScale,
                    maxPreviewScale
                );
            currentPreviewScale = MoveToward(currentPreviewScale, targetScale, maxPreviewScaleStep);

            targetPreview.localScale = Vector3.one * currentPreviewScale;
            resetZoomInProgress = resetZoomInProgress && !Mathf.Approximately(currentPreviewScale, targetScale);
        }

        private void ApplyCameraFeedZoom()
        {
            if (!hasBaseCameraUvRect)
                CaptureBaseCameraUvRect();

            float targetCameraZoom = resetZoomInProgress
                ? 1f
                : Mathf.Clamp(
                    cameraZoomAtGestureStart * pinchZoomGesture.ZoomScaleRatio,
                    1f,
                    maxPreviewScale
                );
            currentCameraZoom = MoveToward(currentCameraZoom, targetCameraZoom, maxPreviewScaleStep);

            Vector2 zoomCenter = ScreenCenter;
            float centerX = baseCameraUvRect.x + baseCameraUvRect.width * zoomCenter.x;
            float centerY = baseCameraUvRect.y + baseCameraUvRect.height * zoomCenter.y;
            float croppedWidth = baseCameraUvRect.width / currentCameraZoom;
            float croppedHeight = baseCameraUvRect.height / currentCameraZoom;
            float minX = baseCameraUvRect.x;
            float maxX = baseCameraUvRect.x + baseCameraUvRect.width - croppedWidth;
            float minY = baseCameraUvRect.y;
            float maxY = baseCameraUvRect.y + baseCameraUvRect.height - croppedHeight;

            targetCameraFeed.uvRect = new Rect(
                Mathf.Clamp(centerX - croppedWidth * 0.5f, minX, maxX),
                Mathf.Clamp(centerY - croppedHeight * 0.5f, minY, maxY),
                croppedWidth,
                croppedHeight
            );
            resetZoomInProgress = resetZoomInProgress && !Mathf.Approximately(currentCameraZoom, targetCameraZoom);
        }

        private void ApplyCameraFieldOfViewZoom()
        {
            float targetFieldOfView = resetZoomInProgress
                ? Mathf.Clamp(initialFieldOfView, minFieldOfView, maxFieldOfView)
                : Mathf.Clamp(
                    fieldOfViewAtGestureStart / Mathf.Max(0.01f, pinchZoomGesture.ZoomScaleRatio),
                    minFieldOfView,
                    maxFieldOfView
                );
            currentFieldOfView = MoveToward(currentFieldOfView, targetFieldOfView, maxFieldOfViewStep);

            targetCamera.fieldOfView = currentFieldOfView;
            resetZoomInProgress = resetZoomInProgress && !Mathf.Approximately(currentFieldOfView, targetFieldOfView);
        }

        private void ApplyArBackgroundZoom()
        {
            float targetArBackgroundZoom = resetZoomInProgress
                ? minArBackgroundZoom
                : Mathf.Clamp(
                    arBackgroundZoomAtGestureStart * pinchZoomGesture.ZoomScaleRatio,
                    minArBackgroundZoom,
                    maxArBackgroundZoom
                );
            currentArBackgroundZoom = MoveToward(currentArBackgroundZoom, targetArBackgroundZoom, maxArBackgroundZoomStep);

            ApplyArBackgroundDisplayTransform();
            resetZoomInProgress = resetZoomInProgress && !Mathf.Approximately(currentArBackgroundZoom, targetArBackgroundZoom);
        }

        private void CaptureGestureStartIfNeeded()
        {
            if (activeGestureSessionId == pinchZoomGesture.GestureSessionId)
                return;

            activeGestureSessionId = pinchZoomGesture.GestureSessionId;
            previewScaleAtGestureStart = currentPreviewScale;
            cameraZoomAtGestureStart = currentCameraZoom;
            fieldOfViewAtGestureStart = currentFieldOfView;
            arBackgroundZoomAtGestureStart = currentArBackgroundZoom;
        }

        private float MoveToward(float current, float target, float maxStep)
        {
            float smoothingStep = Mathf.Abs(target - current) * GetZoomFollowAmount();
            float step = Mathf.Min(Mathf.Max(0f, maxStep), smoothingStep);
            return Mathf.MoveTowards(current, target, step);
        }

        private float GetZoomFollowAmount()
        {
            return 1f - Mathf.Exp(-Mathf.Max(0f, zoomFollowSpeed) * Time.deltaTime);
        }

        private void ApplyArBackgroundDisplayTransform()
        {
            if (targetArCameraBackground == null || targetArCameraBackground.material == null || !hasArDisplayMatrix)
                return;

            targetArCameraBackground.material.SetMatrix(
                UnityDisplayTransformId,
                latestArDisplayMatrix * BuildArBackgroundCropMatrix(currentArBackgroundZoom, ScreenCenter)
            );
        }

        private void OnArCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
        {
            if (!eventArgs.displayMatrix.HasValue)
                return;

            latestArDisplayMatrix = eventArgs.displayMatrix.Value;
            hasArDisplayMatrix = true;
        }

        private static Matrix4x4 BuildArBackgroundCropMatrix(float zoom, Vector2 center)
        {
            float scale = 1f / Mathf.Max(1f, zoom);
            Vector2 clampedCenter = Clamp01(center);
            float maxOffset = 1f - scale;
            float offsetX = Mathf.Clamp(clampedCenter.x - scale * 0.5f, 0f, maxOffset);
            float offsetY = Mathf.Clamp(clampedCenter.y - scale * 0.5f, 0f, maxOffset);

            Matrix4x4 matrix = Matrix4x4.identity;
            matrix.m00 = scale;
            matrix.m11 = scale;
            matrix.m20 = offsetX;
            matrix.m21 = offsetY;
            return matrix;
        }

        private static Vector2 Clamp01(Vector2 value)
        {
            return new Vector2(
                Mathf.Clamp01(value.x),
                Mathf.Clamp01(value.y)
            );
        }

        private void CaptureBaseCameraUvRect()
        {
            baseCameraUvRect = targetCameraFeed.uvRect;
            hasBaseCameraUvRect = true;
        }
    }
}
