using UnityEngine;

public class MobileOrientationController : MonoBehaviour
{
    [Header("Mode Orientation")]
    [SerializeField] private ScreenOrientation frontCameraOrientation = ScreenOrientation.Portrait;
    [SerializeField] private ScreenOrientation backCameraOrientation = ScreenOrientation.LandscapeLeft;

    private string lastAppliedState;

    public ScreenOrientation AppliedOrientation { get; private set; } = ScreenOrientation.Portrait;

    public void ApplyForMode(bool isFrontCamera)
    {
        ApplyForCameraMode(!isFrontCamera);
    }

    private void ApplyForCameraMode(bool backMode)
    {
        ScreenOrientation targetOrientation = backMode ? backCameraOrientation : frontCameraOrientation;

        Screen.autorotateToPortrait = !backMode && targetOrientation == ScreenOrientation.AutoRotation;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = backMode && targetOrientation == ScreenOrientation.AutoRotation;
        Screen.autorotateToLandscapeRight = false;
        Screen.orientation = targetOrientation;
        AppliedOrientation = targetOrientation;

        string modeLabel = backMode ? "Back" : "Front";
        string state = $"mode={modeLabel} orientation={targetOrientation}";
        if (state == lastAppliedState)
            return;

        lastAppliedState = state;
        Debug.Log($"[MobileOrientation] Applied {state}", this);
    }
}
