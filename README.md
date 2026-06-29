# KSA AI Advisor

An AI mission advisor for Kitten Space Agency. Reads live vessel telemetry
and answers questions about your mission — transfer windows, dV budgets,
maneuver planning, and anything else where a thinking copilot beats a
calculator.

## 📥 Download

**[➜ Download KSAAdvisor v1.0](https://github.com/losyakov199566-creator/KSAAdvisor/releases/latest/download/KSAAdvisor.v1.0.zip)**

Then follow the [installation steps](#installation) below.

---

## What it does

- **Live telemetry** — knows your orbit, fuel, dV, TWR, situation, attitude
- **Transfer windows** — when to leave for Mars, Luna, anywhere in the system
- **Maneuver planning** — can create circularization burns directly
- **Time warp** — warp to a date or to the next maneuver on request
- **Multi-chat** — keep separate conversations for different missions
- **Custom persona** — drop a `persona.txt` next to the DLL to change the
  advisor's tone and style

The advisor is designed to reason about non-standard situations, not to
replace specialized mods (orbit planners, builders, etc).

## Requirements

- Kitten Space Agency (tested on **v2026.6.3.4568**, may work on earlier builds)
- [StarMap mod loader](https://github.com/StarMapLoader/StarMap/releases)
- An API key from any OpenAI-compatible provider (see [below](#where-to-get-an-api-key))

## Installation

**Easy:** run `install.bat` from the extracted folder — it does everything
automatically.

**Manual:**

1. Extract the `KSAAdvisor` folder to:
   `Documents\My Games\Kitten Space Agency\mods\`
2. Add to `Documents\My Games\Kitten Space Agency\manifest.toml`:
```toml
   [[mods]]
   id = "KSAAdvisor"
   enabled = true
```

## First launch

1. Start the game through `StarMap.Loader.exe`
2. Press **F2** to open the advisor window
3. Enter your API key and model name, click **Save**
4. Ask your first question

## Where to get an API key

The advisor works with any provider that supports the OpenAI-compatible
API (`/v1/chat/completions`).

The easiest way to get started is **OpenRouter** — one key gives access
to hundreds of models from different companies:

- [OpenRouter](https://openrouter.ai/keys) — recommended

Direct provider access is also supported:

- [OpenAI](https://platform.openai.com/api-keys)
- [DeepSeek](https://platform.deepseek.com/api_keys)
- [Groq](https://console.groq.com/keys)
- [Mistral](https://console.mistral.ai/api-keys)

If you're using a provider other than OpenRouter, enter their URL in the
**Provider URL** field on the setup screen.

## Configuration

A `config.json` is generated next to the DLL on first run. You can edit
it directly to tune behavior:

- `ApiKey`, `BaseUrl`, `Model`, `MaxTokens` — provider settings
- `HistoryLimit` — how many past messages to send as context (default: 10)
- `UserSkillLevel` — `"beginner"`, `"experienced"` (default), or `"expert"`.
  Beginners get short explanations of terms; experts get pure numbers.

To customize the advisor's persona, create a `persona.txt` next to the
DLL — its contents replace the default character description.

## Logs
