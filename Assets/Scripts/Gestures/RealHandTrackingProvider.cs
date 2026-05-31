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
                (thumbTip + indexTip) * 0.5f,
                handScale,
                Time.time
            );

            return isTracked;
        }

        public void SetHandFrame(bool tracked, Vector2 thumbTipPosition, Vector2 indexTipPosition)
        {
            isTracked = tracked;
            thumbTip = thumbTipPosition;
            indexTip = indexTipPosition;
            handScale = 1f;
        }

        public void SetHandFrame(bool tracked, Vector2 thumbTipPosition, Vector2 indexTipPosition, float handScaleValue)
        {
            isTracked = tracked;
            thumbTip = thumbTipPosition;
            indexTip = indexTipPosition;
            handScale = handScaleValue;
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
