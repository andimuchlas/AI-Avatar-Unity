# AI Avatar Unity

Avatar AI 3D interaktif yang mampu bercakap-cakap secara real-time menggunakan suara. Project ini mengintegrasikan MetaPerson avatar dengan Oculus Lipsync untuk animasi wajah yang natural, dilengkapi voice pipeline end-to-end untuk percakapan AI.

## Fitur

- 🗣️ **Voice Conversation** - Percakapan suara real-time dengan AI
- 👄 **Lip-sync Otomatis** - Animasi bibir sinkron dengan audio menggunakan Oculus Lipsync
- 🤖 **AI Powered** - Respon cerdas dari DeepSeek LLM
- 🎭 **Avatar 3D Realistis** - MetaPerson avatar dengan ekspresi wajah natural
- ⚡ **Real-time Processing** - Pipeline suara low-latency dengan Docker containers

## Voice Pipeline

```
Suara User → STT (piper1-gpl) → Teks → LLM (DeepSeek) → Respon Teks → TTS (faster-whisper-tiny) → Audio → Avatar Bicara
```

### Tech Stack

**Frontend (Unity)**
- MetaPerson Avatar - Avatar 3D realistis dari Avatar SDK untuk visualisasi karakter
- Oculus Lipsync - Plugin Meta untuk sinkronisasi gerakan bibir dengan audio (viseme-based)
- Voice Pipeline - WebSocket client untuk komunikasi real-time dengan backend

**Backend (Docker Containers)**
- STT: `piper1-gpl` - Speech-to-Text engine
- TTS: `Systran/faster-whisper-tiny` - Text-to-Speech engine  
- LLM: `DeepSeek` - AI conversation model (via API)

Selain DeepSeek, semua service backend berjalan dalam container Docker.
