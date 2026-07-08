# D2Companion — Design Document

A voice companion for **Diablo II: Resurrected**. You play; you talk to it; an in-app
Claude that knows D2 advises you and **decides your build**. This document is the whole
picture — what's shipped, what's in progress, and the full target — so any future session
(human or Claude) can pick up with the design intact.

---

## 1. Vision & the twist

You're mid-game. You say "I just hit 12, where do my points go?" and a voice answers like a
knowledgeable friend on the couch: it decides, tells you, and quietly records the decision.
Over a run it drives your whole build — class, mode, skills, stats, gear, progression.

**The twist that makes it simple:** because Claude is the one *making* the decisions, we don't
reverse-engineer the game. No screen scraping, no memory reading (which is fragile and a ban
risk). The app just records what Claude decides — its tool calls *are* the tracking — plus any
manual corrections. The decider and the recorder are the same brain, so state is a side effect
of the conversation.

---

## 2. Goals & non-goals

**Goals**
- Hands-light: talk to it while playing; it speaks back.
- Claude owns the build decisions and records them via tools.
- A trustworthy, inspectable record (the decision ledger) you can export and share.
- Our own brain (Anthropic API) for newest model, our key, cost control, and full control of
  the speech-input experience.

**Non-goals**
- Reading game state from the client (memory/OCR) — deliberately avoided.
- Being a theory-optimal build calculator. Decisive and coherent beats perfect-but-unusable.
- A managed ElevenLabs agent. We use ElevenLabs for voice I/O only.

---

## 3. Architecture

```
You (voice) ─mic→ WPF app ─text→ Claude brain ─tool calls→ CharacterService → SQLite + ledger
     ▲                │                                            │
     └──── speaker ◄──┴──────── reply text ◄── brain ◄─────────────┘ (UI re-syncs on Changed)
```

**Projects**

| Project | Role | Notable deps |
|---|---|---|
| `D2Companion.Core` | Domain model, SQLite store, decision ledger, `CharacterService`, export/import | Microsoft.Data.Sqlite |
| `D2Companion.Brain` | Anthropic tool-loop brain, D2 tool schema, reference/knowledge subsystem | Anthropic SDK |
| `D2Companion.Voice` | Mic capture, ElevenLabs STT/TTS, playback, transcript cleaning | NAudio (`net10.0-windows`) |
| `D2Companion.App` | WPF UI (MVVM), composition root, config/secrets | CommunityToolkit.Mvvm, Extensions.Configuration |
| `D2Companion.Harness` | Headless console: live text chat + offline sim; the no-GUI test seam | Extensions.Configuration |
| `D2Companion.Tests` | xUnit (`net10.0-windows` to reference Voice) | xUnit |

Solution is `.slnx`. Targets `net10.0` / `net10.0-windows`.

**Key seam:** `CharacterService` is the *one* place the character mutates. Both Claude's tool
calls and manual UI edits go through it; each change persists, appends a `LedgerEntry`
(`Claude` | `Manual` source + rationale), and raises `Changed`. This unifies "Claude decides"
and "hand-correct" into one audited pipeline, and the UI simply re-syncs on `Changed`.

---

## 4. Domain model

`Character` is a single aggregate (stored as one JSON row): name, class, mode, patch/season,
build goal, difficulty, act, level, `AttributePoints`, `List<SkillAllocation>`,
`List<GearItem>`, `List<string>` reminders. Class/mode start `Unset` — Claude chooses them.

`LedgerEntry` (stored as rows for query/history): id, timestamp, source, action, details,
rationale. Append-only.

---

## 5. The brain

- **Manual tool loop** (not the SDK auto-runner) so we can gate/log/throttle every call.
- **Model:** Opus 4.8 (`claude-opus-4-8`); adaptive thinking + effort `low` for snappy voice.
- **Prompt caching:** the stable system prompt is cached, so each turn pays only for the new
  message + reply. Tools render before system and cache with it.
- **Tool schema (`D2Tools`):** `get_character_state`, `set_name/class/mode/build_goal/level/
  progress/attributes`, `assign_skill_point`, `set_skill`, `note_gear`, `add_reminder`,
  `lookup_reference`. Each mutation tool takes an optional `rationale` that lands in the ledger.
- **System prompt:** D2 dungeon-master persona — *you* decide the build, record decisions via
  tools, keep spoken replies short, use your own knowledge first and `lookup_reference` only to
  verify.
- **Cost control levers (designed in):** prompt caching (biggest), effort dial, and the option
  to route cheap utility calls to Haiku 4.5 later.

---

## 6. Voice pipeline

ElevenLabs is voice I/O only: **STT** (`scribe_v1`) in, **TTS** (`eleven_turbo_v2_5`) out.

**Speech-input handling ("the limits"):** gap tolerance, filler-word filtering, and a
min-content gate so an "uh" or throat-clear isn't a turn.

**Two modes** (mirrors the ShelfAware voice A/B):
- **A — Push-to-talk (shipped):** hold the button; mic live only while held. Trivial with game
  audio blasting — no silence to endpoint, minimal bleed. `TranscriptCleaner` does the light
  filler/min-content pass.
- **B — Open-mic / hands-free (planned):** continuous capture with voice-activity detection
  (energy-based first, Silero-via-ONNX as an upgrade) and silence-duration endpointing so it
  waits through mid-thought pauses. This is where the gap-tolerance knobs earn their keep, and
  where game-audio robustness matters most.

One voice turn: capture → STT → clean → brain → TTS → play. Claude's tool calls update the
character mid-turn; the sheet re-syncs via `Changed`.

---

## 7. Reference / knowledge subsystem

Claude leans on its own strong D2 knowledge first; seed sites are for orientation/verification
when a call should be solid (breakpoints, current-patch specifics) — keeping reference tokens
out of every turn.

- **Seed sites:** an editable list; default **Icy Veins — Diablo II** (`icy-veins.com/d2`).
  Stored in `reference-sites.json` under `%LocalAppData%\D2Companion` (created with defaults on
  first run, so it's editable). A settings window will make this point-and-click later.
- **`lookup_reference(topic)`:** the brain fetches the seed page(s) (cached), strips HTML to
  readable text, scores paragraphs against the topic keywords, and returns the most relevant
  slice. If no reference library is configured, the tool degrades to a "rely on your own
  knowledge" note.

---

## 8. Persistence & data management

- **SQLite:** character as one JSON row; ledger as rows. `RETURNING id` for inserts.
- **Export:** JSON (complete `CharacterExport` = character + full ledger, re-importable) and a
  readable **Markdown build log** (oldest-first with rationales).
- **Import:** load a `CharacterExport` JSON back in (replaces current; auto-backs-up first).
- **New character (reset):** auto-backup → clear character + ledger → log a fresh start. If the
  backup fails it asks before wiping — never silent data loss.
- **Backups:** timestamped JSON snapshots under `%LocalAppData%\D2Companion\backups`.

---

## 9. Configuration & secrets

- **Secrets** resolve from dotnet user-secrets → `secrets.local.json` → environment variables.
  App and harness share the store (`UserSecretsId d2companion-app-secrets`). Keys:
  `Anthropic:ApiKey`, `ElevenLabs:ApiKey`, `ElevenLabs:VoiceId` (a **Voice ID**, not an
  `agent_…` id).
- **Graceful degradation:** no Anthropic key → manual-only sheet; no ElevenLabs key → brain
  without voice. The app always runs.

---

## 10. UI (WPF)

- **Character sheet** (editable) + **skills/gear/reminders** editors + **live decision ledger**.
- **Toolbar:** New character, Export…, Import…, Settings….
- **Voice panel:** push-to-talk button *or* open-mic Start/Stop (by mode), status line,
  last-heard / last-reply.
- **Settings window:** voice mode + VAD thresholds, TTS model, effort, and a seed-site editor.
- **Window placement is remembered** across runs (handy on a second monitor), validated
  against currently-connected screens so it never opens off-screen.
- **Overlay: dropped.** An always-on-top overlay is redundant with a second monitor (the app
  window just lives there) and a voice-first app rarely needs eyes on it mid-fight. If ever
  revisited it must be a plain top-most window (no game hooking) to stay EULA-safe.

---

## 11. Testing strategy

- Core pipeline (service, store round-trip, ledger, reset/export/import) — unit-tested.
- Tool schema serialization — regression-tested (guards the static-init class of bug).
- Transcript cleaning and HTML/relevance extraction — unit-tested with fixed inputs (no network).
- Live paths (Anthropic calls, ElevenLabs, mic/playback) — verified by running with real keys;
  the harness is the no-GUI seam for the brain.

---

## 12. Roadmap

**Shipped**
- Phase 0 — solution, domain + SQLite + ledger, editable WPF sheet, harness, tests.
- Phase 1 — the brain (tool loop) + ElevenLabs voice + push-to-talk (voice confirmed working).
- Data management — reset, export (JSON + Markdown), JSON import, backups.
- Reference/knowledge subsystem wired to icy-veins (editable seed list).
- Open-mic hands-free mode (VAD + endpointing) + settings window.
- Window placement remembered across runs.

**Future (optional polish)**
- Multiple saved characters (switch between runs).
- Ledger-based undo.
- Optional: route cheap utility calls to Haiku; Silero VAD; richer retrieval.

**Decided against**
- Always-on-top overlay — redundant with a second monitor and voice-first use; would only
  matter for single-monitor fullscreen, and any revisit must use a non-injected top-most
  window to stay EULA-safe.
