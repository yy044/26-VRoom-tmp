using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class WhisperServerSpeechProvider : ISpeechToTextProvider, ISpeechToTextTickProvider
{
    private const int SampleRate = 16000;
    private const int ClipSeconds = 1;
    private const int MaxChunkSamples = 2048;

    public event Action<string> OnPartialText;
    public event Action<string> OnFinalText;

    private readonly Uri serverUri;
    private readonly ConcurrentQueue<byte[]> pendingAudio = new ConcurrentQueue<byte[]>();

    private ClientWebSocket websocket;
    private CancellationTokenSource cancellation;
    private AudioClip microphoneClip;
    private string microphoneDevice;
    private int lastSamplePosition;
    private bool isListening;

    public WhisperServerSpeechProvider(string url)
    {
        serverUri = new Uri(string.IsNullOrWhiteSpace(url) ? "ws://127.0.0.1:8000/ws/stt" : url);
    }

    public void StartListening()
    {
        if (isListening)
            return;

        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("Whisper STT server provider could not start: no microphone devices found.");
            return;
        }

        microphoneDevice = Microphone.devices[0];
        microphoneClip = Microphone.Start(microphoneDevice, true, ClipSeconds, SampleRate);
        lastSamplePosition = 0;
        isListening = true;

        cancellation = new CancellationTokenSource();
        _ = RunWebSocketAsync(cancellation.Token);

        Debug.Log($"Whisper STT server provider started: {serverUri}");
    }

    public void Tick()
    {
        if (!isListening || microphoneClip == null)
            return;

        int currentPosition = Microphone.GetPosition(microphoneDevice);
        if (currentPosition < 0 || currentPosition == lastSamplePosition)
            return;

        int clipSamples = microphoneClip.samples;

        if (currentPosition > lastSamplePosition)
        {
            QueueSamples(lastSamplePosition, currentPosition - lastSamplePosition);
        }
        else
        {
            QueueSamples(lastSamplePosition, clipSamples - lastSamplePosition);
            if (currentPosition > 0)
                QueueSamples(0, currentPosition);
        }

        lastSamplePosition = currentPosition;
    }

    public void StopListening()
    {
        if (!isListening)
            return;

        isListening = false;

        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        if (!string.IsNullOrEmpty(microphoneDevice) && Microphone.IsRecording(microphoneDevice))
            Microphone.End(microphoneDevice);

        microphoneClip = null;
        microphoneDevice = null;
        lastSamplePosition = 0;

        Debug.Log("Whisper STT server provider stopped");
    }

    private void QueueSamples(int offset, int count)
    {
        while (count > 0)
        {
            int chunkSamples = Mathf.Min(count, MaxChunkSamples);
            var samples = new float[chunkSamples];

            if (!microphoneClip.GetData(samples, offset))
                return;

            pendingAudio.Enqueue(ConvertFloatToPcm16(samples));

            offset += chunkSamples;
            count -= chunkSamples;
        }
    }

    private static byte[] ConvertFloatToPcm16(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];

        for (int i = 0; i < samples.Length; i++)
        {
            float clamped = Mathf.Clamp(samples[i], -1f, 1f);
            short value = (short)Mathf.RoundToInt(clamped * short.MaxValue);
            bytes[i * 2] = (byte)(value & 0xff);
            bytes[i * 2 + 1] = (byte)((value >> 8) & 0xff);
        }

        return bytes;
    }

    private async Task RunWebSocketAsync(CancellationToken token)
    {
        websocket = new ClientWebSocket();

        try
        {
            await websocket.ConnectAsync(serverUri, token);
            Debug.Log("Connected to Whisper STT server.");

            Task receiveTask = ReceiveLoopAsync(websocket, token);
            Task sendTask = SendLoopAsync(websocket, token);

            await Task.WhenAny(receiveTask, sendTask);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.LogError($"Whisper STT server connection failed: {ex.Message}");
        }
        finally
        {
            await CloseWebSocketAsync(websocket);
            websocket.Dispose();
            websocket = null;
            cancellation?.Dispose();
            cancellation = null;
        }
    }

    private async Task SendLoopAsync(ClientWebSocket socket, CancellationToken token)
    {
        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            if (!pendingAudio.TryDequeue(out var audio))
            {
                await Task.Delay(15, token);
                continue;
            }

            await socket.SendAsync(
                new ArraySegment<byte>(audio),
                WebSocketMessageType.Binary,
                true,
                token
            );
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = new byte[8192];

        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var builder = new StringBuilder();
            WebSocketReceiveResult result;

            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);

                if (result.MessageType == WebSocketMessageType.Close)
                    return;

                builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            HandleServerMessage(builder.ToString());
        }
    }

    private void HandleServerMessage(string json)
    {
        SttServerMessage message;

        try
        {
            message = JsonUtility.FromJson<SttServerMessage>(json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Could not parse STT server message: {ex.Message}");
            return;
        }

        if (message == null || string.IsNullOrEmpty(message.type))
            return;

        switch (message.type)
        {
            case "partial":
                if (!string.IsNullOrEmpty(message.text))
                    OnPartialText?.Invoke(message.text);
                break;

            case "final":
                if (!string.IsNullOrEmpty(message.text))
                    OnFinalText?.Invoke(message.text);
                break;

            case "error":
                Debug.LogWarning($"STT server error: {message.message}");
                break;
        }
    }

    private static async Task CloseWebSocketAsync(ClientWebSocket socket)
    {
        if (socket == null)
            return;

        if (socket.State != WebSocketState.Open)
            return;

        try
        {
            byte[] closeCommand = Encoding.UTF8.GetBytes("{\"command\":\"close\"}");
            await socket.SendAsync(
                new ArraySegment<byte>(closeCommand),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None
            );

            await socket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Unity STT provider stopped",
                CancellationToken.None
            );
        }
        catch
        {
        }
    }

    [Serializable]
    private class SttServerMessage
    {
        public string type = "";
        public string text = "";
        public string message = "";
    }
}
