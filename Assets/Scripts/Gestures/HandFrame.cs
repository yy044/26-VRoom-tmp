using UnityEngine;

namespace VRoom.Gestures
{
    public struct HandFrame
    {
        public bool isTracked;
        public Vector2 thumbTip;
        public Vector2 indexTip;
        public Vector2 pinchCenter;
        public float handScale;
        public float timestamp;

        public HandFrame(bool isTracked, Vector2 thumbTip, Vector2 indexTip, float timestamp)
        {
            this.isTracked = isTracked;
            this.thumbTip = thumbTip;
            this.indexTip = indexTip;
            this.pinchCenter = (thumbTip + indexTip) * 0.5f;
            this.handScale = 1f;
            this.timestamp = timestamp;
        }

        public HandFrame(bool isTracked, Vector2 thumbTip, Vector2 indexTip, Vector2 pinchCenter, float timestamp)
        {
            this.isTracked = isTracked;
            this.thumbTip = thumbTip;
            this.indexTip = indexTip;
            this.pinchCenter = pinchCenter;
            this.handScale = 1f;
            this.timestamp = timestamp;
        }

        public HandFrame(bool isTracked, Vector2 thumbTip, Vector2 indexTip, Vector2 pinchCenter, float handScale, float timestamp)
        {
            this.isTracked = isTracked;
            this.thumbTip = thumbTip;
            this.indexTip = indexTip;
            this.pinchCenter = pinchCenter;
            this.handScale = handScale;
            this.timestamp = timestamp;
        }
    }
}
