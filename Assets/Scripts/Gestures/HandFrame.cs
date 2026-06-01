using UnityEngine;

namespace VRoom.Gestures
{
    public struct HandFrame
    {
        public bool isTracked;
        public Vector2 thumbTip;
        public Vector2 indexTip;
        public float timestamp;
        public float handScale;
        public Vector2 pinchCenter;

        public HandFrame(bool isTracked, Vector2 thumbTip, Vector2 indexTip, float timestamp, float handScale = 1f)
        {
            this.isTracked = isTracked;
            this.thumbTip = thumbTip;
            this.indexTip = indexTip;
            this.timestamp = timestamp;
            this.handScale = Mathf.Max(0.0001f, handScale);
            this.pinchCenter = (thumbTip + indexTip) * 0.5f;
        }
    }
}
