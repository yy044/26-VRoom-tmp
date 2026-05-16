using System.Collections;
using TMPro;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ARCameraConfigSwitcher : MonoBehaviour
{
    [Header("AR")]
    public ARCameraManager cameraManager;

    [Header("UI")]
    public TMP_Text statusText;

    private XRCameraConfiguration[] configs = new XRCameraConfiguration[0];
    private int currentIndex;

    private IEnumerator Start()
    {
        AutoBind();

        while (cameraManager == null || cameraManager.subsystem == null || !cameraManager.subsystem.running)
        {
            SetStatus("Waiting for AR camera...");
            yield return null;
            AutoBind();
        }

        LoadConfigurations();
        SnapIndexToCurrentConfig();
        ShowCurrentConfig();
    }

    public void NextCameraConfig()
    {
        if (cameraManager == null)
            AutoBind();

        if (cameraManager == null)
        {
            SetStatus("No ARCameraManager found.");
            return;
        }

        if (configs == null || configs.Length == 0)
            LoadConfigurations();

        if (configs == null || configs.Length == 0)
        {
            SetStatus("No camera configs available.");
            return;
        }

        currentIndex = (currentIndex + 1) % configs.Length;
        cameraManager.currentConfiguration = configs[currentIndex];
        StartCoroutine(RefreshStatusAfterSwitch());
    }

    private void LoadConfigurations()
    {
        if (cameraManager == null || cameraManager.subsystem == null)
        {
            configs = new XRCameraConfiguration[0];
            return;
        }

        using (NativeArray<XRCameraConfiguration> availableConfigs = cameraManager.GetConfigurations(Allocator.Temp))
        {
            if (!availableConfigs.IsCreated || availableConfigs.Length == 0)
            {
                configs = new XRCameraConfiguration[0];
                SetStatus("No AR camera configs found.");
                return;
            }

            configs = new XRCameraConfiguration[availableConfigs.Length];

            for (int i = 0; i < availableConfigs.Length; i++)
            {
                configs[i] = availableConfigs[i];
                Debug.Log($"AR config [{i}]: {Describe(configs[i])}");
            }
        }
    }

    private IEnumerator RefreshStatusAfterSwitch()
    {
        SetStatus("Switching camera config...");
        yield return new WaitForSeconds(0.35f);
        ShowCurrentConfig();
    }

    private void ShowCurrentConfig()
    {
        if (cameraManager == null)
        {
            SetStatus("No ARCameraManager found.");
            return;
        }

        if (!cameraManager.currentConfiguration.HasValue)
        {
            SetStatus("No current AR camera config.");
            return;
        }

        XRCameraConfiguration config = cameraManager.currentConfiguration.Value;
        string message = $"Config {currentIndex + 1}/{Mathf.Max(1, configs.Length)}\n{Describe(config)}";

        if (cameraManager.TryGetIntrinsics(out XRCameraIntrinsics intrinsics))
            message += $"\nFocal: {intrinsics.focalLength.x:F1}, {intrinsics.focalLength.y:F1}";

        SetStatus(message);
    }

    private void SnapIndexToCurrentConfig()
    {
        if (cameraManager == null || !cameraManager.currentConfiguration.HasValue || configs == null)
            return;

        XRCameraConfiguration current = cameraManager.currentConfiguration.Value;

        for (int i = 0; i < configs.Length; i++)
        {
            if (configs[i].Equals(current))
            {
                currentIndex = i;
                return;
            }
        }
    }

    private void AutoBind()
    {
        if (cameraManager == null)
            cameraManager = FindFirstObjectByType<ARCameraManager>(FindObjectsInactive.Include);

        if (statusText == null)
        {
            TMP_Text[] texts = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (TMP_Text text in texts)
            {
                if (text.name.Contains("Debug") || text.name.Contains("Status"))
                {
                    statusText = text;
                    break;
                }
            }
        }
    }

    private static string Describe(XRCameraConfiguration config)
    {
        int fps = config.framerate.HasValue ? config.framerate.Value : 0;
        return fps > 0 ? $"{config.width}x{config.height} @ {fps}fps" : $"{config.width}x{config.height}";
    }

    private void SetStatus(string message)
    {
        Debug.Log(message);

        if (statusText != null)
            statusText.text = message;
    }
}
