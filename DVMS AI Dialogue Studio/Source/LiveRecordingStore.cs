using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Dapper;

namespace DVMS.AIDialogueStudio
{
    public sealed class LiveSessionInfo
    {
        public string Id { get; set; }
        public string Status { get; set; }
        public string VoiceId { get; set; }
        public int SampleRate { get; set; }
        public double? ProviderOffsetMs { get; set; }
        public string ProviderSessionId { get; set; }
        public string StartedUtc { get; set; }
        public string EndedUtc { get; set; }
        public string ContributionId { get; set; }
        public string Error { get; set; }
    }
    public sealed class LiveCaption
    {
        public string EventId { get; set; }
        public string Speaker { get; set; }
        public string Delta { get; set; }
        public double StartMs { get; set; }
        public double EndMs { get; set; }
        public double ReceivedCaptureMs { get; set; }
    }
    public sealed partial class RecordingRepository
    {
        internal void EnsureLiveSchema()
        {
            using (var db = Open())
            using (var tx = db.BeginTransaction())
            {
                if (db.ExecuteScalar<int>("PRAGMA user_version", transaction: tx) == 1)
                    db.Execute(@"CREATE TABLE LiveSessions (Id TEXT PRIMARY KEY, Status TEXT NOT NULL, VoiceId TEXT NOT NULL, SampleRate INTEGER NOT NULL, ProviderOffsetMs REAL, ProviderSessionId TEXT, StartedUtc TEXT NOT NULL, EndedUtc TEXT, ContributionId TEXT REFERENCES Contributions(Id), Error TEXT);
                        CREATE TABLE LiveCaptionEvents (SessionId TEXT NOT NULL REFERENCES LiveSessions(Id), EventId TEXT NOT NULL, Speaker TEXT NOT NULL, Delta TEXT NOT NULL, StartMs REAL NOT NULL, EndMs REAL NOT NULL, ReceivedCaptureMs REAL NOT NULL, PRIMARY KEY(SessionId,EventId));
                        CREATE TABLE LiveAudioBlocks (SessionId TEXT NOT NULL REFERENCES LiveSessions(Id), Speaker TEXT NOT NULL, Sequence INTEGER NOT NULL, StartFrame INTEGER NOT NULL, FrameCount INTEGER NOT NULL, PRIMARY KEY(SessionId,Speaker,Sequence));
                        CREATE TABLE LiveAudioTracks (AssetId TEXT PRIMARY KEY REFERENCES MediaAssets(Id), SessionId TEXT NOT NULL REFERENCES LiveSessions(Id), Speaker TEXT NOT NULL, SampleRate INTEGER NOT NULL, Channels INTEGER NOT NULL, BitsPerSample INTEGER NOT NULL, StartCaptureFrame INTEGER NOT NULL, FrameCount INTEGER NOT NULL);
                        CREATE TABLE UtteranceTimings (UtteranceId TEXT PRIMARY KEY REFERENCES Utterances(Id), SessionId TEXT NOT NULL REFERENCES LiveSessions(Id), ProviderStartMs REAL NOT NULL, ProviderEndMs REAL NOT NULL, EstimatedCaptureStartMs REAL, EstimatedCaptureEndMs REAL, AlignmentMethod TEXT NOT NULL);
                        PRAGMA user_version=2;", transaction: tx);
                tx.Commit();
            }
        }
        public List<LiveSessionInfo> LiveSessions() { using (var db = Open()) return db.Query<LiveSessionInfo>("SELECT * FROM LiveSessions ORDER BY StartedUtc").ToList(); }
        public List<LiveCaption> Captions(string id) { using (var db = Open()) return db.Query<LiveCaption>("SELECT EventId,Speaker,Delta,StartMs,EndMs,ReceivedCaptureMs FROM LiveCaptionEvents WHERE SessionId=@id ORDER BY rowid", new { id }).ToList(); }
        internal string JournalPath(string id) { return Path.Combine(Folder, "Sources", "live-" + id + ".jsonl"); }
        internal string AudioPath(string id, string speaker) { return Path.Combine(Folder, "Media", "Original", id + "-" + speaker.ToLowerInvariant() + ".wav"); }
        internal static string HashFile(string path) { using (var sha = SHA256.Create()) using (var input = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant(); }
        internal void FinishLive(string id, string status, string error)
        {
            var captions = Captions(id);
            // Group captions independently for each speaker, retaining original deltas
            // and their provider clock. Simultaneous speech is not forced into alternation.
            var groups = new List<CaptionGroup>();
            foreach (var c in captions)
            {
                var group = groups.LastOrDefault(g => g.Speaker == c.Speaker);
                if (group == null || c.StartMs - group.End > 1800 || c.StartMs < group.Start)
                { group = new CaptionGroup { Speaker = c.Speaker, Start = c.StartMs, End = c.EndMs }; groups.Add(group); }
                group.Text.Append(c.Delta); group.End = Math.Max(group.End, c.EndMs);
            }
            groups = groups.OrderBy(g => g.Start).ToList();
            using (var db = Open())
            using (var tx = db.BeginTransaction())
            {
                var session = db.QuerySingle<LiveSessionInfo>("SELECT * FROM LiveSessions WHERE Id=@id", new { id }, tx);
                if (session.Status != "Capturing") return;
                var recording = db.QuerySingle<Recording>("SELECT Id,Title,Generation,CurrentRevision AS RevisionId FROM Recording", transaction: tx);
                var revision = Guid.NewGuid().ToString("N"); var contribution = Guid.NewGuid().ToString("N");
                string journal = "Sources/live-" + id + ".jsonl";
                string originalText = string.Join("\n\n", groups.Select(g => g.Speaker + " [provider " + g.Start.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms]:\n" + g.Text));
                db.Execute("INSERT INTO Contributions VALUES (@contribution,'Live','JSONL',@journal,@hash,@originalText,@Now)", new { contribution, journal, hash = HashFile(JournalPath(id)), originalText, Now = session.StartedUtc }, tx);
                db.Execute("INSERT INTO Revisions VALUES (@revision,@Number,@Now,@Note)", new { revision, Number = recording.Generation + 1, Now = DateTime.UtcNow.ToString("O"), Note = "Live capture · " + status }, tx);
                db.Execute("INSERT INTO RevisionItems SELECT @revision,Id,UtteranceId,Speaker,Text,TopicId,Position FROM RevisionItems WHERE RevisionId=@old", new { revision, old = recording.RevisionId }, tx);
                db.Execute("INSERT INTO RevisionTopics SELECT @revision,Id,ParentId,Title,Position FROM RevisionTopics WHERE RevisionId=@old", new { revision, old = recording.RevisionId }, tx);
                int pos = db.ExecuteScalar<int>("SELECT COUNT(*) FROM RevisionItems WHERE RevisionId=@revision", new { revision }, tx);
                string exchange = Guid.NewGuid().ToString("N");
                foreach (var group in groups)
                {
                    if (group.Speaker == "Grant") exchange = Guid.NewGuid().ToString("N");
                    var utterance = Guid.NewGuid().ToString("N");
                    db.Execute("INSERT OR IGNORE INTO Exchanges VALUES (@exchange)", new { exchange }, tx);
                    db.Execute("INSERT INTO Utterances VALUES (@utterance,@contribution,@exchange,@Speaker,@Text)", new { utterance, contribution, exchange, group.Speaker, Text = group.Text.ToString() }, tx);
                    db.Execute("INSERT INTO RevisionItems VALUES (@revision,@utterance,@utterance,@Speaker,@Text,NULL,@pos)", new { revision, utterance, group.Speaker, Text = group.Text.ToString(), pos = pos++ }, tx);
                    db.Execute("INSERT INTO UtteranceTimings VALUES (@utterance,@id,@Start,@End,@CaptureStart,@CaptureEnd,@Method)", new { utterance, id, group.Start, group.End, CaptureStart = session.ProviderOffsetMs + group.Start, CaptureEnd = session.ProviderOffsetMs + group.End, Method = "Estimated from session.started reception on local audio clock; network delay is not measured" }, tx);
                }
                foreach (var speaker in new[] { "Grant", "Nex" })
                {
                    string path = AudioPath(id, speaker); if (!File.Exists(path)) continue;
                    long frames = Math.Max(0, (new FileInfo(path).Length - 44) / 2); string asset = Guid.NewGuid().ToString("N"); string relative = "Media/Original/" + Path.GetFileName(path);
                    db.Execute("INSERT INTO MediaAssets VALUES (@asset,'Audio',@relative,@hash,@duration,NULL,NULL)", new { asset, relative, hash = HashFile(path), duration = frames * 1000 / session.SampleRate }, tx);
                    db.Execute("INSERT INTO LiveAudioTracks VALUES (@asset,@id,@speaker,@SampleRate,1,16,0,@frames)", new { asset, id, speaker, session.SampleRate, frames }, tx);
                }
                db.Execute("UPDATE Recording SET Generation=Generation+1,CurrentRevision=@revision WHERE Id=@Id", new { revision, recording.Id }, tx);
                db.Execute("UPDATE LiveSessions SET Status=@status,EndedUtc=@Now,ContributionId=@contribution,Error=@error WHERE Id=@id", new { id, status, Now = DateTime.UtcNow.ToString("O"), contribution, error }, tx);
                tx.Commit();
            }
        }
        public int RecoverInterruptedCaptures()
        {
            var pending = LiveSessions().Where(s => s.Status == "Capturing").ToList(); if (pending.Count == 0) return 0;
            FileStream guard;
            try { guard = new FileStream(Path.Combine(Folder, "Sources", "live-capture.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { return 0; }
            using (guard) foreach (var session in pending)
            {
                foreach (var speaker in new[] { "Grant", "Nex" }) { var path = AudioPath(session.Id, speaker); if (File.Exists(path)) PcmWave.RecoverHeader(path); }
                if (File.Exists(JournalPath(session.Id)))
                {
                    using (var db = Open()) foreach (var line in File.ReadLines(JournalPath(session.Id)))
                    {
                        LiveCaption c;
                        try { c = new JavaScriptSerializer().Deserialize<LiveCaption>(line); } catch (ArgumentException) { continue; } catch (InvalidOperationException) { continue; }
                        if (c == null || (c.Speaker != "Grant" && c.Speaker != "Nex") || string.IsNullOrWhiteSpace(c.EventId) || c.Delta == null || c.StartMs < 0 || c.EndMs < c.StartMs || double.IsNaN(c.EndMs) || double.IsInfinity(c.EndMs)) continue;
                        db.Execute("INSERT OR IGNORE INTO LiveCaptionEvents VALUES (@id,@EventId,@Speaker,@Delta,@StartMs,@EndMs,@ReceivedCaptureMs)", new { id = session.Id, c.EventId, c.Speaker, c.Delta, c.StartMs, c.EndMs, c.ReceivedCaptureMs });
                    }
                }
                FinishLive(session.Id, "Interrupted", "Recovered retained audio and complete journal events after an interrupted capture. Any incomplete journal line remains in the source file. Final provider usage is unconfirmed.");
            }
            return pending.Count;
        }
        sealed class CaptionGroup { public string Speaker; public double Start, End; public StringBuilder Text = new StringBuilder(); }
    }
    public sealed class LiveRecordingCapture : IDisposable
    {
        readonly RecordingRepository repo;
        readonly FileStream guard;
        readonly StreamWriter journal;
        readonly Dictionary<string, PcmWave> tracks = new Dictionary<string, PcmWave>();
        readonly Dictionary<string, int> sequences = new Dictionary<string, int> { { "Grant", 0 }, { "Nex", 0 } };
        public string Id { get; private set; }
        public bool Finished { get; private set; }
        public long AudioFrames { get { return tracks.Count == 0 ? 0 : tracks.Values.Max(t => t.Frames); } }
        public LiveRecordingCapture(RecordingRepository repo, string voice, int sampleRate)
        {
            this.repo = repo;
            guard = new FileStream(Path.Combine(repo.Folder, "Sources", "live-capture.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            Id = Guid.NewGuid().ToString("N");
            try
            {
                foreach (var speaker in sequences.Keys) tracks[speaker] = new PcmWave(repo.AudioPath(Id, speaker), sampleRate);
                journal = new StreamWriter(new FileStream(repo.JournalPath(Id), FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
                using (var db = repo.Open()) db.Execute("INSERT INTO LiveSessions VALUES (@Id,'Capturing',@voice,@sampleRate,NULL,NULL,@Now,NULL,NULL,NULL)", new { Id, voice, sampleRate, Now = DateTime.UtcNow.ToString("O") });
            }
            catch { foreach (var track in tracks.Values) track.Dispose(); guard.Dispose(); throw; }
        }
        public void ProviderStarted(string providerId, double localAudioMs)
        {
            if (!double.IsNaN(localAudioMs) && !double.IsInfinity(localAudioMs) && localAudioMs >= 0)
                using (var db = repo.Open()) db.Execute("UPDATE LiveSessions SET ProviderSessionId=@providerId,ProviderOffsetMs=@localAudioMs WHERE Id=@Id", new { providerId, localAudioMs, Id });
        }
        public void Audio(string speaker, int sequence, long startFrame, byte[] pcm)
        {
            if (Finished || !tracks.ContainsKey(speaker)) throw new InvalidOperationException("No active speaker track.");
            if (sequence < sequences[speaker]) return; // Delivery retry.
            if (sequence != sequences[speaker]) throw new InvalidDataException("A captured audio block is missing. Capture has been stopped to preserve the durable prefix.");
            tracks[speaker].Append(startFrame, pcm);
            using (var db = repo.Open()) db.Execute("INSERT INTO LiveAudioBlocks VALUES (@Id,@speaker,@sequence,@startFrame,@frames)", new { Id, speaker, sequence, startFrame, frames = pcm.Length / 2 });
            sequences[speaker]++;
        }
        public void Caption(LiveCaption caption)
        {
            if (Finished || (caption.Speaker != "Grant" && caption.Speaker != "Nex") || string.IsNullOrWhiteSpace(caption.EventId) || caption.Delta == null || caption.Delta.Length > 100000 || !Finite(caption.StartMs) || !Finite(caption.EndMs) || !Finite(caption.ReceivedCaptureMs) || caption.EndMs < caption.StartMs) throw new InvalidDataException("Invalid live caption event.");
            using (var db = repo.Open())
            {
                if (db.ExecuteScalar<int>("SELECT COUNT(*) FROM LiveCaptionEvents WHERE SessionId=@Id AND EventId=@EventId", new { Id, caption.EventId }) != 0) return;
                journal.WriteLine(new JavaScriptSerializer().Serialize(caption)); journal.Flush(); ((FileStream)journal.BaseStream).Flush(true);
                db.Execute("INSERT INTO LiveCaptionEvents VALUES (@Id,@EventId,@Speaker,@Delta,@StartMs,@EndMs,@ReceivedCaptureMs)", new { Id, caption.EventId, caption.Speaker, caption.Delta, caption.StartMs, caption.EndMs, caption.ReceivedCaptureMs });
            }
        }
        static bool Finite(double value) { return value >= 0 && !double.IsInfinity(value) && !double.IsNaN(value); }
        public void Complete(bool confirmed, string error = null)
        {
            if (Finished) return;
            long end = AudioFrames; foreach (var track in tracks.Values) { track.PadTo(end); track.Dispose(); } tracks.Clear(); journal.Dispose();
            try { repo.FinishLive(Id, confirmed ? "Completed" : "Interrupted", error); Finished = true; }
            finally { guard.Dispose(); }
        }
        public void Dispose() { if (!Finished) Complete(false, "Capture closed without a confirmed session.closed event."); }
    }
}
