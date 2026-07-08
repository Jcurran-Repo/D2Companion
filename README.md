# D2Companion

A voice companion for **Diablo II: Resurrected**. You play; you talk to the app; an
in-app Claude that knows D2 advises you and — the twist — **decides your build**. Because
Claude is the one making the decisions, tracking your character is trivial: the app just
records what Claude decided (plus any manual corrections). No screen reading, no memory
hacks.

> **Working name.** `D2Companion` is a placeholder — rename freely.

## Architecture (target)

- **Voice I/O:** ElevenLabs speech-to-text + text-to-speech only.
- **Brain:** our own loop against the Anthropic Messages API (Opus 4.8), so we get the
  newest model, our own key, prompt caching to keep tokens down, and full control of the
  turn-taking / filler-handling.
- **State:** Claude's tool calls are the source of truth; a hand-editable sheet lets you
  correct anything. Every change lands in an append-only **decision ledger**.
- **UI:** WPF.

## Projects

| Project | What it is |
|---|---|
| `src/D2Companion.Core` | Domain model (`Character`), SQLite store, decision ledger, `CharacterService` — the one audited place the character can change. |
| `src/D2Companion.App` | WPF character sheet + live ledger (MVVM). |
| `src/D2Companion.Harness` | Headless console that exercises Core by simulating Claude's decisions — lets us test the brain/state pipeline without the GUI. |
| `tests/D2Companion.Tests` | xUnit tests for the Core pipeline. |

## Phase status

- **Phase 0 — done:** solution, `Character` model + SQLite + ledger, editable WPF sheet,
  console harness, tests. No voice or AI yet; no API keys required.
- **Phase 1 — next:** the Anthropic brain (tool calls that drive `CharacterService`) +
  ElevenLabs voice, push-to-talk first.
- **Phase 2:** full tool set, `get_character_state`, transcript, reminders.
- **Phase 3:** open-mic mode, ledger-based undo, multiple characters, optional overlay.

## Running

```bash
# WPF app (state persists in %LocalAppData%\D2Companion\d2companion.db)
dotnet run --project src/D2Companion.App

# Headless harness (prints a simulated build + the decision ledger)
dotnet run --project src/D2Companion.Harness

# Tests
dotnet test
```

## Keys

Stored locally in user-secrets — nothing touches the repo. The **app and the harness
share one secrets store** (UserSecretsId `d2companion-app-secrets`), so set them once
against the app project:

```bash
dotnet user-secrets set "Anthropic:ApiKey"   "sk-ant-..." --project src/D2Companion.App
dotnet user-secrets set "ElevenLabs:ApiKey"  "..."        --project src/D2Companion.App
dotnet user-secrets set "ElevenLabs:VoiceId" "..."        --project src/D2Companion.App
```

- **`ElevenLabs:VoiceId` must be a Voice ID, not an agent ID.** Get it from ElevenLabs →
  **Voices** → pick a voice → **Voice ID** (a ~20-char string like `21m00Tcm4TlvDq8ikWAM`).
  An `agent_…` id is for the Conversational-AI product and won't work with our text-to-speech.
- Anthropic key alone → the harness text chat and the app's brain work. Add the ElevenLabs
  key + Voice ID → full push-to-talk voice in the app.
- The harness also honors the `ANTHROPIC_API_KEY` environment variable as a fallback.
