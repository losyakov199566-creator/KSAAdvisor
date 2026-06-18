# KSA AI Advisor — Инструкция по установке

## Что это

Самостоятельный C# мод для KSA. Заменяет связку KittenRemoteControl + Python.  
Один DLL файл — читает игру напрямую, вызывает DeepSeek/OpenRouter, рисует ImGui окно.

---

## Требования

- .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0
- StarMap 0.4.x (или выше): уже должен быть у тебя
- Аккаунт OpenRouter: openrouter.ai (или DeepSeek API напрямую)

---

## Структура файлов

Создай такую структуру на диске:

```
D:\ksamodding\
├── KSA_dlls\               ← уже есть (KSA.dll, Brutal.ImGui.dll и др.)
├── StarMap\                ← уже есть
└── KSAAdvisor\             ← СОЗДАЙ, скопируй все файлы сюда
    ├── KSAAdvisor.csproj
    ├── AdvisorMod.cs
    ├── AdvisorWindow.cs
    ├── ChatManager.cs
    ├── Config.cs
    ├── GameStateReader.cs
    ├── LLMClient.cs
    └── build.bat
```

---

## Шаг 1 — Создай папку проекта

```
mkdir D:\ksamodding\KSAAdvisor
```

Скопируй все 7 файлов .cs, .csproj и build.bat в эту папку.

---

## Шаг 2 — Проверь пути в build.bat

Открой `build.bat` в блокноте. Проверь первые две строки:

```batch
set GAME_DIR=D:\Kitten Space Agency
set KSA_DLLS=%GAME_DIR%\Content\KSA_dlls
```

Если игра стоит в другом месте — поправь `GAME_DIR`.

---

## Шаг 3 — Запусти сборку

```
cd D:\ksamodding\KSAAdvisor
build.bat
```

Батник:
1. Соберёт проект через `dotnet build`
2. Создаст папку `D:\Kitten Space Agency\Content\KSAAdvisor\`
3. Скопирует `KSAAdvisor.dll` в папку мода
4. Скажет что добавить в `manifest.toml`

---

## Шаг 4 — Добавь мод в manifest.toml

Открой файл:
```
D:\Kitten Space Agency\Content\manifest.toml
```

Добавь в конец:
```toml
[[mods]]
id = "KSAAdvisor"
enabled = true
```

Если там был `KittenRemoteControl` — можешь поставить `enabled = false` или удалить.

---

## Шаг 5 — Запусти игру

```
D:\ksamodding\StarMap\StarMap.Loader.exe
```

В консоли StarMap должна появиться строка:
```
[KSAAdvisor] Советник готов. Нажми F2 для открытия окна.
```

---

## Шаг 6 — Введи API ключ

1. В игре нажми **F2**
2. Появится окно советника с полем для API ключа
3. Вставь ключ от OpenRouter (начинается с `sk-or-...`)
4. Нажми **Сохранить**

Ключ сохраняется в:
```
D:\Kitten Space Agency\Content\KSAAdvisor\config.json
```

---

## Смена модели

Открой `config.json` и поменяй поле `Model`:

```json
{
  "ApiKey": "sk-or-...",
  "BaseUrl": "https://openrouter.ai/api/v1",
  "Model": "deepseek/deepseek-chat",
  "MaxTokens": 512
}
```

Примеры моделей через OpenRouter:
- `deepseek/deepseek-chat` — выбранная на бенчмарке, дёшево
- `anthropic/claude-haiku-4-5` — дороже, но нативная
- `google/gemini-2.5-flash-lite` — дёшево, хорошее качество
- `openai/gpt-4.1-nano` — тоже дёшево

---

## Если ImGui не компилируется

`Brutal.ImGuiApi` может иметь немного другое API в твоей версии StarMap.  
Если `ImGuiCond`, `ImGuiCol`, `ImGuiTabBarFlags` дают ошибки компилятора:

1. Открой `Brutal.ImGui.dll` через dnSpy или ILSpy
2. Посмотри имена enum'ов в пространстве имён `Brutal.ImGuiApi`
3. Поправь в `AdvisorWindow.cs`

Чаще всего отличается только регистр: `ImGuiCond.FirstUseEver` vs `ImGuiCond_.FirstUseEver`.

---

## Структура после установки

```
D:\Kitten Space Agency\Content\KSAAdvisor\
├── KSAAdvisor.dll      ← основной файл мода
├── config.json         ← создаётся автоматически при первом сохранении ключа
└── chats\
    ├── abc12345.json   ← история первого чата
    └── def67890.json   ← история второго чата
```

---

## Куда Python делся

Python больше не нужен совсем.  
`KittenRemoteControl` тоже не нужен — `GameStateReader` читает игру напрямую через C# API.
