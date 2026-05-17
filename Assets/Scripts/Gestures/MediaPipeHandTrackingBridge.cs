using Mediapipe.Tasks.Vision.HandLandmarker;
using UnityEngine;

namespace VRoom.Gestures
{
    public class MediaPipeHandTrackingBridge : MonoBehaviour
    {
        private const int ThumbTipIndex = 4;
        private const int IndexTipIndex = 8;

        [Header("Output")]
        [SerializeField]
        private RealHandTrackingProvider handTrackingProvider;

        [Header("Coordinate Mapping")]
        [SerializeField]
        private bool flipY = true;

        [SerializeField]
        [Range(0.01f, 1f)]
        private float landmarkSmoothing = 0.35f;

        private readonly object resultLock = new object();
        private HandLandmarkerResult pendingResult;
        private bool hasPendingResult;
        private bool hasSmoothedLandmarks;
        private Vector2 smoothedThumbTip;
        private Vector2 smoothedIndexTip;

        public void SubmitResult(HandLandmarkerResult result)
        {
            lock (resultLock)
            {
                result.CloneTo(ref pendingResult);
                hasPendingResult = true;
            }
        }

        private void Awake()
        {
            if (handTrackingProvider == null)
                handTrackingProvider = FindFirstObjectByType<RealHandTrackingProvider>();

            if (handTrackingProvider == null)
            {
                Debug.LogError("RealHandTrackingProvider is not assigned.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            HandLandmarkerResult result;

            lock (resultLock)
            {
                if (!hasPendingResult)
                    return;

                result = pendingResult;
                hasPendingResult = false;
            }

            if (!TryGetPinchPoints(result, out var thumbTip, out var indexTip))
            {
                handTrackingProvider.ClearHandFrame();
                hasSmoothedLandmarks = false;
                return;
            }

            if (!hasSmoothedLandmarks)
            {
                smoothedThumbTip = thumbTip;
                smoothedIndexTip = indexTip;
                hasSmoothedLandmarks = true;
            }
            else
            {
                smoothedThumbTip = Vector2.Lerp(smoothedThumbTip, thumbTip, landmarkSmoothing);
                smoothedIndexTip = Vector2.Lerp(smoothedIndexTip, indexTip, landmarkSmoothing);
            }

            handTrackingProvider.SetHandFrame(true, smoothedThumbTip, smoothedIndexTip);
        }

        private bool TryGetPinchPoints(HandLandmarkerResult result, out Vector2 thumbTip, out Vector2 indexTip)
        {
            thumbTip = Vector2.zero;
            indexTip = Vector2.zero;

            if (result.handLandmarks == null || result.handLandmarks.Count == 0)
                return false;

            var landmarks = result.handLandmarks[0].landmarks;
            if (landmarks == null || landmarks.Count <= IndexTipIndex)
                return false;

            var thumb = landmarks[ThumbTipIndex];
            var index = landmarks[IndexTipIndex];

            thumbTip = ToVector2(thumb.x, thumb.y);
            indexTip = ToVector2(index.x, index.y);
            return true;
        }

        private Vector2 ToVector2(float x, float y)
        {
            return new Vector2(x, flipY ? 1f - y : y);
        }
    }
}
