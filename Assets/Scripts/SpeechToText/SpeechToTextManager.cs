using TMPro;
using UnityEngine;

public class SpeechToTextManager : MonoBehaviour
{
    public TextMeshProUGUI headLabelText;

    [Header("Subtitle Display")]
    public float visibleSeconds = 3f;
    public float fadeSeconds = 1f;
    public int maxCharacters = 90;

    private ISpeechToTextProvider provider;

    private float lastTextTime;
    private Color originalColor;

    void Start()
    {
        #if UNITY_ANDROID && !UNITY_EDITOR
            provider = new AndroidSpeechProvider();
        #elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            provider = new WindowsDictationProvider();
        #else
            Debug.LogWarning("No speech provider available for this platform.");
            return;
        #endif

        if (provider == null)
            return;

        provider.OnPartialText += HandlePartialText;
        provider.OnFinalText += HandleFinalText;

        provider.StartListening();
    }

    void Update()
    {
        if (headLabelText == null)
            return;

        float age = Time.time - lastTextTime;

        if (age <= visibleSeconds)
        {
            SetAlpha(1f);
        }
        else if (age <= visibleSeconds + fadeSeconds)
        {
            float t = (age - visibleSeconds) / fadeSeconds;
            SetAlpha(1f - t);
        }
        else
        {
            SetAlpha(0f);
        }
    }

    private void HandlePartialText(string text)
    {
        SetSubtitle(text);
    }

    private void HandleFinalText(string text)
    {
        Debug.Log($"STT Final: {text}");
        SetSubtitle(text);
    }

    private void SetSubtitle(string text)
    {
        if (headLabelText == null)
            return;

        if (text.Length > maxCharacters)
            text = text.Substring(text.Length - maxCharacters);

        headLabelText.text = text;

        lastTextTime = Time.time;
        SetAlpha(1f);
    }

    private void SetAlpha(float alpha)
    {
        Color c = originalColor;
        c.a = alpha;
        headLabelText.color = c;
    }

    void OnDestroy()
    {
        if (provider != null)
        {
            provider.StopListening();
        }
    }
}