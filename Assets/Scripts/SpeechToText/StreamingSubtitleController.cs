using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StreamingSubtitleController : MonoBehaviour
{
    [Header("References")]
    public RectTransform subtitleContent;
    public TextMeshProUGUI wordTextTemplate;
    public CanvasGroup subtitleCanvasGroup;
    public Image subtitleBackground;

    [Header("Subtitle Settings")]
    public int maxVisibleLines = 10;
    public bool clearTextAfterFadeOut = true;

    [Header("Animation Settings")]
    public float boxFadeDuration = 0.25f;
    public float hideDelay = 3.5f;

    [Header("Background Settings")]
    [Range(0f, 1f)]
    public float backgroundAlpha = 0.45f;

    private Coroutine hideCoroutine;
    private Coroutine boxFadeCoroutine;

    private string currentDisplayedText = "";
    private string lastSubtitleAuditState;

    private void Start()
    {
        SetupText();
        SetupBackground();
        SetupInitialVisibility();
    }

    public void ReceivePartialText(string text)
    {
        Debug.Log($"[SubtitleAudit] partial input: {text}", this);
        ReceiveText(text);
    }

    public void ReceiveFinalText(string text)
    {
        Debug.Log($"[SubtitleAudit] final input: {text}", this);
        ReceiveText(text);
    }

    // 기존 토큰 방식과의 호환용
    public void ReceiveToken(string token)
    {
        if (string.IsNullOrEmpty(token))
            return;

        if (token == "\\n" || token == "\n")
            return;

        ReceiveText(token);
    }

    private void ReceiveText(string text)
    {
        string displayText = FormatTextForDisplay(text);

        if (string.IsNullOrWhiteSpace(displayText))
            return;

        ShowSubtitle();
        SetSubtitleText(displayText);
    }

    private void SetSubtitleText(string text)
    {
        if (wordTextTemplate == null)
            return;

        currentDisplayedText = text;
        wordTextTemplate.text = text;
        LogSubtitleUpdate(text);
    }

    private string FormatTextForDisplay(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        return text.Trim();
    }

    private void ShowSubtitle()
    {
        if (hideCoroutine != null)
        {
            StopCoroutine(hideCoroutine);
            hideCoroutine = null;
        }

        StartBoxFade(1f, false);
        hideCoroutine = StartCoroutine(HideAfterDelay());
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(hideDelay);
        StartBoxFade(0f, clearTextAfterFadeOut);
    }

    private void StartBoxFade(float targetAlpha, bool clearAfterFade)
    {
        if (subtitleCanvasGroup == null)
            return;

        if (boxFadeCoroutine != null)
        {
            StopCoroutine(boxFadeCoroutine);
        }

        boxFadeCoroutine = StartCoroutine(FadeCanvasGroup(
            subtitleCanvasGroup,
            targetAlpha,
            boxFadeDuration,
            clearAfterFade
        ));
    }

    private IEnumerator FadeCanvasGroup(
        CanvasGroup canvasGroup,
        float targetAlpha,
        float duration,
        bool clearAfterFade
    )
    {
        if (canvasGroup == null)
            yield break;

        float startAlpha = canvasGroup.alpha;
        float elapsed = 0f;
        float safeDuration = Mathf.Max(0.01f, duration);

        while (elapsed < safeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / safeDuration);

            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);

            yield return null;
        }

        canvasGroup.alpha = targetAlpha;
        boxFadeCoroutine = null;

        if (clearAfterFade)
            ClearSubtitle();
    }

    private void SetupText()
    {
        if (wordTextTemplate == null)
        {
            Debug.LogWarning("wordTextTemplate이 연결되지 않았습니다.");
            return;
        }

        wordTextTemplate.gameObject.SetActive(true);
        wordTextTemplate.text = "";

        wordTextTemplate.enableWordWrapping = true;

        wordTextTemplate.overflowMode = TextOverflowModes.Overflow;

        wordTextTemplate.maxVisibleLines = Mathf.Max(1, maxVisibleLines);
        wordTextTemplate.raycastTarget = false;

        // 예전 단어 생성 방식에서 남은 자식 오브젝트가 있다면 정리한다.
        if (subtitleContent != null)
        {
            for (int i = subtitleContent.childCount - 1; i >= 0; i--)
            {
                Transform child = subtitleContent.GetChild(i);

                if (child != wordTextTemplate.transform)
                    Destroy(child.gameObject);
            }
        }
    }

    private void SetupBackground()
    {
        if (subtitleBackground == null)
            return;

        Color color = subtitleBackground.color;
        color.a = backgroundAlpha;
        subtitleBackground.color = color;
    }

    private void SetupInitialVisibility()
    {
        if (subtitleCanvasGroup != null)
        {
            subtitleCanvasGroup.alpha = 0f;
            subtitleCanvasGroup.interactable = false;
            subtitleCanvasGroup.blocksRaycasts = false;
        }

        ClearSubtitle();
    }

    private void ClearSubtitle()
    {
        currentDisplayedText = "";

        if (wordTextTemplate != null)
            wordTextTemplate.text = "";

        LogSubtitleUpdate("");
    }

    private void LogSubtitleUpdate(string text)
    {
        string targetName = wordTextTemplate != null ? wordTextTemplate.name : "null";
        string state = $"target={targetName}, text={text}";

        if (state == lastSubtitleAuditState)
            return;

        lastSubtitleAuditState = state;
        Debug.Log($"[SubtitleAudit] subtitle UI updated: {state}", this);
    }
}
