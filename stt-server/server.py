import asyncio
import json
import os
import time
import uuid
from collections import deque
from contextlib import asynccontextmanager
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import numpy as np
from dotenv import load_dotenv
from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from faster_whisper import WhisperModel

load_dotenv()


class Config:
    # Local default: small / cpu / int8
    # Modal default is overridden in modal_app.py: large-v3-turbo / cuda / float16
    WHISPER_MODEL = os.getenv("WHISPER_MODEL", "small")
    DEVICE = os.getenv("DEVICE", "cpu")
    COMPUTE_TYPE = os.getenv("COMPUTE_TYPE", "int8")

    SAMPLE_RATE = int(os.getenv("SAMPLE_RATE", "16000"))
    LANGUAGE = os.getenv("LANGUAGE", "ko")

    # Basic energy gate. Tune this per microphone/environment.
    VOICE_RMS_THRESHOLD = float(os.getenv("VOICE_RMS_THRESHOLD", "0.0015"))
    MIN_AUDIO_MS = int(os.getenv("MIN_AUDIO_MS", "500"))
    FINAL_SILENCE_MS = int(os.getenv("FINAL_SILENCE_MS", "1200"))
    PREROLL_MS = int(os.getenv("PREROLL_MS", "300"))

    # Partial STT settings.
    PARTIAL_ENABLED = os.getenv("PARTIAL_ENABLED", "true").lower() == "true"
    PARTIAL_INTERVAL_MS = int(os.getenv("PARTIAL_INTERVAL_MS", "900"))
    PARTIAL_MIN_AUDIO_MS = int(os.getenv("PARTIAL_MIN_AUDIO_MS", "900"))

    # If an utterance gets too long, partial STT becomes expensive.
    # Final STT still runs on the full utterance.
    PARTIAL_MAX_AUDIO_SEC = float(os.getenv("PARTIAL_MAX_AUDIO_SEC", "14"))

    # faster-whisper VAD for final transcription. Keep false when you want
    # the server's own silence logic to control utterance boundaries only.
    WHISPER_VAD_FILTER = os.getenv("WHISPER_VAD_FILTER", "false").lower() == "true"

    # Logs are off by default because transcripts may contain personal data.
    ENABLE_LOGS = os.getenv("ENABLE_LOGS", "false").lower() == "true"
    LOG_DIR = os.getenv("LOG_DIR", "logs")


model: WhisperModel | None = None


@dataclass
class WordItem:
    text: str
    start: float | None = None
    end: float | None = None
    confidence: float | None = None


@dataclass
class STTSession:
    session_id: str
    sample_rate: int

    utterance_id: int = 0
    audio_parts: list[np.ndarray] = field(default_factory=list)
    preroll_parts: deque[np.ndarray] = field(default_factory=deque)

    has_voice: bool = False
    last_voice_time: float = field(default_factory=time.time)

    last_partial_time: float = 0.0
    last_partial_text: str = ""

    closed: bool = False

    audio_lock: asyncio.Lock = field(default_factory=asyncio.Lock)
    send_lock: asyncio.Lock = field(default_factory=asyncio.Lock)
    stt_lock: asyncio.Lock = field(default_factory=asyncio.Lock)

    def _max_preroll_chunks(self, chunk_samples: int) -> int:
        if chunk_samples <= 0:
            return 1
        chunks = int((Config.PREROLL_MS / 1000) * self.sample_rate / chunk_samples)
        return max(1, chunks)

    async def append_pcm16(self, payload: bytes) -> None:
        if not payload:
            return

        if len(payload) % 2 != 0:
            # Ignore malformed trailing byte rather than crashing the session.
            payload = payload[:-1]

        pcm = np.frombuffer(payload, dtype=np.int16)
        if pcm.size == 0:
            return

        audio = pcm.astype(np.float32) / 32768.0
        rms = float(np.sqrt(np.mean(audio ** 2))) if audio.size else 0.0
        is_voice = rms >= Config.VOICE_RMS_THRESHOLD

        async with self.audio_lock:
            if not self.has_voice:
                self.preroll_parts.append(audio)

                max_chunks = self._max_preroll_chunks(audio.size)
                while len(self.preroll_parts) > max_chunks:
                    self.preroll_parts.popleft()

            if is_voice:
                if not self.has_voice:
                    # Include a short pre-roll so consonants at speech start
                    # are not lost by the RMS gate.
                    self.audio_parts.extend(list(self.preroll_parts))
                    self.preroll_parts.clear()

                self.has_voice = True
                self.last_voice_time = time.time()

            if self.has_voice:
                self.audio_parts.append(audio)

    async def get_audio_copy(self) -> tuple[int, np.ndarray]:
        async with self.audio_lock:
            if not self.audio_parts:
                return self.utterance_id, np.array([], dtype=np.float32)

            return self.utterance_id, np.concatenate(self.audio_parts).astype(np.float32)

    async def pop_audio_for_final(self) -> tuple[int, np.ndarray] | None:
        async with self.audio_lock:
            if not self.audio_parts or not self.has_voice:
                return None

            utterance_id = self.utterance_id
            audio = np.concatenate(self.audio_parts).astype(np.float32)

            self.audio_parts.clear()
            self.preroll_parts.clear()
            self.has_voice = False
            self.last_voice_time = time.time()
            self.last_partial_time = 0.0
            self.last_partial_text = ""
            self.utterance_id += 1

            return utterance_id, audio


@asynccontextmanager
async def lifespan(app: FastAPI):
    global model

    if Config.ENABLE_LOGS:
        Path(Config.LOG_DIR).mkdir(parents=True, exist_ok=True)

    print("[startup] loading Whisper model")
    print(
        {
            "model": Config.WHISPER_MODEL,
            "device": Config.DEVICE,
            "compute_type": Config.COMPUTE_TYPE,
            "sample_rate": Config.SAMPLE_RATE,
            "language": Config.LANGUAGE,
            "partial_enabled": Config.PARTIAL_ENABLED,
            "whisper_vad_filter": Config.WHISPER_VAD_FILTER,
        }
    )

    model = WhisperModel(
        Config.WHISPER_MODEL,
        device=Config.DEVICE,
        compute_type=Config.COMPUTE_TYPE,
    )

    print("[startup] Whisper model loaded")
    yield


app = FastAPI(title="VRoom Realtime STT Server", lifespan=lifespan)


@app.get("/")
async def root() -> dict[str, Any]:
    return {
        "ok": True,
        "service": "VRoom Realtime STT Server",
        "websocket": "/ws/stt",
        "protocol": "partial/final JSON over WebSocket",
    }


@app.get("/health")
async def health() -> dict[str, Any]:
    return {
        "ok": True,
        "model": Config.WHISPER_MODEL,
        "device": Config.DEVICE,
        "compute_type": Config.COMPUTE_TYPE,
        "sample_rate": Config.SAMPLE_RATE,
        "language": Config.LANGUAGE,
        "partial_enabled": Config.PARTIAL_ENABLED,
    }


def safe_log_json(session_id: str, data: dict[str, Any]) -> None:
    if not Config.ENABLE_LOGS:
        return

    # Avoid storing full transcript text by default. Keep only operational metadata.
    sanitized = {
        "type": data.get("type"),
        "session_id": data.get("session_id"),
        "server_time": data.get("server_time"),
        "utterance_id": data.get("utterance_id"),
        "is_final": data.get("is_final"),
        "text_length": len(data.get("text") or ""),
        "audio_ms": data.get("audio_ms"),
        "latency_ms": data.get("latency_ms"),
        "message": data.get("message") if data.get("type") == "error" else None,
    }

    path = Path(Config.LOG_DIR) / f"{session_id}.jsonl"
    with path.open("a", encoding="utf-8") as f:
        f.write(json.dumps(sanitized, ensure_ascii=False) + "\n")


async def send_json(websocket: WebSocket, session: STTSession, data: dict[str, Any]) -> None:
    data.setdefault("session_id", session.session_id)
    data.setdefault("server_time", time.time())

    text = json.dumps(data, ensure_ascii=False)

    async with session.send_lock:
        await websocket.send_text(text)

    safe_log_json(session.session_id, data)


def normalize_word(word: str) -> str:
    return word.strip()


def transcribe_audio(audio: np.ndarray) -> tuple[str, list[WordItem]]:
    if model is None:
        raise RuntimeError("Whisper model is not loaded.")

    if audio.size == 0:
        return "", []

    segments, _info = model.transcribe(
        audio,
        language=Config.LANGUAGE,
        beam_size=1,
        best_of=1,
        temperature=0.0,
        word_timestamps=True,
        condition_on_previous_text=False,
        vad_filter=Config.WHISPER_VAD_FILTER,
        no_speech_threshold=0.6,
    )

    texts: list[str] = []
    words: list[WordItem] = []

    for segment in segments:
        text = (segment.text or "").strip()
        if text:
            texts.append(text)

        if segment.words:
            for w in segment.words:
                token = normalize_word(w.word or "")
                if not token:
                    continue

                words.append(
                    WordItem(
                        text=token,
                        start=w.start,
                        end=w.end,
                        confidence=getattr(w, "probability", None),
                    )
                )

    return " ".join(texts).strip(), words


def words_to_payload(words: list[WordItem]) -> list[dict[str, Any]]:
    return [
        {
            "word_id": i,
            "text": w.text,
            "start": w.start,
            "end": w.end,
            "confidence": w.confidence,
        }
        for i, w in enumerate(words)
    ]


async def run_partial_stt(websocket: WebSocket, session: STTSession) -> None:
    if not Config.PARTIAL_ENABLED:
        return

    now = time.time()
    if (now - session.last_partial_time) * 1000 < Config.PARTIAL_INTERVAL_MS:
        return

    if session.stt_lock.locked():
        return

    utterance_id, audio = await session.get_audio_copy()
    audio_ms = int(audio.size / session.sample_rate * 1000)

    if audio_ms < Config.PARTIAL_MIN_AUDIO_MS:
        return

    if audio.size > int(Config.PARTIAL_MAX_AUDIO_SEC * session.sample_rate):
        # Avoid making partial STT slower than the UI. Final still handles full audio.
        return

    async with session.stt_lock:
        session.last_partial_time = time.time()
        started = time.time()

        print(f"[stt] partial start | utterance={utterance_id}, audio_ms={audio_ms}")

        text, _words = await asyncio.to_thread(transcribe_audio, audio)
        text = text.strip()
        latency_ms = int((time.time() - started) * 1000)

        print(f"[stt] partial text | {text}")

        if not text:
            return

        # Full partial text update, not token diff.
        # Unity should replace the displayed text for the same utterance_id.
        if text == session.last_partial_text:
            return

        session.last_partial_text = text

        await send_json(
            websocket,
            session,
            {
                "type": "partial",
                "utterance_id": utterance_id,
                "speaker_id": "speaker_0",
                "text": text,
                "is_final": False,
                "audio_ms": audio_ms,
                "latency_ms": latency_ms,
            },
        )


async def run_final_stt(websocket: WebSocket, session: STTSession) -> None:
    async with session.stt_lock:
        item = await session.pop_audio_for_final()

        if item is None:
            return

        utterance_id, audio = item
        audio_ms = int(audio.size / session.sample_rate * 1000)

        if audio_ms < Config.MIN_AUDIO_MS:
            print(f"[stt] final skipped | too short: {audio_ms}ms")
            await send_json(
                websocket,
                session,
                {
                    "type": "empty_result",
                    "utterance_id": utterance_id,
                    "is_final": True,
                    "text": "",
                    "reason": "too_short",
                    "audio_ms": audio_ms,
                },
            )
            return

        started = time.time()
        print(f"[stt] final start | utterance={utterance_id}, audio_ms={audio_ms}")

        text, words = await asyncio.to_thread(transcribe_audio, audio)
        text = text.strip()
        latency_ms = int((time.time() - started) * 1000)

        print(f"[stt] final text | {text}")

        if not text:
            await send_json(
                websocket,
                session,
                {
                    "type": "empty_result",
                    "utterance_id": utterance_id,
                    "is_final": True,
                    "text": "",
                    "reason": "no_text",
                    "audio_ms": audio_ms,
                    "latency_ms": latency_ms,
                },
            )
            return

        await send_json(
            websocket,
            session,
            {
                "type": "final",
                "utterance_id": utterance_id,
                "speaker_id": "speaker_0",
                "text": text,
                "words": words_to_payload(words),
                "is_final": True,
                "audio_ms": audio_ms,
                "latency_ms": latency_ms,
            },
        )


async def monitor_loop(websocket: WebSocket, session: STTSession) -> None:
    while not session.closed:
        await asyncio.sleep(0.12)

        if not session.has_voice:
            continue

        silence_ms = int((time.time() - session.last_voice_time) * 1000)

        try:
            await run_partial_stt(websocket, session)

            if silence_ms >= Config.FINAL_SILENCE_MS:
                await run_final_stt(websocket, session)

        except Exception as e:
            print("[monitor] error:", repr(e))

            try:
                await send_json(
                    websocket,
                    session,
                    {
                        "type": "error",
                        "message": str(e),
                    },
                )
            except Exception:
                pass


@app.websocket("/ws/stt")
async def ws_stt(websocket: WebSocket):
    await websocket.accept()

    session = STTSession(
        session_id=str(uuid.uuid4()),
        sample_rate=Config.SAMPLE_RATE,
    )

    print(f"[ws] connected | {session.session_id}")

    await send_json(
        websocket,
        session,
        {
            "type": "session_started",
            "sample_rate": Config.SAMPLE_RATE,
            "audio_format": "pcm_s16le",
            "channels": 1,
            "protocol_version": "2.0",
            "input": {
                "binary": "PCM16 mono 16kHz chunks",
                "json_commands": {
                    "flush": "force final STT for current utterance",
                    "close": "finalize current utterance and close",
                    "ping": "server replies with pong",
                },
            },
            "output_types": [
                "partial",
                "final",
                "empty_result",
                "error",
                "pong",
            ],
            "partial_rule": "For the same utterance_id, replace the currently displayed text with the newest partial.text.",
            "final_rule": "When final arrives, replace the same utterance_id with final.text and mark it completed.",
        },
    )

    monitor_task = asyncio.create_task(monitor_loop(websocket, session))

    try:
        while True:
            message = await websocket.receive()

            if message.get("type") == "websocket.disconnect":
                break

            if message.get("bytes") is not None:
                await session.append_pcm16(message["bytes"])
                continue

            if message.get("text") is not None:
                try:
                    data = json.loads(message["text"])
                except json.JSONDecodeError:
                    await send_json(
                        websocket,
                        session,
                        {
                            "type": "error",
                            "message": "Invalid JSON message.",
                        },
                    )
                    continue

                command = data.get("command")

                if command == "flush":
                    await run_final_stt(websocket, session)

                elif command == "close":
                    await run_final_stt(websocket, session)
                    break

                elif command == "ping":
                    await send_json(
                        websocket,
                        session,
                        {
                            "type": "pong",
                        },
                    )

                else:
                    await send_json(
                        websocket,
                        session,
                        {
                            "type": "error",
                            "message": f"Unknown command: {command}",
                        },
                    )

    except WebSocketDisconnect:
        pass

    except RuntimeError as e:
        if "disconnect message has been received" not in str(e):
            raise

    finally:
        session.closed = True
        monitor_task.cancel()
        await asyncio.gather(monitor_task, return_exceptions=True)

        print(f"[ws] closed | {session.session_id}")