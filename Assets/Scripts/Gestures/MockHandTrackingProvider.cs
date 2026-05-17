using UnityEngine;
using UnityEngine.InputSystem;

namespace VRoom.Gestures
{
    public class MockHandTrackingProvider : MonoBehaviour, IHandTrackingProvider
    {
        [Header("Mock Hand")]
        [SerializeField]
        private bool isTracked = true;

        [SerializeField]
        [Range(0.02f, 0.5f)]
        private float pinchDistance = 0.08f;

        [SerializeField]
        private float distanceChangeStep = 0.006f;

        [SerializeField]
        private Vector2 pinchCenter = new Vector2(0.5f, 0.5f);

        public bool TryGetHandFrame(out HandFrame frame)
        {
            UpdateMockDistance();

            if (!isTracked)
            {
                frame = new HandFrame(false, Vector2.zero, Vector2.zero, Time.time);
                return false;
            }

            Vector2 halfOffset = new Vector2(pinchDistance * 0.5f, 0f);

            frame = new HandFrame(
                true,
                pinchCenter - halfOffset,
                pinchCenter + halfOffset,
                Time.time
            );

            return true;
        }

        private void UpdateMockDistance()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.upArrowKey.wasPressedThisFrame)
                pinchDistance += distanceChangeStep;

            if (keyboard.downArrowKey.wasPressedThisFrame)
                pinchDistance -= distanceChangeStep;

            if (keyboard.spaceKey.wasPressedThisFrame)
                isTracked = !isTracked;

            pinchDistance = Mathf.Clamp(pinchDistance, 0.02f, 0.5f);
        }
    }
}
