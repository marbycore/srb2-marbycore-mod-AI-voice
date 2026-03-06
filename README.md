# SRB2 MarbyCore AI Mod — Tails AI Voice Integration 🦊🤖

A total immersion mod for **Sonic Robo Blast 2 (SRB2)** that integrates a real-time conversational AI embodied as Tails. It features advanced speech recognition, high-quality text-to-speech synthesis, and live game telemetry.

---

## 🎮 Features

- **Mission Mode**: Tails provides live, context-aware commentary on rings, lives, enemies, hazards, and objectives.
- **Chat Mode**: Have a free-form conversation with Tails about anything, including deep-cut SRB2 lore.
- **Quiz Mode**: Sonic's Challenge! Answer Tails' trivia to win in-game rewards like rings and extra lives.
- **Voice Control**: Command the game using your voice (e.g., "Add 50 rings", "Change gravity", "Activate god mode").

---

## 🛠️ Installation & Build Guide (Step-by-Step)

This repository contains the **Source Code**. To use it, you must follow these steps to build the executables and provide the game assets.

### 1. Prerequisites (Setup your PC)

You need to have these tools installed to compile the code:
- **Visual Studio 2022 Build Tools**: Download and install from [Microsoft](https://visualstudio.microsoft.com/downloads/). During installation, make sure to check the "C++ Build Tools" workload.
- **SRB2 v2.2.x Assets**: You must own a copy of the original SRB2 game to get the necessary data files.

### 2. Prepare the Game Assets (.PK3)

Due to copyright laws, this repository **does not** include the original game assets. You must copy them manually from your original SRB2 installation folder.

**Copy these files from your SRB2 folder to the root of this folder:**

| File Name | Where is it? (Original Folder) | Where to put it? (This Folder) |
|---|---|---|
| `srb2.pk3` | Main SRB2 folder | Root folder (next to `build_srb2.bat`) |
| `zones.pk3` | Main SRB2 folder | Root folder (next to `build_srb2.bat`) |
| `music.pk3` | Main SRB2 folder | Root folder (next to `build_srb2.bat`) |
| `characters.pk3` | Main SRB2 folder | Root folder (next to `build_srb2.bat`) |

> [!IMPORTANT]
> Without these 4 files, the game will **not** start.

### 3. Compiling the Mod

Once you have your assets in place:
1.  **Open this folder** in your file explorer.
2.  Find the file named **`build_srb2.bat`**.
3.  **Double-click** it.
4.  A black window (terminal) will open. It will automatically compile the game and the AI dashboards.
5.  Wait until it says `BUILD COMPLETE!` and press any key to close the window.

---

## ⚙️ Hardware & AI Configuration

This mod is optimized for the following setup:

### Recommended Setup (Local AI)
- **Model**: `goekdeniz-guelmez/josiefied-qwen3-8b-abliterated-v1`
- **Software**: **LM Studio**
- **Settings**: 
  - Flash Attention: **ON**
  - KV Caching: **Q4_0**
  - Context Window: **40960**
  - GPU Offload: **MAX (36 layers)**
- **Hardware**: Tested on **RTX 30 series / 40 series** (8GB+ VRAM recommended).
- **TTS Engine**: **[Coqui TTS XTTS v2](https://github.com/marbycore/XTTS-v2_audio_generator_marbycore_cuda12_F16_Blackwell)**. To generate high-quality audio locally, download this supplementary API and leave it running in the background. *(Note: If you do not have a powerful GPU, you can use Piper instead, which runs on your CPU).*

### Low-End / No GPU Setup (Cloud AI / CPU)
If your PC isn't powerful enough to run the local AI models or the Coqui XTTS v2 generator, you can use these alternatives:
1.  **OpenRouter**: Select `OpenRouter` in the Dashboard and paste your API key in `mod/ai_provider_settings.json`.
2.  **Groq (STT)**: Select `Groq` in the Mic Manager for lighting-fast free voice recognition.
3.  **Piper (TTS)**: Select `Piper` in the Voice Manager for lightweight offline voice synthesis. This is the recommended alternative to Coqui XTTS v2 if you do not have a powerful GPU.

---

## 🕹️ How to Start Playing

1.  **Start the Game**: Double-click `Srb2Win.exe` in the root folder.
2.  **Start the AI**: Double-click `TelemetryDashboard.exe` in the same folder.
3.  **Select Mode**: In the Dashboard, choose between Mission, Chat, or Quiz.
4.  **Talk!**: Press and hold the **'C' key** on your keyboard to speak to Tails. The AI will listen as long as you hold the key.

---

## ⚠️ Community & Safety Guidelines

### SRB2 Message Board (MB) Compliance
- **Legality**: This mod does not distribute any copyrighted `.pk3` or `.wad` files from the base game. Users must provide their own.
- **Privacy**: No telemetry or API data is sent outside of your chosen AI providers.
- **Open Source**: All source code is provided for transparency.

---

## 📜 Credits & Disclaimers

### Sonic Team Junior
A massive thank you to **Sonic Team Junior** for creating **Sonic Robo Blast 2**, the best 3D Sonic fan game in existence. This mod is built upon their incredible work.
- Official Site: [srb2.org](https://www.srb2.org/)

### Mod Creator
Developed by **MarbyCore**.

---
*Disclaimer: This is a non-profit fan project. Sonic the Hedgehog is a trademark of SEGA. SRB2 is a non-commercial fan game.*
