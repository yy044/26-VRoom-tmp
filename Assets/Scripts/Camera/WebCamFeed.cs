using UnityEngine;
using UnityEngine.UI;

public class WebCamFeed : MonoBehaviour
{
    public RawImage display;
    public WebCamTexture webcamTexture;

    void Start()
    {
        webcamTexture = new WebCamTexture(1280, 720, 30);
        display.texture = webcamTexture;
        webcamTexture.Play();

        // Mirror for selfie view
        display.rectTransform.localScale = new Vector3(-1, 1, 1);
    }

    public int Width => webcamTexture.width;
    public int Height => webcamTexture.height;
    public bool IsReady => webcamTexture != null && webcamTexture.isPlaying 
                           && webcamTexture.width > 100; // width is 16 until actually ready

    void OnDestroy()
    {
        if (webcamTexture != null) webcamTexture.Stop();
    }
}
