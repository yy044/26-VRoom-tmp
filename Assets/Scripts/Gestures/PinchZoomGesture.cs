using UnityEngine;

namespace VRoom.Gestures
{
    public class PinchZoomGesture : MonoBehaviour
    {
        private enum GestureState
        {
            Idle,
            Primed,
            Zooming
        }

        [Header("Input")]
        [SerializeField]
        private MonoBehaviour handTrackingProviderBehaviour;

        [Header("Gesture Settings")]
        [SerializeField]
        private float noiseThreshold = 0.05f;

        [SerializeField]
        private float ratioSensitivity = 1f;

        [SerializeField]
        private float activationRatioThreshold = 0.12f;

        [SerializeField]
        private float pinchStartThreshold = 0.45f;

        [SerializeField]
        [Min(1)]
        private int pinchReadyFrameCount = 2;

        [SerializeField]
        private float repinchThreshold = 0.45f;

        [SerializeField]
        [Min(1)]
        private int repinchReadyFrameCount = 2;

        [SerializeField]
        [Range(0.01f, 1f)]
        private float distanceSmoothing = 0.35f;

        [SerializeField]
        private float minValidDistance = 0.15f;

        [SerializeField]
        private float maxValidDistance = 2.5f;

        [SerializeField]
        private float minValidHandScale = 0.03f;

        [SerializeField]
        private float maxDistanceJump = 0.5f;

        [SerializeField]
        [Min(0)]
        private int warmupFrameCount = 2;

        [SerializeField]
        [Min(0)]
        private int trackingLossGraceFrames = 2;

        private IHandTrackingProvider handTrackingProvider;
        private GestureState state = GestureState.Idle;
        private float smoothedDistance;
        private float gestureStartDistance;
        private int remainingWarmupFrames;
        private int missingFrameCount;
        private int readyFrameCount;
        private int repinchFrameCount;
        private bool canRebaselineAfterExpansion;
        private float peakRawRatio = 1f;

        public float ZoomDelta { get; private set; }
        public float ZoomScaleRatio { get; private set; } = 1f;
        public bool ResetZoomRequested { get; private set; }
        public bool IsTracking { get; private set; }
        public Vector2 PinchCenter { get; private set; } = new Vector2(0.5f, 0.5f);
        public int GestureSessionId { get; private set; }

        void Awake()
        {
            handTrackingProvider = handTrackingProviderBehaviour as IHandTrackingProvider;

            if (handTrackingProvider == null)
            {
                Debug.LogError("Hand tracking provider must implement IHandTrackingProvider.", this);
                enabled = false;
            }
        }

        void Update()
        {
            ZoomDelta = 0f;
            ResetZoomRequested = false;

            if (!handTrackingProvider.TryGetHandFrame(out var frame) || !frame.isTracked)
            {
                HandleTrackingLost();
                return;
            }

            if (frame.handScale < minValidHandScale)
            {
                HandleTrackingLost();
                return;
            }

            float currentDistance = Vector2.Distance(frame.thumbTip, frame.indexTip) / frame.handScale;
            if (currentDistance < minValidDistance || currentDistance > maxValidDistance)
            {
                HandleTrackingLost();
                return;
            }

            missingFrameCount = 0;
            PinchCenter = frame.pinchCenter;

            if (state == GestureState.Idle)
            {
                TryBeginPrimed(currentDistance);
                return;
            }

            if (Mathf.Abs(currentDistance - smoothedDistance) > maxDistanceJump)
            {
                BeginPrimed(currentDistance);
                return;
            }

            smoothedDistance = Mathf.Lerp(smoothedDistance, currentDistance, distanceSmoothing);

            if (remainingWarmupFrames > 0)
            {
                gestureStartDistance = smoothedDistance;
                ZoomScaleRatio = 1f;
                IsTracking = false;
                remainingWarmupFrames--;
                return;
            }

            float rawRatio = GetRawRatio();

            if (state == GestureState.Primed)
            {
                if (Mathf.Abs(rawRatio - 1f) < activationRatioThreshold)
                {
                    ZoomScaleRatio = 1f;
                    IsTracking = false;
                    return;
                }

                BeginZoomSession();
            }

            if (state == GestureState.Zooming && TryApplyZoomReset(rawRatio))
                return;

            if (state == GestureState.Zooming && TryApplyClutchRebaseline(ref rawRatio))
                return;

            ApplyZoomRatio(rawRatio);
        }

        private float GetRawRatio()
        {
            float rawRatio = smoothedDistance / Mathf.Max(gestureStartDistance, minValidDistance);

            if (Mathf.Abs(smoothedDistance - gestureStartDistance) < noiseThreshold)
                return 1f;

            return rawRatio;
        }

        private void ApplyZoomRatio(float rawRatio)
        {
            IsTracking = true;
            ZoomScaleRatio = Mathf.Pow(Mathf.Max(0.01f, rawRatio), Mathf.Max(0.01f, ratioSensitivity));
            ZoomDelta = ZoomScaleRatio - 1f;
        }

        private bool TryApplyZoomReset(float rawRatio)
        {
            if (rawRatio > 1f - activationRatioThreshold)
                return false;

            IsTracking = true;
            ResetZoomRequested = true;
            ZoomScaleRatio = 1f;
            ZoomDelta = 0f;
            return true;
        }

        private void BeginPrimed(float currentDistance)
        {
            state = GestureState.Primed;
            IsTracking = false;
            missingFrameCount = 0;
            readyFrameCount = 0;
            repinchFrameCount = 0;
            canRebaselineAfterExpansion = false;
            peakRawRatio = 1f;
            remainingWarmupFrames = warmupFrameCount;
            smoothedDistance = currentDistance;
            gestureStartDistance = currentDistance;
            ZoomScaleRatio = 1f;
        }

        private void TryBeginPrimed(float currentDistance)
        {
            IsTracking = false;
            ZoomScaleRatio = 1f;

            if (currentDistance > pinchStartThreshold)
            {
                readyFrameCount = 0;
                return;
            }

            readyFrameCount++;
            if (readyFrameCount >= pinchReadyFrameCount)
                BeginPrimed(currentDistance);
        }

        private void BeginZoomSession()
        {
            state = GestureState.Zooming;
            IsTracking = true;
            repinchFrameCount = 0;
            canRebaselineAfterExpansion = false;
            peakRawRatio = 1f;
            GestureSessionId++;
        }

        private bool TryApplyClutchRebaseline(ref float rawRatio)
        {
            if (rawRatio > 1f + activationRatioThreshold)
            {
                canRebaselineAfterExpansion = true;
                peakRawRatio = Mathf.Max(peakRawRatio, rawRatio);
            }

            if (!canRebaselineAfterExpansion || smoothedDistance > repinchThreshold)
            {
                repinchFrameCount = 0;
                if (canRebaselineAfterExpansion)
                    rawRatio = Mathf.Max(rawRatio, peakRawRatio);

                return false;
            }

            repinchFrameCount++;
            if (repinchFrameCount < repinchReadyFrameCount)
            {
                rawRatio = Mathf.Max(rawRatio, peakRawRatio);
                return false;
            }

            BeginPrimed(smoothedDistance);
            return true;
        }

        private void HandleTrackingLost()
        {
            if (state != GestureState.Zooming)
                readyFrameCount = 0;

            missingFrameCount++;

            if (missingFrameCount <= trackingLossGraceFrames)
            {
                IsTracking = state == GestureState.Zooming;
                return;
            }

            state = GestureState.Idle;
            IsTracking = false;
            ZoomScaleRatio = 1f;
            readyFrameCount = 0;
            repinchFrameCount = 0;
            canRebaselineAfterExpansion = false;
            peakRawRatio = 1f;
            remainingWarmupFrames = warmupFrameCount;
        }
    }
}
