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

        public bool TryGetHandFrame(out HandFrame frame)
        {
            frame = new HandFrame(
                isTracked,
                thumbTip,
                indexTip,
                Time.time
            );

            return isTracked;
        }

        public void SetHandFrame(bool tracked, Vector2 thumbTipPosition, Vector2 indexTipPosition)
        {
            isTracked = tracked;
            thumbTip = thumbTipPosition;
            indexTip = indexTipPosition;
        }

        public void ClearHandFrame()
        {
            isTracked = false;
            thumbTip = Vector2.zero;
            indexTip = Vector2.zero;
        }
    }
}
