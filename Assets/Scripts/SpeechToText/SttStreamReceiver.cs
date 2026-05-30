// STT / 웹소켓으로부터 전달받은 텍스트를
// 자막 시스템으로 넘기는 "단일 입력 지점"

using System.Text;
using UnityEngine;

public class SttStreamReceiver : MonoBehaviour
{
    public StreamingSubtitleController subtitleController;

    private readonly StringBuilder legacyTokenBuffer = new StringBuilder();

    public void OnPartialTextReceived(string text)
    {
        if (subtitleController == null)
        {
            Debug.LogWarning("subtitleController가 연결되지 않았습니다.");
            return;
        }

        Debug.Log($"[STTAudit] receiver partial -> subtitleController={subtitleController.name}: {text}", this);
        subtitleController.ReceivePartialText(text);
    }

    public void OnFinalTextReceived(string text)
    {
        if (subtitleController == null)
        {
            Debug.LogWarning("subtitleController가 연결되지 않았습니다.");
            return;
        }

        Debug.Log($"[STTAudit] receiver final -> subtitleController={subtitleController.name}: {text}", this);
        subtitleController.ReceiveFinalText(text);
    }

    // 기존 토큰 방식과의 호환용
    public void OnTokenReceived(string token)
    {
        if (subtitleController == null)
        {
            Debug.LogWarning("subtitleController가 연결되지 않았습니다.");
            return;
        }

        if (string.IsNullOrEmpty(token))
            return;

        Debug.Log($"[STTAudit] receiver token: {token}", this);

        if (token == "\\n" || token == "\n")
        {
            string finalText = legacyTokenBuffer.ToString().Trim();

            if (!string.IsNullOrWhiteSpace(finalText))
                subtitleController.ReceiveFinalText(finalText);

            legacyTokenBuffer.Clear();
            return;
        }

        if (legacyTokenBuffer.Length > 0)
            legacyTokenBuffer.Append(' ');

        legacyTokenBuffer.Append(token.Trim());

        subtitleController.ReceivePartialText(legacyTokenBuffer.ToString());
    }
}
