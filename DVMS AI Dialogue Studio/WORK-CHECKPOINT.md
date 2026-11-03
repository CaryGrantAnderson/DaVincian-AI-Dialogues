# DVMS AI Dialogue Studio — implementation checkpoint

Updated October 18, 2026 (local). Dialogue containers, protected settings, and recording indicators build.

## Decisions and implemented behavior

- .NET Framework 4.7.2, WinForms, DevExpress 25.1.13, SQLite, authoritative DVMS Desktop Framework projects referenced in place.
- One application DB, AppData/DBs/Framework.db. Each Recording has a portable Recording.sqlite plus Sources, Media, and Exports.
- Actual framework navigation: Dialogues root, Dialogue containers, title-named Recording workspaces, hierarchical topic children. Unassigned Recordings has its own SQLite database/folder/index. Existing v1 catalog membership migrates there. Destination choice is offered for new/import/live flows; Move to Dialogue changes membership without relocating existing folders. Product commands and captions use Recording terminology. Legacy navigation backed up before migration.
- Existing authoring retained: multiple source imports, immutable originals, linked exchanges, topic assignment/filtering, revisions, conflict rejection, Markdown export.
- Embedded WebView2 live UI, central Settings → OpenAI connection with a Windows DPAPI current-user protected key, saved Nex custom voice, native Live API WebRTC broker, microphone mute, orderly End & save.
- Separate Grant and Nex PCM16 WAVs share a local sample clock. SQLite retains source sample metadata, caption provider times and estimated local alignment, session state, and immutable source contributions with editable revisions.
- Schema v1 to v2 migration is transactional. Durable WAV headers/journal, exclusive capture lock, interrupted-prefix recovery, journal replay, and idempotent contribution finalization are implemented.
- Framework additions are optional: shared DB mode, container-enabled tool nodes, product navigation activation/reload hooks, product captions, registry removal. Existing defaults and constructors retained.

## Verification

Release compiles with VS2022 MSBuild. Authoring/navigation and live suites run with --self-test and --live-self-test. Run Build Studio.ps1 -Test for both. Evidence and rendered fixture UI remain under Build/Release/TestEvidence; stable snapshots/logs are copied under docs.

Live suite: 20 checks, including actual embedded two-tone AudioWorklet capture, distinct aligned tracks, raw/estimated timing, source preservation, caption retry, gaps, lock behavior, interrupted WAV header repair, journal replay, and duplicate-free recovery. Live indicators, elapsed clock, microphone meter, and byte-preserving audio import are also checked. No microphone or API calls in tests.

Authoring/navigation suite: 36 checks, including container migration/index membership, destination UI, DPAPI round-trip/removal, settings UI, and actual 58-message archive import, revisions, source preservation, topics, portability, SQLite integrity, workspace save lifecycle, title-named Recordings root/topic activation. Framework suite passed 138 icon gallery assertions and 23 main framework assertions, plus Designer/infrastructure checks.

## Practical limits and next work

A real microphone/custom-Nex-voice call remains unverified. The key is entered once in menu-accessible native settings and stored as a Windows-protected blob under AppData/Secrets; no plaintext key is persisted. Missing keys prompt for settings. Environment fallback and per-window key fields are removed. The live prompt includes bounded recent Recording text, not full archive retrieval.

The WAV timeline is local and sample-based; caption alignment uses an estimated provider offset. It is not word-perfect audio synchronization. Provider closure is distinguished from interrupted local preservation. Audio buffered at the instant of a crash can be lost despite durable-prefix recovery.

Next: human live-call acceptance, then audio playback/clip editing, rendered presentation timeline/export, additional imports/research, publication package/player and Version 2 visual tracks. Original design documents and existing archived dialogues remain unchanged. The old standalone Nex Voice App remains separate.

Container indexes are regenerated from the app catalog at startup/update. Back up app data plus all Recording folders, including external links; standalone Dialogue DB import is pending. Audio import preserves source bytes without generating a transcript. Original live timing caveats remain unchanged.

