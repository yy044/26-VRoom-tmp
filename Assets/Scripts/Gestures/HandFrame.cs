using UnityEngine;

namespace VRoom.Gestures
{
    public struct HandFrame
    {
        public bool isTracked;
        public Vector2 thumbTip;
        public Vector2 indexTip;
        public float timestamp;

        public HandFrame(bool isTracked, Vector2 thumbTip, Vector2 indexTip, float timestamp)
        {
            this.isTracked = isTracked;
            this.thumbTip = thumbTip;
            this.indexTip = indexTip;
            this.timestamp = timestamp;
        }
    }
}
