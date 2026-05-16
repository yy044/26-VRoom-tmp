// STT / 웹소켓으로부터 전달받은 텍스트를
// 자막 시스템으로 넘기는 "단일 입력 지점"
//
// 사용 방법:
// 외부(STT, 웹소켓)에서 토큰이 들어올 때마다
// OnTokenReceived(token) 호출
//
// token 규칙:
// - 단어 단위 문자열 ("오늘", "날씨가", "좋습니다")
// - 문장 종료는 "\n"

using UnityEngine;

public class SttStreamReceiver : MonoBehaviour
{
    public StreamingSubtitleController subtitleController;

    // 나중에 웹소켓 메시지를 여기로 넣는다고 가정
    public void OnTokenReceived(string token)
    {
        if (subtitleController == null)
        {
            Debug.LogWarning("subtitleController가 연결되지 않았습니다.");
            return;
        }

        subtitleController.ReceiveToken(token);
    }
}