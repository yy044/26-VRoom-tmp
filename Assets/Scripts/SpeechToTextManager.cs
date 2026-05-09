using TMPro;
using UnityEngine;
using UnityEngine.Windows.Speech;

public class SpeechToTextManager : MonoBehaviour
{
    public TextMeshProUGUI headLabelText;

    [Header("Subtitle Display")]
    public float visibleSeconds = 3f;
    public float fadeSeconds = 1f;
    public int maxCharacters = 90;

    private DictationRecognizer dictationRecognizer;
    private float lastTextTime;
    private Color originalColor;

    void Start()
    {
        if (headLabelText != null)
            originalColor = headLabelText.color;

        dictationRecognizer = new DictationRecognizer();

        dictationRecognizer.DictationHypothesis += (text) =>
        {
            SetSubtitle(text);
        };

        dictationRecognizer.DictationResult += (text, confidence) =>
        {
            Debug.Log($"STT Final: {text}");
            SetSubtitle(text);
        };

        dictationRecognizer.DictationError += (error, hresult) =>
        {
            Debug.LogError($"Dictation error: {error}");
        };

        dictationRecognizer.Start();
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

    void SetSubtitle(string text)
    {
        if (headLabelText == null)
            return;

        if (text.Length > maxCharacters)
            text = text.Substring(text.Length - maxCharacters);

        headLabelText.text = text;
        lastTextTime = Time.time;
        SetAlpha(1f);
    }

    void SetAlpha(float alpha)
    {
        Color c = originalColor;
        c.a = alpha;
        headLabelText.color = c;
    }

    void OnDestroy()
    {
        if (dictationRecognizer != null)
        {
            if (dictationRecognizer.Status == SpeechSystemStatus.Running)
                dictationRecognizer.Stop();

            dictationRecognizer.Dispose();
        }
    }
}