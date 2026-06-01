using UnityEngine;

namespace VRoom.Gestures
{
    public class RealHandTrackingProvider : MonoBehaviour, IHandTrackingProvider
    {
        [Header("Current Hand Frame")]
        [SerializeField]
        private bool isTracked;

        [SerializeField]
        private Vector2 thumbTip;

        [SerializeField]
        private Vector2 indexTip;

        [SerializeField]
        private float handScale = 1f;

        public bool TryGetHandFrame(out HandFrame frame)
        {
            frame = new HandFrame(
                isTracked,
                thumbTip,
                indexTip,
                Time.time,
                handScale
            );

            return isTracked;
        }

        public void SetHandFrame(bool tracked, Vector2 thumbTipPosition, Vector2 indexTipPosition, float handScale)
        {
            isTracked = tracked;
            thumbTip = thumbTipPosition;
            indexTip = indexTipPosition;
            this.handScale = Mathf.Max(0.0001f, handScale);
        }

        public void ClearHandFrame()
        {
            isTracked = false;
            thumbTip = Vector2.zero;
            indexTip = Vector2.zero;
            handScale = 1f;
        }
    }
}
