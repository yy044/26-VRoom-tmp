using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StreamingSubtitleController : MonoBehaviour
{
    public RectTransform subtitleContent;
    public TextMeshProUGUI wordTextTemplate;
    public CanvasGroup subtitleCanvasGroup;
    public Image subtitleBackground;

    [Header("Subtitle Settings")]
    public int maxVisibleLines = 2;

    [Header("Layout Settings")]
    public float lineHeight = 56f;
    public float wordSpacing = 8f;

    [Header("Animation Settings")]
    public float wordFadeDuration = 0.18f;
    public float lineMoveDuration = 0.35f;
    public float lineFadeDuration = 0.4f;
    public float boxFadeDuration = 0.35f;
    public float hideDelay = 3.5f;

    [Header("Background Settings")]
    [Range(0f, 1f)]
    public float backgroundAlpha = 0.45f;

    private readonly List<SubtitleLineView> lineViews = new List<SubtitleLineView>();

    private SubtitleLineView currentLineView;
    private string currentLine = "";

    private Coroutine hideCoroutine;
    private Coroutine boxFadeCoroutine;

    private class SubtitleLineView
    {
        public GameObject gameObject;
        public RectTransform rectTransform;
        public CanvasGroup canvasGroup;
        public Coroutine moveCoroutine;
        public float contentWidth;
    }

    void Start()
    {
        SetupContentRoot();
        SetupTemplate();
        SetupBackground();
        SetupInitialVisibility();
    }

    public void ReceiveToken(string token)
    {
        if (string.IsNullOrEmpty(token))
            return;

        ShowSubtitle();

        if (token == "\\n" || token == "\n")
        {
            CommitCurrentLine();
            return;
        }

        AppendWord(token.Trim());
    }

    private void AppendWord(string word)
    {
        if (string.IsNullOrEmpty(word))
            return;

        if (currentLineView == null)
        {
            StartNewLine();
        }

        float wordWidth = GetWordWidth(word);

        if (ShouldWrapBeforeWord(wordWidth))
        {
            CommitCurrentLine();
            StartNewLine();
        }

        AddWordToCurrentLine(word, wordWidth);

        if (string.IsNullOrEmpty(currentLine))
            currentLine = word;
        else
            currentLine += " " + word;
    }

    private bool ShouldWrapBeforeWord(float wordWidth)
    {
        if (currentLineView == null)
            return false;

        if (currentLineView.rectTransform.childCount == 0)
            return false;

        float availableWidth = GetAvailableLineWidth();

        if (availableWidth <= 0f)
            return false;

        float nextWidth = currentLineView.contentWidth + wordSpacing + wordWidth;

        return nextWidth > availableWidth;
    }

    private float GetWordWidth(string word)
    {
        if (wordTextTemplate == null)
            return 0f;

        Vector2 preferredSize = wordTextTemplate.GetPreferredValues(word);
        return preferredSize.x;
    }

    private float GetAvailableLineWidth()
    {
        if (subtitleContent == null)
            return 0f;

        float width = subtitleContent.rect.width;

        if (width <= 0f && wordTextTemplate != null)
        {
            width = wordTextTemplate.rectTransform.rect.width;
        }

        return width;
    }

    private void CommitCurrentLine()
    {
        if (currentLineView == null || string.IsNullOrWhiteSpace(currentLine))
            return;

        currentLineView = null;
        currentLine = "";

        RefreshLinePositions(true, null);
    }

    private void StartNewLine()
    {
        currentLineView = CreateLineView();

        Vector2 bottomPosition = GetLineTargetPosition(maxVisibleLines - 1);
        currentLineView.rectTransform.anchoredPosition = bottomPosition;

        lineViews.Add(currentLineView);

        TrimOverflowLines();

        RefreshLinePositions(true, currentLineView);
    }

    private SubtitleLineView CreateLineView()
    {
        GameObject lineObject = new GameObject(
            "SubtitleLine",
            typeof(RectTransform),
            typeof(CanvasGroup),
            typeof(HorizontalLayoutGroup)
        );

        lineObject.transform.SetParent(subtitleContent, false);

        RectTransform lineRect = lineObject.GetComponent<RectTransform>();
        lineRect.anchorMin = new Vector2(0f, 1f);
        lineRect.anchorMax = new Vector2(1f, 1f);
        lineRect.pivot = new Vector2(0.5f, 1f);
        lineRect.sizeDelta = new Vector2(0f, lineHeight);

        CanvasGroup lineCanvasGroup = lineObject.GetComponent<CanvasGroup>();
        lineCanvasGroup.alpha = 1f;

        HorizontalLayoutGroup layoutGroup = lineObject.GetComponent<HorizontalLayoutGroup>();
        layoutGroup.childAlignment = TextAnchor.MiddleCenter;
        layoutGroup.spacing = wordSpacing;
        layoutGroup.padding = new RectOffset(0, 0, 0, 0);
        layoutGroup.childControlWidth = true;
        layoutGroup.childControlHeight = true;
        layoutGroup.childForceExpandWidth = false;
        layoutGroup.childForceExpandHeight = false;

        return new SubtitleLineView
        {
            gameObject = lineObject,
            rectTransform = lineRect,
            canvasGroup = lineCanvasGroup,
            contentWidth = 0f
        };
    }

    private void AddWordToCurrentLine(string word, float wordWidth)
    {
        if (currentLineView == null || wordTextTemplate == null)
            return;

        GameObject wordObject = new GameObject(
            "Word",
            typeof(RectTransform),
            typeof(CanvasGroup),
            typeof(TextMeshProUGUI),
            typeof(LayoutElement)
        );

        wordObject.transform.SetParent(currentLineView.rectTransform, false);

        TextMeshProUGUI wordText = wordObject.GetComponent<TextMeshProUGUI>();
        CopyTextStyle(wordText);
        wordText.text = word;

        LayoutElement layoutElement = wordObject.GetComponent<LayoutElement>();
        layoutElement.preferredWidth = wordWidth;
        layoutElement.preferredHeight = lineHeight;

        CanvasGroup wordCanvasGroup = wordObject.GetComponent<CanvasGroup>();
        wordCanvasGroup.alpha = 0f;

        if (currentLineView.rectTransform.childCount == 1)
            currentLineView.contentWidth = wordWidth;
        else
            currentLineView.contentWidth += wordSpacing + wordWidth;

        LayoutRebuilder.ForceRebuildLayoutImmediate(currentLineView.rectTransform);
        StartCoroutine(FadeCanvasGroup(wordCanvasGroup, 1f, wordFadeDuration, false));
    }

    private void CopyTextStyle(TextMeshProUGUI target)
    {
        target.font = wordTextTemplate.font;
        target.fontSharedMaterial = wordTextTemplate.fontSharedMaterial;
        target.fontSize = wordTextTemplate.fontSize;
        target.fontStyle = wordTextTemplate.fontStyle;
        target.color = wordTextTemplate.color;
        target.alignment = TextAlignmentOptions.Midline;
        target.enableWordWrapping = false;
        target.overflowMode = TextOverflowModes.Overflow;
        target.raycastTarget = false;
        target.margin = Vector4.zero;
    }

    private void TrimOverflowLines()
    {
        while (lineViews.Count > maxVisibleLines)
        {
            SubtitleLineView oldLine = lineViews[0];
            lineViews.RemoveAt(0);

            if (oldLine == currentLineView)
            {
                currentLineView = null;
                currentLine = "";
            }

            StartCoroutine(FadeOutAndDestroyLine(oldLine));
        }
    }

    private void RefreshLinePositions(bool animate, SubtitleLineView instantLine)
    {
        for (int i = 0; i < lineViews.Count; i++)
        {
            Vector2 targetPosition = GetVisibleLinePosition(i, lineViews.Count);
            bool shouldAnimate = animate && lineViews[i] != instantLine;

            MoveLineTo(lineViews[i], targetPosition, shouldAnimate);
        }
    }

    private Vector2 GetVisibleLinePosition(int index, int visibleLineCount)
    {
        int startRow = maxVisibleLines - visibleLineCount;
        int row = startRow + index;

        return GetLineTargetPosition(row);
    }

    private Vector2 GetLineTargetPosition(int row)
    {
        return new Vector2(0f, -row * lineHeight);
    }

    private void MoveLineTo(SubtitleLineView lineView, Vector2 targetPosition, bool animate)
    {
        if (lineView == null || lineView.rectTransform == null)
            return;

        if (lineView.moveCoroutine != null)
        {
            StopCoroutine(lineView.moveCoroutine);
        }

        if (!animate)
        {
            lineView.rectTransform.anchoredPosition = targetPosition;
            return;
        }

        lineView.moveCoroutine = StartCoroutine(MoveLineRoutine(lineView, targetPosition));
    }

    private IEnumerator MoveLineRoutine(SubtitleLineView lineView, Vector2 targetPosition)
    {
        Vector2 startPosition = lineView.rectTransform.anchoredPosition;
        float elapsed = 0f;

        while (elapsed < lineMoveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / lineMoveDuration);
            float easedT = EaseOutCubic(t);

            lineView.rectTransform.anchoredPosition =
                Vector2.Lerp(startPosition, targetPosition, easedT);

            yield return null;
        }

        lineView.rectTransform.anchoredPosition = targetPosition;
        lineView.moveCoroutine = null;
    }

    private IEnumerator FadeOutAndDestroyLine(SubtitleLineView lineView)
    {
        if (lineView == null || lineView.rectTransform == null)
            yield break;

        Vector2 startPosition = lineView.rectTransform.anchoredPosition;
        Vector2 endPosition = startPosition + new Vector2(0f, lineHeight * 0.45f);

        float startAlpha = lineView.canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < lineFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / lineFadeDuration);
            float easedT = EaseOutCubic(t);

            lineView.canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);
            lineView.rectTransform.anchoredPosition =
                Vector2.Lerp(startPosition, endPosition, easedT);

            yield return null;
        }

        Destroy(lineView.gameObject);
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
        StartBoxFade(0f, true);
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

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            yield return null;
        }

        canvasGroup.alpha = targetAlpha;

        if (clearAfterFade)
        {
            ClearSubtitle();
        }
    }

    private void SetupContentRoot()
    {
        if (subtitleContent != null)
            return;

        if (wordTextTemplate == null)
        {
            Debug.LogWarning("wordTextTemplate이 연결되지 않았습니다.");
            return;
        }

        RectTransform templateRect = wordTextTemplate.rectTransform;
        RectTransform parentRect = templateRect.parent as RectTransform;

        if (parentRect == null)
        {
            Debug.LogWarning("wordTextTemplate의 부모 RectTransform을 찾을 수 없습니다.");
            return;
        }

        GameObject contentObject = new GameObject(
            "SubtitleContent",
            typeof(RectTransform)
        );

        contentObject.transform.SetParent(parentRect, false);

        subtitleContent = contentObject.GetComponent<RectTransform>();
        subtitleContent.anchorMin = templateRect.anchorMin;
        subtitleContent.anchorMax = templateRect.anchorMax;
        subtitleContent.pivot = templateRect.pivot;
        subtitleContent.anchoredPosition = templateRect.anchoredPosition;
        subtitleContent.sizeDelta = templateRect.sizeDelta;
        subtitleContent.offsetMin = templateRect.offsetMin;
        subtitleContent.offsetMax = templateRect.offsetMax;
    }

    private void SetupTemplate()
    {
        if (wordTextTemplate == null)
            return;

        wordTextTemplate.gameObject.SetActive(false);
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
        }
    }

    private float EaseOutCubic(float t)
    {
        return 1f - Mathf.Pow(1f - t, 3f);
    }

    private void ClearSubtitle()
    {
        if (subtitleContent != null)
        {
            for (int i = subtitleContent.childCount - 1; i >= 0; i--)
            {
                Destroy(subtitleContent.GetChild(i).gameObject);
            }
        }

        lineViews.Clear();
        currentLineView = null;
        currentLine = "";
    }
}