#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

using System;
using UnityEngine;
using UnityEngine.Windows.Speech;

public class WindowsDictationProvider : ISpeechToTextProvider
{
    public event Action<string> OnPartialText;
    public event Action<string> OnFinalText;

    private DictationRecognizer recognizer;

    public void StartListening()
    {
        if (recognizer != null)
            return;

        recognizer = new DictationRecognizer();

        recognizer.DictationHypothesis += HandleHypothesis;
        recognizer.DictationResult += HandleResult;
        recognizer.DictationError += HandleError;
        recognizer.DictationComplete += HandleComplete;

        recognizer.Start();

        Debug.Log("Windows dictation started");
    }

    public void StopListening()
    {
        if (recognizer == null)
            return;

        if (recognizer.Status == SpeechSystemStatus.Running)
            recognizer.Stop();

        recognizer.DictationHypothesis -= HandleHypothesis;
        recognizer.DictationResult -= HandleResult;
        recognizer.DictationError -= HandleError;
        recognizer.DictationComplete -= HandleComplete;

        recognizer.Dispose();
        recognizer = null;

        Debug.Log("Windows dictation stopped");
    }

    private void HandleHypothesis(string text)
    {
        OnPartialText?.Invoke(text);
    }

    private void HandleResult(string text, ConfidenceLevel confidence)
    {
        OnFinalText?.Invoke(text);
    }

    private void HandleError(string error, int hresult)
    {
        Debug.LogError($"Dictation error: {error} ({hresult})");
    }

    private void HandleComplete(DictationCompletionCause cause)
    {
        Debug.Log($"Dictation completed: {cause}");
    }
}

#endif