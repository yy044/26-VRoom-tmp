using System;
using UnityEngine;

public class AndroidSpeechProvider : ISpeechToTextProvider
{
    public event Action<string> OnPartialText;
    public event Action<string> OnFinalText;

    private AndroidJavaObject speechRecognizer;
    private AndroidJavaObject activity;

    public void StartListening()
    {
#if UNITY_ANDROID && !UNITY_EDITOR

        activity =
            new AndroidJavaClass("com.unity3d.player.UnityPlayer")
            .GetStatic<AndroidJavaObject>("currentActivity");

        Debug.Log("AndroidSpeechProvider placeholder started");

        // TEMP PLACEHOLDER
        // Real Android SpeechRecognizer bridge comes next

        OnFinalText?.Invoke("Android STT placeholder");

#endif
    }

    public void StopListening()
    {
    }
}