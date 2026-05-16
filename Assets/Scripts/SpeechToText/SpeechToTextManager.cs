using TMPro;
using UnityEngine;
using System.Collections.Concurrent;

public class SpeechToTextManager : MonoBehaviour
{
    public enum SpeechProviderType
    {
        WindowsNative,
        WhisperServer,
        PlatformDefault
    }

    public TextMeshProUGUI headLabelText;

    [Header("Provider")]
    public SpeechProviderType providerType = SpeechProviderType.WindowsNative;
    public string whisperServerUrl = "ws://127.0.0.1:8000/ws/stt";

    [Header("Subtitle Display")]
    public float visibleSeconds = 3f;
    public float fadeSeconds = 1f;
    public int maxCharacters = 90;

    private ISpeechToTextProvider provider;
    private ISpeechToTextTickProvider tickProvider;
    private readonly ConcurrentQueue<SpeechTextEvent> pendingTextEvents = new ConcurrentQueue<SpeechTextEvent>();

    private float lastTextTime;
    private Color originalColor;

    void Start()
    {
        if (headLabelText != null)
            originalColor = headLabelText.color;

        provider = CreateProvider();

        if (provider == null)
            return;

        tickProvider = provider as ISpeechToTextTickProvider;
        provider.OnPartialText += QueuePartialText;
        provider.OnFinalText += QueueFinalText;

        provider.StartListening();
    }

    void Update()
    {
        tickProvider?.Tick();
        FlushPendingTextEvents();

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

    private ISpeechToTextProvider CreateProvider()
    {
        switch (providerType)
        {
            case SpeechProviderType.WindowsNative:
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                return new WindowsDictationProvider();
#else
                Debug.LogWarning("Windows native STT is only available on Windows desktop/editor.");
                return null;
#endif

            case SpeechProviderType.WhisperServer:
                return new WhisperServerSpeechProvider(whisperServerUrl);

            case SpeechProviderType.PlatformDefault:
#if UNITY_ANDROID && !UNITY_EDITOR
                return new AndroidSpeechProvider();
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                return new WindowsDictationProvider();
#else
                Debug.LogWarning("No default speech provider available for this platform.");
                return null;
#endif

            default:
                Debug.LogWarning($"Unknown speech provider: {providerType}");
                return null;
        }
    }

    private void QueuePartialText(string text)
    {
        pendingTextEvents.Enqueue(new SpeechTextEvent(text, false));
    }

    private void QueueFinalText(string text)
    {
        pendingTextEvents.Enqueue(new SpeechTextEvent(text, true));
    }

    private void FlushPendingTextEvents()
    {
        while (pendingTextEvents.TryDequeue(out var textEvent))
        {
            if (textEvent.IsFinal)
                HandleFinalText(textEvent.Text);
            else
                HandlePartialText(textEvent.Text);
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
            provider.OnPartialText -= QueuePartialText;
            provider.OnFinalText -= QueueFinalText;
            provider.StopListening();
        }
    }

    private readonly struct SpeechTextEvent
    {
        public readonly string Text;
        public readonly bool IsFinal;

        public SpeechTextEvent(string text, bool isFinal)
        {
            Text = text;
            IsFinal = isFinal;
        }
    }
}
