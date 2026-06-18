KSA AI Advisor
==============

An AI mission advisor for Kitten Space Agency.
Reads live vessel telemetry and answers questions about your mission —
transfer windows, dV calculations, maneuver planning, landing advice.


REQUIREMENTS
------------
  - Kitten Space Agency (Compatible with KSA v2026.6.3.4568 and earlier)
  - StarMap mod loader: github.com/StarMapLoader/StarMap/releases
  - API key from any compatible AI provider


INSTALLATION
------------
  Easy: run install.bat — it does everything automatically.

  Manual:
    1. Extract KSAAdvisor folder to:
       Documents\My Games\Kitten Space Agency\mods\
    2. Add to Documents\My Games\Kitten Space Agency\manifest.toml:
       [[mods]]
       id = "KSAAdvisor"
       enabled = true


FIRST LAUNCH
------------
  1. Start the game through StarMap.Loader.exe
  2. Press F2 to open the advisor window
  3. Enter your API key and model name, click Save
  4. Done — start asking questions!

Logs location:
  %USERPROFILE%\Documents\My Games\Kitten Space Agency\mods\KSAAdvisor\advisor.log
  
  
WHERE TO GET AN API KEY
-----------------------
  The advisor works with any provider that supports the
  OpenAI-compatible API (/v1/chat/completions).

  The easiest way to get started is OpenRouter — one key gives
  access to hundreds of models from different companies:

    OpenRouter   openrouter.ai/keys

  Direct provider access is also supported:

    OpenAI       platform.openai.com/api-keys
    DeepSeek     platform.deepseek.com/api_keys
    Groq         console.groq.com/keys
    Mistral      console.mistral.ai/api-keys

  If you're using a provider other than OpenRouter, enter their
  URL in the Provider URL field on the setup screen.


SUPPORT
-------
  Questions and bug reports: [discord/github link]
