# Nex Voice

A local .NET 10 application for Grant and Nex: a saved custom GPT-Live voice, access to the complete Markdown dialogue archive, and locally preserved conversations.

## Start

1. Double-click **Start Nex.cmd** in this folder, or run `./Start Nex.ps1` from PowerShell.
2. Keep its terminal open. Open **http://localhost:5188** in Chrome or Edge.
3. Under **Connection**, expand **Set or replace API key**. Enter an OpenAI project API key from the organization that owns the saved Nex voice. Click **Verify key & voice**. Do not paste a key into chat or source files. A key can instead be supplied through the server's `OPENAI_API_KEY` environment variable.
4. Create a conversation or choose one to resume. Click **Start voice**, allow microphone access, and speak. **Mute mic** stops sending captured microphone audio; it does not stop session billing. Use **End call** to end the session.
5. At the end, write decisions and the next step in **Continuity notes**, then click **Save continuity notes**. Notes are included at the start of the next call.

The key entered in the app is sent only to the local server and OpenAI for verification. The app holds it in server memory until shutdown; it does not save it to disk, browser storage, transcripts, or return it to the browser. API requests use separate OpenAI API billing.

## Archive and continuity

The configured archive root is the parent **8 - DaVincian AI Dialogues** folder. All Markdown below it is scanned, except technical source/build folders, index files, the app's own documentation, and duplicate content. Relevant-excerpt files are skipped when a full-dialogue file exists beside them. Full originals are never rewritten.

- **Search** finds passages and shows the source title and line range. Select a result to read the entire document as Markdown text. Search is local keyword matching, not semantic/vector search; distinctive terms and alternate wording can improve results.
- Nex's delegated backend receives read-only `search_archive` and `read_archive` functions. It can search the complete indexed archive and page through any full document. The prompt tells it to retrieve prior discussion before claiming a recollection and to preserve subsequent corrections. Whether the model follows these instructions reliably needs live acceptance testing.
- **Refresh archive** loads file changes. The app also refreshes before every voice call, including newly saved conversations.
- The app reloads **Nex - Live Playground Prompt.md** from the archive root at each call. Edit that master file to change personality/background. App-specific instructions add the archive tools and caption-saving behavior.
- Resuming a selected conversation supplies its most recent 7,000 characters as startup history. Older history remains searchable in its complete Markdown transcript. This is retrieval-backed continuity, not a promise that every file is present in the model's active context.
- Global notes in `Data/Continuity.md` are supplied on each new call. Notes are manually curated; this version does not silently generate or promote model suggestions into lasting memory.

## Files saved locally

`Data/Conversations/<conversation-id>.json` stores the title and dates.

`Data/Conversations/<conversation-id>.jsonl` is an append-only journal of received speech transcript fragments. Each includes the provider event ID, connection ID, speaker/event type, timing, and exact received text. Duplicate deliveries are ignored. The journal is flushed to disk before a save is acknowledged.

`Data/Conversations/<conversation-id>.md` is the readable dialogue, updated as captions arrive. **Open Markdown** displays it. Fragments are organized by call and session timing; simultaneous speech and speaker turns are approximate. Captions may contain recognition errors. Audio is not recorded.

The browser temporarily queues unsaved caption fragments in local storage and retries. An interrupted network does not delete acknowledged captions. A browser/device failure can still lose fragments that were never received or durably queued. End the call normally and check the save status before closing the tab.

Back up the **Data** directory and the original archive. No delete function is included.

## Configuration

Edit `appsettings.json`, then restart:

- `Nex:VoiceId`: saved custom voice ID, initially `voice_6ad55cb9d048819194018d2e18af182c`.
- `Nex:VoiceModel`: `gpt-live-1`.
- `Nex:BackendModel`: `gpt-6-luna`, used for reasoning and archive tool calls.
- `Nex:ArchiveRoot`: `..`, relative to this app folder.
- `Nex:Port`: `5188`. Open the corresponding port if you change it.

The app binds only to loopback and checks Host/Origin on its API. It is intended for one trusted local user. Do not expose it to a LAN or public internet without authentication, HTTPS, and stronger session controls. No framework or NuGet packages beyond the installed .NET 10 shared framework are required.

## Verification and present limitation — October 18, 2026

Verified: clean .NET build; JavaScript syntax; archive indexing and search against Grant's real files; full-document opening in Chrome; conversation creation; fixture-based journal persistence, duplicate suppression, resume preservation, notes persistence, paging, original-file preservation, and path rejection; HTTP rejection of cross-site writes.

The API key inherited by the build session returned **HTTP 401** when reading saved voice metadata. Therefore actual GPT-Live audio, custom-voice selection, and model-initiated archive tool continuation are **implemented but not yet verified live**. Use a valid project key through the app's masked field before acceptance testing. A successful metadata check establishes voice access, but GPT-Live/backend model access is checked when starting a call and during delegation. Errors appear in the call status.

Suggested first acceptance conversation: ask, “Nex, search our Review Teachings Architecture dialogue. Explain my correction about the Triune and your response.” Check the source result, hear the correct custom voice, end the call, open its Markdown, restart the app, and resume the same conversation. Confirm that the saved dialogue and manually saved notes return.

## Developer checks

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.runtime'
dotnet build
dotnet run --no-build -- --self-test
node --check wwwroot/app.js
```

Self-tests use isolated fixture directories under `bin` and retain them as evidence. They do not make OpenAI calls or edit the original archive. The tests are in `SelfTests.cs`.

## API references

- [GPT-Live WebRTC session setup](https://developers.openai.com/api/docs/guides/voice-webrtc)
- [Custom voices](https://developers.openai.com/api/docs/guides/custom-voices)
- [History, captions, and lifecycle](https://developers.openai.com/api/docs/guides/live-conversations)
- [Responses delegation and application function results](https://developers.openai.com/api/docs/guides/live-delegation)

Voice uses WebRTC media tracks. The local server creates the session through `POST /v1/live/sessions`; its key never goes into the WebRTC client. Nested Responses function calls are returned using `response.item.create`, then continued with `response.create`. This implementation uses GPT-Live, not the older Realtime-only transport API.
