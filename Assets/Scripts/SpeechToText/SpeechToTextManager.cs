using System.Collections.Concurrent;
using TMPro;
using UnityEngine;

public class SpeechToTextManager : MonoBehaviour
{
    public enum SpeechProviderType
    {
        WindowsNative,
        WhisperServer,
        PlatformDefault,
        Disabled
    }

    public TextMeshProUGUI headLabelText;
    public SttStreamReceiver sttStreamReceiver;

    [Header("Provider")]
    public SpeechProviderType providerType = SpeechProviderType.PlatformDefault;
    public string whisperServerUrl = "ws://127.0.0.1:8000/ws/stt";

    [Header("Subtitle Display")]
    public float visibleSeconds = 3f;
    public float fadeSeconds = 1f;
    public int maxCharacters = 90;

    private readonly ConcurrentQueue<SpeechTextEvent> pendingTextEvents = new ConcurrentQueue<SpeechTextEvent>();
    private ISpeechToTextProvider provider;
    private ISpeechToTextTickProvider tickProvider;
    private float lastTextTime = -999f;
    private Color originalColor = Color.white;

    private void Awake()
    {
        AutoBind();
    }

    private void Start()
    {
        AutoBind();

        if (headLabelText != null)
            originalColor = headLabelText.color;

        provider = CreateProvider();
        Debug.Log($"[STTAudit] provider selected: {providerType}, providerInstance={(provider != null ? provider.GetType().Name : "null")}", this);

        if (provider == null)
            return;

        tickProvider = provider as ISpeechToTextTickProvider;
        provider.OnPartialText += QueuePartialText;
        provider.OnFinalText += QueueFinalText;
        provider.StartListening();
        Debug.Log($"[STTAudit] STT started: provider={provider.GetType().Name}, streamReceiver={(sttStreamReceiver != null ? sttStreamReceiver.name : "null")}", this);
    }

    private void Update()
    {
        tickProvider?.Tick();
        FlushPendingTextEvents();

        if (sttStreamReceiver != null)
            return;

        if (headLabelText == null)
            return;

        float age = Time.time - lastTextTime;

        if (age <= visibleSeconds)
            SetAlpha(1f);
        else if (age <= visibleSeconds + fadeSeconds)
            SetAlpha(1f - ((age - visibleSeconds) / Mathf.Max(0.01f, fadeSeconds)));
        else
            SetAlpha(0f);
    }

    private ISpeechToTextProvider CreateProvider()
    {
        switch (providerType)
        {
            case SpeechProviderType.Disabled:
                return null;

            case SpeechProviderType.WindowsNative:
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                return new WindowsDictationProvider();
#else
                Debug.LogWarning("Windows native speech-to-text is only available on Windows desktop/editor.");
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
                Debug.LogWarning("No default speech-to-text provider is available for this platform.");
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
        while (pendingTextEvents.TryDequeue(out SpeechTextEvent textEvent))
        {
            if (textEvent.IsFinal)
                HandleFinalText(textEvent.Text);
            else
                HandlePartialText(textEvent.Text);
        }
    }

    private void HandlePartialText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (sttStreamReceiver != null)
        {
            Debug.Log($"[STTAudit] partial transcript -> SttStreamReceiver: {text}", this);
            sttStreamReceiver.OnPartialTextReceived(text);
            return;
        }

        Debug.Log($"[STTAudit] partial transcript -> direct subtitle TMP: {text}", this);
        SetSubtitle(text);
    }

    private void HandleFinalText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        Debug.Log($"[STTAudit] final transcript received: {text}", this);

        if (sttStreamReceiver != null)
        {
            Debug.Log($"[STTAudit] final transcript -> SttStreamReceiver: {text}", this);
            sttStreamReceiver.OnFinalTextReceived(text);
            return;
        }

        Debug.Log($"[STTAudit] final transcript -> direct subtitle TMP: {text}", this);
        SetSubtitle(text);
    }

    private void SetSubtitle(string text)
    {
        if (headLabelText == null || string.IsNullOrWhiteSpace(text))
            return;

        text = text.Trim();

        if (text.Length > maxCharacters)
            text = text.Substring(text.Length - maxCharacters);

        headLabelText.text = text;
        lastTextTime = Time.time;
        SetAlpha(1f);
        Debug.Log($"[SubtitleAudit] direct TMP updated: object={headLabelText.name}, text={text}", this);
    }

    private void SetAlpha(float alpha)
    {
        if (headLabelText == null)
            return;

        Color color = originalColor;
        color.a = Mathf.Clamp01(alpha);
        headLabelText.color = color;
    }

    private void AutoBind()
    {
        if (headLabelText != null)
            return;

        TextMeshProUGUI[] texts = FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (TextMeshProUGUI text in texts)
        {
            if (text.name.Contains("HeadLabel") || text.transform.parent != null && text.transform.parent.name.Contains("HeadLabel"))
            {
                headLabelText = text;
                return;
            }
        }
    }

    private void OnDestroy()
    {
        if (provider == null)
            return;

        provider.OnPartialText -= QueuePartialText;
        provider.OnFinalText -= QueueFinalText;
        provider.StopListening();
        Debug.Log($"[STTAudit] STT stopped: provider={provider.GetType().Name}", this);
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
