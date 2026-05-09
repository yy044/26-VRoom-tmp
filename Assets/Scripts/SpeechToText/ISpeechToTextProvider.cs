using System;

public interface ISpeechToTextProvider
{
    event Action<string> OnPartialText;
    event Action<string> OnFinalText;

    void StartListening();
    void StopListening();
}