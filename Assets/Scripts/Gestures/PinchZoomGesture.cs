using UnityEngine;

namespace VRoom.Gestures
{
    public class PinchZoomGesture : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField]
        private MonoBehaviour handTrackingProviderBehaviour;

        [Header("Gesture Settings")]
        [SerializeField]
        private float noiseThreshold = 0.015f;

        [SerializeField]
        private float zoomSensitivity = 16f;

        [SerializeField]
        [Range(0.01f, 1f)]
        private float smoothing = 0.9f;

        [SerializeField]
        private float outputDeadZone = 0.01f;

        private IHandTrackingProvider handTrackingProvider;
        private bool hasPreviousDistance;
        private float previousDistance;
        private float smoothedZoomDelta;

        public float ZoomDelta { get; private set; }
        public bool IsTracking { get; private set; }

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

            if (!handTrackingProvider.TryGetHandFrame(out var frame) || !frame.isTracked)
            {
                IsTracking = false;
                hasPreviousDistance = false;
                smoothedZoomDelta = 0f;
                return;
            }

            IsTracking = true;

            float currentDistance = Vector2.Distance(frame.thumbTip, frame.indexTip);

            if (!hasPreviousDistance)
            {
                previousDistance = currentDistance;
                hasPreviousDistance = true;
                return;
            }

            float rawDelta = currentDistance - previousDistance;
            previousDistance = currentDistance;

            if (Mathf.Abs(rawDelta) < noiseThreshold)
                rawDelta = 0f;

            float targetZoomDelta = rawDelta * zoomSensitivity;
            smoothedZoomDelta = Mathf.Lerp(smoothedZoomDelta, targetZoomDelta, smoothing);

            ZoomDelta = Mathf.Abs(smoothedZoomDelta) < outputDeadZone ? 0f : smoothedZoomDelta;
        }
    }
}
