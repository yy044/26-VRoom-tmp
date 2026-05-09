using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using TMPro;

public class ARCameraConfigSwitcher : MonoBehaviour
{
    [Header("AR")]
    public ARCameraManager cameraManager;

    [Header("UI")]
    public TMP_Text statusText;

    private XRCameraConfiguration[] configs;
    private int currentIndex = 0;

    IEnumerator Start()
    {
        while (cameraManager == null ||
               cameraManager.subsystem == null ||
               !cameraManager.subsystem.running)
        {
            SetStatus("Waiting for AR camera...");
            yield return null;
        }

        LoadConfigurations();
        ShowCurrentConfig();
    }

    void LoadConfigurations()
    {
        using (var availableConfigs = cameraManager.GetConfigurations(Allocator.Temp))
        {
            if (!availableConfigs.IsCreated || availableConfigs.Length == 0)
            {
                SetStatus("No AR camera configs found.");
                configs = new XRCameraConfiguration[0];
                return;
            }

            configs = new XRCameraConfiguration[availableConfigs.Length];

            for (int i = 0; i < availableConfigs.Length; i++)
            {
                configs[i] = availableConfigs[i];

                int fps = configs[i].framerate.HasValue
                    ? configs[i].framerate.Value
                    : 0;

                Debug.Log($"Config [{i}]: {configs[i].width}x{configs[i].height} @ {fps}fps");
            }
        }
    }

    public void NextCameraConfig()
    {
        if (configs == null || configs.Length == 0)
        {
            SetStatus("No configs available.");
            return;
        }

        currentIndex++;

        if (currentIndex >= configs.Length)
            currentIndex = 0;

        cameraManager.currentConfiguration = configs[currentIndex];

        StartCoroutine(RefreshStatusAfterSwitch());
    }

    IEnumerator RefreshStatusAfterSwitch()
    {
        SetStatus("Switching config...");
        yield return new WaitForSeconds(0.5f);
        ShowCurrentConfig();
    }

    void ShowCurrentConfig()
    {
        if (!cameraManager.currentConfiguration.HasValue)
        {
            SetStatus("No current config.");
            return;
        }

        var config = cameraManager.currentConfiguration.Value;

        int fps = config.framerate.HasValue
            ? config.framerate.Value
            : 0;

        string msg =
            $"Config {currentIndex}\n" +
            $"{config.width}x{config.height} @ {fps}fps";

        if (cameraManager.TryGetIntrinsics(out XRCameraIntrinsics intrinsics))
        {
            msg +=
                $"\nFocal: {intrinsics.focalLength.x:F1}, {intrinsics.focalLength.y:F1}";
        }

        SetStatus(msg);
    }

    void SetStatus(string msg)
    {
        Debug.Log(msg);

        if (statusText != null)
            statusText.text = msg;
    }
}