import modal

image = (
    modal.Image.from_registry(
        "nvidia/cuda:12.4.1-cudnn-runtime-ubuntu22.04",
        add_python="3.11",
    )
    .apt_install("ffmpeg")
    .pip_install_from_requirements("requirements.txt")
    .add_local_file("server.py", remote_path="/root/server.py")
)

app = modal.App("vroom-realtime-stt")


@app.function(
    image=image,
    gpu="T4",
    timeout=60 * 10,
    scaledown_window=60,
    env={
        "WHISPER_MODEL": "large-v3-turbo",
        "DEVICE": "cuda",
        "COMPUTE_TYPE": "float16",

        "SAMPLE_RATE": "16000",
        "LANGUAGE": "ko",

        "VOICE_RMS_THRESHOLD": "0.0015",
        "MIN_AUDIO_MS": "500",
        "FINAL_SILENCE_MS": "1200",

        "PARTIAL_INTERVAL_MS": "900",
        "PARTIAL_MIN_AUDIO_MS": "900",
        "PARTIAL_WINDOW_SEC": "8",

        "LOG_DIR": "/tmp/logs",
    },
)
@modal.concurrent(max_inputs=2)
@modal.asgi_app()
def fastapi_app():
    import sys

    sys.path.append("/root")

    from server import app as fastapi_app_instance

    return fastapi_app_instance