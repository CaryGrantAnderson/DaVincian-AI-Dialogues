using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Dapper;

namespace DVMS.AIDialogueStudio
{
    public sealed partial class RecordingRepository
    {
        const int ApplicationId = 1146506579;
        public string DatabasePath { get; private set; }
        public string Folder { get { return Path.GetDirectoryName(DatabasePath); } }
        RecordingRepository(string databasePath) { DatabasePath = Path.GetFullPath(databasePath); }
        internal SQLiteConnection Open()
        {
            var db = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false, ForeignKeys = true, FailIfMissing = true }.ToString());
            db.Open(); db.Execute("PRAGMA busy_timeout=5000"); return db;
        }
        public static RecordingRepository Create(string parent, string title)
        {
            if (string.IsNullOrWhiteSpace(title) || title.Length > 200) throw new ArgumentException("Enter a Recording title of 1–200 characters.");
            string name = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
            if (name.Length == 0) name = "Recording";
            string folder = Path.Combine(Path.GetFullPath(parent), name + " - " + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(folder);
            foreach (var child in new[] { "Sources", "Media/Original", "Media/Generated", "Media/Imported", "Exports" }) Directory.CreateDirectory(Path.Combine(folder, child));
            string path = Path.Combine(folder, "Recording.sqlite");
            SQLiteConnection.CreateFile(path);
            var repo = new RecordingRepository(path);
            using (var db = repo.Open())
            using (var tx = db.BeginTransaction())
            {
                db.Execute(@"CREATE TABLE Recording (Id TEXT PRIMARY KEY, Title TEXT NOT NULL, Generation INTEGER NOT NULL, CurrentRevision TEXT, CreatedUtc TEXT NOT NULL);
                    CREATE TABLE Contributions (Id TEXT PRIMARY KEY, Kind TEXT NOT NULL, Format TEXT NOT NULL, RelativePath TEXT, Sha256 TEXT, OriginalText TEXT NOT NULL, CreatedUtc TEXT NOT NULL);
                    CREATE TABLE Exchanges (Id TEXT PRIMARY KEY);
                    CREATE TABLE Utterances (Id TEXT PRIMARY KEY, ContributionId TEXT REFERENCES Contributions(Id), ExchangeId TEXT NOT NULL REFERENCES Exchanges(Id), Speaker TEXT NOT NULL, OriginalText TEXT NOT NULL);
                    CREATE TABLE Revisions (Id TEXT PRIMARY KEY, Number INTEGER NOT NULL UNIQUE, CreatedUtc TEXT NOT NULL, Note TEXT NOT NULL);
                    CREATE TABLE RevisionTopics (RevisionId TEXT NOT NULL REFERENCES Revisions(Id), Id TEXT NOT NULL, ParentId TEXT, Title TEXT NOT NULL, Position INTEGER NOT NULL, PRIMARY KEY(RevisionId,Id));
                    CREATE TABLE RevisionItems (RevisionId TEXT NOT NULL REFERENCES Revisions(Id), Id TEXT NOT NULL, UtteranceId TEXT NOT NULL REFERENCES Utterances(Id), Speaker TEXT NOT NULL, Text TEXT NOT NULL, TopicId TEXT, Position INTEGER NOT NULL, PRIMARY KEY(RevisionId,Id));
                    CREATE TABLE MediaAssets (Id TEXT PRIMARY KEY, Kind TEXT NOT NULL, RelativePath TEXT NOT NULL, Sha256 TEXT NOT NULL, DurationMs INTEGER, SourceUri TEXT, LicenseInfo TEXT);
                    CREATE TABLE Presentations (Id TEXT PRIMARY KEY, RevisionId TEXT NOT NULL REFERENCES Revisions(Id), Title TEXT NOT NULL);
                    CREATE TABLE TimelinePlacements (Id TEXT PRIMARY KEY, PresentationId TEXT NOT NULL REFERENCES Presentations(Id), AssetId TEXT NOT NULL REFERENCES MediaAssets(Id), DialogueItemId TEXT, TrackKind TEXT NOT NULL, StartMs INTEGER NOT NULL, SourceInMs INTEGER NOT NULL, SourceOutMs INTEGER NOT NULL);
                    CREATE TABLE SourceReferences (Id TEXT PRIMARY KEY, UtteranceId TEXT REFERENCES Utterances(Id), Uri TEXT NOT NULL, Title TEXT, RetrievedUtc TEXT, Excerpt TEXT);
                    CREATE TABLE ExportJobs (Id TEXT PRIMARY KEY, RevisionId TEXT NOT NULL REFERENCES Revisions(Id), Format TEXT NOT NULL, RelativePath TEXT, CreatedUtc TEXT NOT NULL);
                    CREATE TRIGGER PreserveUtterances BEFORE UPDATE ON Utterances BEGIN SELECT RAISE(ABORT,'Original utterances are immutable'); END;
                    CREATE TRIGGER PreserveContributions BEFORE UPDATE ON Contributions BEGIN SELECT RAISE(ABORT,'Original contributions are immutable'); END;
                    PRAGMA application_id=1146506579; PRAGMA user_version=1;", transaction: tx);
                db.Execute("INSERT INTO Recording VALUES (@Id,@title,0,NULL,@Now)", new { Id = Guid.NewGuid().ToString("N"), title, Now = DateTime.UtcNow.ToString("O") }, tx);
                tx.Commit();
            }
            repo.EnsureLiveSchema();
            repo.Save(repo.Load(), "Recording created");
            return repo;
        }
        public static RecordingRepository OpenExisting(string databasePath)
        {
            if (!File.Exists(databasePath)) throw new FileNotFoundException("Choose the Recording.sqlite inside a Recording folder.", databasePath);
            var repo = new RecordingRepository(databasePath);
            using (var db = repo.Open())
            {
                if (db.ExecuteScalar<int>("PRAGMA application_id") != ApplicationId) throw new InvalidDataException("This is not a DVMS AI Dialogue Studio Recording database.");
                int version = db.ExecuteScalar<int>("PRAGMA user_version");
                if (version < 1 || version > 2) throw new InvalidDataException("This Recording requires a different schema version. No changes were made.");
            }
            repo.EnsureLiveSchema();
            return repo;
        }
        public Recording Load(string revision = null)
        {
            using (var db = Open())
            {
                var result = db.QuerySingle<Recording>("SELECT Id,Title,Generation,CurrentRevision AS RevisionId FROM Recording");
                result.Folder = Folder;
                string selected = revision ?? result.RevisionId;
                if (selected != null)
                {
                    if (db.ExecuteScalar<int>("SELECT COUNT(*) FROM Revisions WHERE Id=@selected", new { selected }) != 1) throw new InvalidOperationException("Revision not found.");
                    result.Items = db.Query<DialogueItem>(@"SELECT i.Id,i.UtteranceId,u.ExchangeId,i.Speaker,i.Text,i.TopicId,i.Position,u.OriginalText,u.ContributionId AS SourceId FROM RevisionItems i JOIN Utterances u ON u.Id=i.UtteranceId WHERE i.RevisionId=@selected ORDER BY i.Position", new { selected }).ToList();
                    result.Topics = db.Query<Topic>("SELECT Id,ParentId,Title FROM RevisionTopics WHERE RevisionId=@selected ORDER BY Position", new { selected }).ToList();
                }
                return result;
            }
        }
        public void Save(Recording recording, string note)
        {
            Validate(recording);
            var newIds = new Dictionary<DialogueItem, string>();
            string revision = Guid.NewGuid().ToString("N");
            using (var db = Open())
            using (var tx = db.BeginTransaction())
            {
                if (db.Execute("UPDATE Recording SET Title=@Title,Generation=Generation+1,CurrentRevision=@revision WHERE Id=@Id AND Generation=@Generation", new { recording.Title, revision, recording.Id, recording.Generation }, tx) != 1)
                    throw new InvalidOperationException("This Recording changed in another window. Reopen it before saving; your edits have not been overwritten.");
                db.Execute("INSERT INTO Revisions VALUES (@revision,@Number,@Now,@note)", new { revision, Number = recording.Generation + 1, Now = DateTime.UtcNow.ToString("O"), note = note ?? "Edited dialogue" }, tx);
                foreach (var topic in recording.Topics.Select((t, i) => new { Topic = t, Position = i }))
                    db.Execute("INSERT INTO RevisionTopics VALUES (@revision,@Id,@ParentId,@Title,@Position)", new { revision, topic.Topic.Id, topic.Topic.ParentId, topic.Topic.Title, topic.Position }, tx);
                int position = 0;
                foreach (var item in recording.Items)
                {
                    string utterance = item.UtteranceId;
                    if (utterance == null)
                    {
                        utterance = Guid.NewGuid().ToString("N"); newIds[item] = utterance;
                        db.Execute("INSERT OR IGNORE INTO Exchanges VALUES (@ExchangeId)", item, tx);
                        db.Execute("INSERT INTO Utterances VALUES (@utterance,NULL,@ExchangeId,@Speaker,@Text)", new { utterance, item.ExchangeId, item.Speaker, item.Text }, tx);
                    }
                    db.Execute("INSERT INTO RevisionItems VALUES (@revision,@Id,@utterance,@Speaker,@Text,@TopicId,@position)", new { revision, item.Id, utterance, item.Speaker, item.Text, item.TopicId, position = position++ }, tx);
                }
                tx.Commit();
            }
            foreach (var pair in newIds) { pair.Key.UtteranceId = pair.Value; pair.Key.OriginalText = pair.Key.Text; }
            recording.RevisionId = revision; recording.Generation++;
        }
        static void Validate(Recording r)
        {
            if (string.IsNullOrWhiteSpace(r.Title) || r.Title.Length > 200) throw new InvalidOperationException("A Recording needs a title.");
            if (r.Items.Select(i => i.Id).Distinct().Count() != r.Items.Count) throw new InvalidOperationException("Duplicate dialogue item ID.");
            if (r.Topics.Select(t => t.Id).Distinct().Count() != r.Topics.Count) throw new InvalidOperationException("Duplicate topic ID.");
            foreach (var t in r.Topics)
            {
                var visited = new HashSet<string> { t.Id }; string parent = t.ParentId;
                while (parent != null) { if (!visited.Add(parent)) throw new InvalidOperationException("A topic cannot be its own ancestor."); var p = r.Topics.SingleOrDefault(x => x.Id == parent); if (p == null) throw new InvalidOperationException("Missing topic parent."); parent = p.ParentId; }
            }
            if (r.Items.Any(i => string.IsNullOrWhiteSpace(i.Speaker) || i.Text == null || string.IsNullOrEmpty(i.ExchangeId) || (i.TopicId != null && !r.Topics.Any(t => t.Id == i.TopicId)))) throw new InvalidOperationException("Every passage needs a speaker, text, exchange, and valid topic reference.");
        }
        public void Import(string sourceFile, string text)
        {
            var recording = Load();
            string contribution = Guid.NewGuid().ToString("N");
            var bytes = File.ReadAllBytes(sourceFile);
            string extension = Path.GetExtension(sourceFile).ToLowerInvariant();
            string relative = "Sources/" + contribution + extension;
            string destination = Path.Combine(Folder, relative.Replace('/', Path.DirectorySeparatorChar));
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write)) { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
            string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            var items = DialogueImporter.Parse(text);
            using (var db = Open())
            using (var tx = db.BeginTransaction())
            {
                db.Execute("INSERT INTO Contributions VALUES (@contribution,'Imported',@extension,@relative,@hash,@text,@Now)", new { contribution, extension, relative, hash, text, Now = DateTime.UtcNow.ToString("O") }, tx);
                foreach (var item in items)
                {
                    item.SourceId = contribution; item.UtteranceId = Guid.NewGuid().ToString("N");
                    db.Execute("INSERT OR IGNORE INTO Exchanges VALUES (@ExchangeId)", item, tx);
                    db.Execute("INSERT INTO Utterances VALUES (@UtteranceId,@SourceId,@ExchangeId,@Speaker,@OriginalText)", item, tx);
                }
                // Import is a single database transaction including its first revision.
                string revision = Guid.NewGuid().ToString("N");
                if (db.Execute("UPDATE Recording SET Generation=Generation+1,CurrentRevision=@revision WHERE Generation=@Generation", new { revision, recording.Generation }, tx) != 1) throw new InvalidOperationException("Recording changed during import. Reopen and try again.");
                db.Execute("INSERT INTO Revisions VALUES (@revision,@Number,@Now,@Note)", new { revision, Number = recording.Generation + 1, Now = DateTime.UtcNow.ToString("O"), Note = "Imported " + Path.GetFileName(sourceFile) }, tx);
                foreach (var topic in recording.Topics.Select((t, i) => new { Topic = t, Position = i })) db.Execute("INSERT INTO RevisionTopics VALUES (@revision,@Id,@ParentId,@Title,@Position)", new { revision, topic.Topic.Id, topic.Topic.ParentId, topic.Topic.Title, topic.Position }, tx);
                int pos = 0;
                foreach (var item in recording.Items.Concat(items)) db.Execute("INSERT INTO RevisionItems VALUES (@revision,@Id,@UtteranceId,@Speaker,@Text,@TopicId,@pos)", new { revision, item.Id, item.UtteranceId, item.Speaker, item.Text, item.TopicId, pos = pos++ }, tx);
                tx.Commit();
            }
        }
        public List<RevisionInfo> Revisions() { using (var db = Open()) return db.Query<RevisionInfo>("SELECT * FROM Revisions ORDER BY Number DESC").ToList(); }
        public string ExportText(Recording recording)
        {
            var b = new StringBuilder("# " + recording.Title + "\n\nRecording ID: " + recording.Id + "\n\nEdited dialogue revision: " + recording.Generation + "\n\n");
            int i = 0;
            foreach (var item in recording.Items) b.Append("---\n\n## " + (++i) + ". " + item.Speaker + "\n\n" + item.Text + "\n\n");
            return b.ToString();
        }
        public string ExportMarkdown(Recording recording)
        {
            if (recording.RevisionId == null) throw new InvalidOperationException("Save this Recording first.");
            string id = Guid.NewGuid().ToString("N"); string relative = "Exports/Dialogue-r" + recording.Generation + "-" + id.Substring(0, 8) + ".md";
            string path = Path.Combine(Folder, relative.Replace('/', Path.DirectorySeparatorChar));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(ExportText(recording));
            using (var db = Open()) db.Execute("INSERT INTO ExportJobs VALUES (@id,@RevisionId,'Markdown',@relative,@Now)", new { id, recording.RevisionId, relative, Now = DateTime.UtcNow.ToString("O") });
            return path;
        }
    }
}
