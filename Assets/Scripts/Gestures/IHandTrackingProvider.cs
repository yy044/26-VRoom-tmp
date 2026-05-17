namespace VRoom.Gestures
{
    public interface IHandTrackingProvider
    {
        bool TryGetHandFrame(out HandFrame frame);
    }
}
