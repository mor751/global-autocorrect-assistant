# Global Autocorrect

Windows tray app that corrects high-confidence English typos after a completed word.

## Run from source

```powershell
dotnet run --project src\Autocorrect.App\Autocorrect.App.csproj
```

The app starts in the system tray. Right-click the tray icon to pause corrections, edit settings, view recent corrections, or quit.

## Features

- Global Windows autocorrect after completed words.
- Floating word suggestions while typing.
- Floating `Improve` pill after a pause in allowed text fields.
- Compact rewrite modal with actions: fix typos, optimize prompt, compress tokens, clarify, professional, direct, coding prompt, video prompt.
- Personal learning for common words and accepted corrections.
- Protected vocabulary for names/tools/brands that should never be corrected.
- Local-only rewrite mode by default; no keystrokes are sent to AI.
- Optional OpenAI-compatible endpoint setting for explicit rewrite actions only.
- Correction history stored locally with undo.

## Correction engine

The app uses a fast local correction engine:

- bundled English frequency dictionary
- SymSpell-style delete index for fast candidate lookup
- Damerau-Levenshtein distance for insert/delete/substitute/transposition typos
- word-frequency and small context boosts for ranking
- no Ollama, Python, or TensorFlow in the live keyboard path
- protected vocabulary and unsafe token detection
- learned personal corrections and rejection penalties

ONNX is kept as a disabled future fallback slot. The intended path is to train/prototype a small model in Python later, export it to ONNX, then run it directly from C# only for uncertain cases.

Default speed setting:

```text
Max correction latency: 230 ms
```

The keyboard hook never waits for correction work.

## Hotkeys

```text
Ctrl+Alt+Backspace  Undo last correction
Ctrl+Alt+Pause      Pause/resume app
Ctrl+Alt+F          Open fix-typos rewrite modal
Ctrl+Alt+O          Open optimize-prompt rewrite modal
Ctrl+Alt+S          Open compress-tokens rewrite modal
```

## Woody project brain

Woody can compile messy coding prompts into project-aware prompts for Codex, Cursor, Claude Code, and similar agents.

Local setup:

```powershell
docker run -p 6333:6333 qdrant/qdrant
pip install fastembed
ollama pull gemma3:4b
```

Flow:

1. Click Woody.
2. Choose Folder.
3. Wait for indexing to finish.
4. Press `Ctrl+Alt+O` or click Fix Prompt.

The first folder selection scans and indexes the project. Later prompt optimization does not rescan the whole folder; it embeds only the prompt, searches Qdrant, sends compact retrieved context to `gemma3:4b`, and shows the optimized prompt. If Qdrant, FastEmbed, or Gemma is unavailable, Woody falls back locally and shows the fallback mode in Dashboard / View RAG.

Project brain data is stored under:

```text
%APPDATA%\GlobalAutocorrect\brain
```

## Local data

Settings and learning data are stored under:

```text
%APPDATA%\GlobalAutocorrect
```

Files:

```text
settings.json
learned-corrections.json
protected-vocabulary.json
correction-history.jsonl
```

The app stores word-level learning data and correction records locally. It does not store full typed text by default.

## Published executable

```powershell
dotnet publish src\Autocorrect.App\Autocorrect.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The executable is written to:

```text
src\Autocorrect.App\bin\Release\net8.0-windows\win-x64\publish\GlobalAutocorrect.exe
```

This build can be published either framework-dependent or self-contained. The project is structured so the app target can be moved to `net10.0-windows` when that SDK is installed.

## Safety defaults

- Corrections run only after space or punctuation.
- Terminals and code editors are blocked by default.
- Password and obvious sensitive fields are skipped when Windows UI Automation exposes that context.
- Corrections are conservative and fully local.
- AI rewrite is explicit only; it is never called for every word.
- Local-only mode is enabled by default.
- Remote Desktop / VM windows are blocked by default.
- ONNX fallback is off by default until a dedicated spelling model is added.

## Tests

```powershell
dotnet run --project tests\Autocorrect.Tests\Autocorrect.Tests.csproj
```
