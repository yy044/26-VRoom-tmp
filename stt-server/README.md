# VRoom STT Server

Local Whisper-backed speech-to-text server for Unity.

## Run Locally

```powershell
cd stt-server
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
uvicorn server:app --host 127.0.0.1 --port 8000
```

Unity connects to:

```text
ws://127.0.0.1:8000/ws/stt
```

In the Unity scene, select the object with `SpeechToTextManager`, then set:

```text
Provider Type: Whisper Server
Whisper Server Url: ws://127.0.0.1:8000/ws/stt
```

Use `Windows Native` to switch back to Unity's `DictationRecognizer` provider.
