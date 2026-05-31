using Mediapipe.Tasks.Vision.HandLandmarker;
using UnityEngine;

namespace VRoom.Gestures
{
    public class MediaPipeHandTrackingBridge : MonoBehaviour
    {
        private enum PreferredHand
        {
            Any,
            Left,
            Right
        }

        private const int WristIndex = 0;
        private const int ThumbTipIndex = 4;
        private const int IndexTipIndex = 8;
        private const int MiddleMcpIndex = 9;

        [Header("Output")]
        [SerializeField]
        private RealHandTrackingProvider handTrackingProvider;

        [Header("Coordinate Mapping")]
        [SerializeField]
        private bool flipY = true;

        [SerializeField]
        [Range(0.01f, 1f)]
        private float landmarkSmoothing = 0.35f;

        [Header("Hand Selection")]
        [SerializeField]
        private PreferredHand preferredHand = PreferredHand.Any;

        [SerializeField]
        [Range(0f, 1f)]
        private float minHandednessScore = 0.6f;

        private readonly object resultLock = new object();
        private HandLandmarkerResult pendingResult;
        private bool hasPendingResult;
        private bool hasSmoothedLandmarks;
        private Vector2 smoothedThumbTip;
        private Vector2 smoothedIndexTip;
        private float smoothedHandScale;
        private string activeHandLabel;
        private bool hasActiveHand;

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

            if (!TryGetPinchPoints(result, out var thumbTip, out var indexTip, out var handScale))
            {
                handTrackingProvider.ClearHandFrame();
                hasSmoothedLandmarks = false;
                hasActiveHand = false;
                activeHandLabel = null;
                return;
            }

            if (!hasSmoothedLandmarks)
            {
                smoothedThumbTip = thumbTip;
                smoothedIndexTip = indexTip;
                smoothedHandScale = handScale;
                hasSmoothedLandmarks = true;
            }
            else
            {
                smoothedThumbTip = Vector2.Lerp(smoothedThumbTip, thumbTip, landmarkSmoothing);
                smoothedIndexTip = Vector2.Lerp(smoothedIndexTip, indexTip, landmarkSmoothing);
                smoothedHandScale = Mathf.Lerp(smoothedHandScale, handScale, landmarkSmoothing);
            }

            handTrackingProvider.SetHandFrame(true, smoothedThumbTip, smoothedIndexTip, smoothedHandScale);
        }

        private bool TryGetPinchPoints(HandLandmarkerResult result, out Vector2 thumbTip, out Vector2 indexTip, out float handScale)
        {
            thumbTip = Vector2.zero;
            indexTip = Vector2.zero;
            handScale = 1f;

            if (result.handLandmarks == null || result.handLandmarks.Count == 0)
                return false;

            int handIndex = SelectBestHand(result);
            if (handIndex < 0)
                return false;

            var landmarks = result.handLandmarks[handIndex].landmarks;
            if (landmarks == null || landmarks.Count <= MiddleMcpIndex)
                return false;

            var thumb = landmarks[ThumbTipIndex];
            var index = landmarks[IndexTipIndex];
            var wrist = landmarks[WristIndex];
            var middleMcp = landmarks[MiddleMcpIndex];

            thumbTip = ToVector2(thumb.x, thumb.y);
            indexTip = ToVector2(index.x, index.y);
            handScale = Vector2.Distance(
                ToVector2(wrist.x, wrist.y),
                ToVector2(middleMcp.x, middleMcp.y)
            );

            if (TryGetHandedness(result, handIndex, out var label, out _))
            {
                activeHandLabel = label;
                hasActiveHand = true;
            }

            return true;
        }

        private int SelectBestHand(HandLandmarkerResult result)
        {
            int bestIndex = -1;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < result.handLandmarks.Count; i++)
            {
                if (!TryGetHandedness(result, i, out var label, out var score))
                    continue;

                if (score < minHandednessScore || !MatchesPreferredHand(label))
                    continue;

                float selectionScore = score;
                if (hasActiveHand && label == activeHandLabel)
                    selectionScore += 1f;

                if (selectionScore > bestScore)
                {
                    bestScore = selectionScore;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private bool TryGetHandedness(HandLandmarkerResult result, int handIndex, out string label, out float score)
        {
            label = null;
            score = 0f;

            if (result.handedness == null || handIndex < 0 || handIndex >= result.handedness.Count)
                return false;

            var handedness = result.handedness[handIndex].categories;
            if (handedness == null || handedness.Count == 0)
                return false;

            var category = handedness[0];
            label = string.IsNullOrEmpty(category.categoryName)
                ? category.displayName
                : category.categoryName;
            score = category.score;
            return !string.IsNullOrEmpty(label);
        }

        private bool MatchesPreferredHand(string label)
        {
            return preferredHand == PreferredHand.Any || label == preferredHand.ToString();
        }

        private Vector2 ToVector2(float x, float y)
        {
            return new Vector2(x, flipY ? 1f - y : y);
        }
    }
}
