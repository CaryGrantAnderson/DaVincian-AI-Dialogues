# DVMS AI Dialogue Studio 1.0 — Recording authoring and live capture

Windows desktop application using Grant's DVMS Desktop Framework, .NET Framework **4.7.2**, WinForms, DevExpress **25.1.13**, Dapper, SQLite, and embedded Microsoft WebView2. This build implements Recording authoring and live two-speaker capture. The complete Version 1.0 production feature set is still in progress. The original design documents are unchanged.

## Run and navigate

Double-click **Start Studio.cmd**. It launches **Build/Release/DVMS.AIDialogueStudio.exe**.

The navigation root is **Dialogues**. Each Dialogue contains Recordings; **Unassigned Recordings** is the destination for those not yet organized. Each container has its own Dialogue.sqlite, Dialogue.md index, and Recordings folder under AppData/Dialogues. Selecting a Dialogue filters the library to its members. Each Recording appears by its title, with its topics and nested topics beneath it. Selecting a topic opens its Recording and filters the dialogue. Select **All passages** to clear the filter. Each Recording has its own editor tab; the root opens the full Recording library. The former generic Tools nodes are replaced by the Recording catalog. A legacy navigation snapshot is preserved before this migration.

1. Choose **New Dialogue** to create a container, or **New Recording** to choose an existing/new/unassigned Dialogue and enter the Recording title. New Recording folders are created under the selected container. **Open Recording** can register an existing Recording.sqlite and offers a destination when first added. **Move to Dialogue** changes membership without relocating files. Existing catalog entries migrate to Unassigned Recordings.
2. **Import source** offers a Dialogue destination and adds Markdown, plain text, RTF, or an audio file (WAV, MP3, M4A, FLAC, OGG, WebM). Imported audio is copied unchanged to Media/Imported and registered with a SHA-256 hash; automatic transcription is not implemented. Multiple imports and live contributions can belong to one Recording. Original text files are copied unchanged to Sources. RTF is extracted as text while its original formatting remains in the source file.
3. Edit a passage's text or speaker; its original remains read-only. **Add exchange** creates a linked Grant/Nex exchange. Move whole exchanges with the arrow buttons. Add and assign topics to organize them.
4. **Save revision** or Ctrl+S creates a new snapshot. Restoring history loads an older snapshot as a draft; saving creates a new revision without deleting intervening history. Stale concurrent saves are rejected.
5. **Export Markdown** writes a uniquely named export to the Recording's Exports folder.

## Live Recording with the saved Nex voice

1. Open the target Recording and choose **Live Recording**, then choose its Dialogue destination (current Dialogue preselected). A new Dialogue or Unassigned Recordings can also be chosen. This assigns the whole Recording, including its earlier contributions. From the library, Live Recording first creates a titled Recording and then continues to capture.
2. Open **Settings → OpenAI connection** to save a project API key and the Nex voice ID (`voice_6ad55cb9d048819194018d2e18af182c`). Choose **Save** or **Verify key & voice**, then Close. Windows DPAPI encrypts the key for your Windows user and stores only the protected blob in AppData/Secrets/OpenAI.key.dpapi. The app reuses it across runs. It never places the key in a Recording or embedded browser. A missing/unreadable key prompts you to open these settings; there are no per-page API key fields. **Remove saved key** clears it. Environment keys are no longer used automatically.
3. Choose **Start live Recording**, allow microphone access, and speak. Nex plays through the selected Windows audio output. Headphones help prevent speaker audio entering the microphone. Calls use API billing.
4. Watch the red recording indicator, live microphone-level meter, HH:MM:SS elapsed clock, and activity strip. Time is measured from local capture start, including connection setup. There is no fixed duration or percentage complete. Use **Mute microphone** as needed. Choose **End & save** and wait for completion. Closing during capture requests the same orderly finish.
5. Close the live window. The new transcript contribution appears in the Recording editor. Use **Captured audio & timing** to inspect saved sessions, track paths, and caption timing or open the media folder, which contains original live tracks and imported audio. Reopen Live Recording to add another contribution.

The native application brokers the Live API WebRTC connection using the saved custom voice. The embedded browser captures the local microphone and received Nex audio separately through two AudioWorklet streams sharing one local sample clock. Each is stored as a mono PCM16 WAV. Shorter tracks are padded with silence to retain aligned lengths. Tracks are never mixed together. The captured microphone stream uses the browser's echo cancellation/noise suppression; it is not an untouched hardware feed.

The saved Nex prompt is loaded from **Nex - Live Playground Prompt.md** in the parent DaVincian AI Dialogues folder. The latest selected Recording text is supplied as bounded historical context, up to 5,000 characters and 6,000 UTF-8 bytes. Full-archive retrieval is not implemented in this desktop live connection. The prompt explicitly describes these capability limits.

## Storage and preservation

Exactly one application DB, **AppData/DBs/Framework.db**, holds settings, navigation, workspace state, and the Recording catalog. Each Dialogue, including Unassigned Recordings, additionally has a separate SQLite container database and readable file index. The app catalog owns membership; these container indexes are refreshed on updates/startup. Existing external Recording folders remain linked in place. Back up application data for container membership plus every Recording folder, including any externally linked folders. This build reopens portable Recordings; standalone Dialogue database import is not yet available. Each Recording has its own portable folder:

```text
Recording title - unique suffix/
    Recording.sqlite
    Sources/
        <live-session-id>.jsonl transcript journal
    Media/
        Original/
            <live-session-id>-grant.wav
            <live-session-id>-nex.wav
        Generated/
        Imported/
    Exports/
```

Actual journal filenames use the `live-` prefix. Back up the entire Recording folder, including Sources and Media. Relative asset paths and stable Recording IDs support moving the folder and reopening its database. A title change does not rename the folder automatically.

Schema v2 upgrades v1 Recordings transactionally. It adds live sessions, original caption events, audio block sequence/frame metadata, track assets, and utterance timing. Live transcripts become immutable source utterances plus a new editable revision. Existing source text, revisions, topics, and imported files are retained. Original imported bytes have SHA-256 provenance.

**Timing distinction:** Grant and Nex WAVs share an exact local sample timeline. Provider transcript start/end times are retained independently. Their mapping to local audio uses an estimated offset measured when session.started is received, and is explicitly labeled as an estimate. Network and playout latency prevent claiming word-perfect synchronization. Edited dialogue order is independent of original capture time.

Audio headers and transcript journals are flushed during capture. When a Recording is reopened, abandoned captures recover retained WAV prefixes and complete journal events into an Interrupted contribution. Recovery repairs WAV length headers and is idempotent. An active capture lock prevents recovery from touching an ongoing Recording. An interrupted session does not claim confirmed final provider usage. Recovery can preserve durable prefixes, not audio still in browser/OS buffers at a crash.

## Verification and remaining work

Run **Build Studio.ps1 -Test** to build Release and run both isolated authoring/navigation and live capture suites. Test evidence is under Build/Release/TestEvidence. The live suite uses two different synthetic tones through the actual embedded WebView2 AudioWorklet/native bridge, without microphone access or API calls. It checks separate aligned WAVs, caption/timing persistence, schema migration, retries, gaps, crash recovery, and SQLite integrity. Framework regression checks also passed after its optional navigation/container extensions.

A real microphone conversation and saved custom-voice playback still require human acceptance with a valid project key. Automated synthetic success does not verify provider access, current account eligibility, network behavior, or the audible Nex voice.

Still pending: integrated audio playback/editing, clip replacement/regeneration, podcast mixing and rendered exports, Word/MHTML import, research integration, full-archive retrieval, publication player, video production, installer/distribution. Media/presentation/timeline contracts provide a path to Version 2; reserved tables alone do not implement those features. The separate **Nex Voice App** is unchanged; import desired Markdown transcripts explicitly.

