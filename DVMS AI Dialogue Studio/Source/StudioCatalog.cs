using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Dapper;
using DVMSDesktop.Framework;

namespace DVMS.AIDialogueStudio
{
    public sealed class DialogueContainer
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string DatabasePath { get; set; }
        public override string ToString() { return Title; }
    }
    public sealed class StudioCatalog
    {
        public const string UnassignedId = "unassigned";
        public string DatabasePath { get; private set; }
        public event EventHandler Changed;
        public StudioCatalog(FrameworkStore store)
        {
            DatabasePath = store.DatabasePath;
            store.ApplyMigrations("dialogue-studio", new[] {
                new DatabaseMigration { Version = 1, Sql = "CREATE TABLE StudioRecordings (RecordingId TEXT PRIMARY KEY, Title TEXT NOT NULL, DatabasePath TEXT NOT NULL, OpenedUtc TEXT NOT NULL);" },
                new DatabaseMigration { Version = 2, Sql = "CREATE TABLE StudioDialogues (Id TEXT PRIMARY KEY, Title TEXT NOT NULL, DatabasePath TEXT NOT NULL UNIQUE); ALTER TABLE StudioRecordings ADD COLUMN DialogueId TEXT NOT NULL DEFAULT 'unassigned';" }
            });
            using (var db = Open())
            {
                if (db.ExecuteScalar<int>("SELECT COUNT(*) FROM StudioDialogues WHERE Id=@id", new { id = UnassignedId }) == 0)
                {
                    string path = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(DatabasePath)), "Dialogues", "Unassigned Recordings", "Dialogue.sqlite");
                    EnsureContainer(path, UnassignedId, "Unassigned Recordings");
                    db.Execute("INSERT INTO StudioDialogues VALUES (@id,@title,@path)", new { id = UnassignedId, title = "Unassigned Recordings", path });
                }
            }
            RefreshContainerFiles();
        }
        SQLiteConnection Open()
        {
            var db = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false, FailIfMissing = true }.ToString());
            db.Open(); db.Execute("PRAGMA busy_timeout=5000"); return db;
        }
        static void EnsureContainer(string path, string id, string title)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(path), "Recordings"));
            using (var db = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                db.Open();
                db.Execute("CREATE TABLE IF NOT EXISTS Dialogue (Id TEXT PRIMARY KEY,Title TEXT NOT NULL); CREATE TABLE IF NOT EXISTS Recordings (RecordingId TEXT PRIMARY KEY,Title TEXT NOT NULL,DatabasePath TEXT NOT NULL);");
                var existing = db.ExecuteScalar<string>("SELECT Id FROM Dialogue LIMIT 1");
                if (existing != null && existing != id) throw new InvalidDataException("This Dialogue database belongs to another container.");
                db.Execute("INSERT OR REPLACE INTO Dialogue VALUES (@id,@title)", new { id, title });
            }
        }
        // Container databases and their readable manifests are recoverable indexes of the app catalog.
        // Relative paths preserve a container and its child Recording folders when copied together.
        public void RefreshContainerFiles()
        {
            var recordings = List();
            foreach (var dialogue in Dialogues())
            {
                EnsureContainer(dialogue.DatabasePath, dialogue.Id, dialogue.Title);
                var root = Path.GetDirectoryName(dialogue.DatabasePath);
                var members = recordings.Where(r => r.DialogueId == dialogue.Id).ToList();
                using (var db = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = dialogue.DatabasePath, Pooling = false }.ToString()))
                {
                    db.Open(); using (var tx = db.BeginTransaction())
                    {
                        db.Execute("DELETE FROM Recordings", transaction: tx);
                        foreach (var r in members) db.Execute("INSERT INTO Recordings VALUES (@RecordingId,@Title,@path)", new { r.RecordingId, r.Title, path = Relative(root, r.DatabasePath) }, tx);
                        tx.Commit();
                    }
                }
                string manifest = Path.Combine(root, "Dialogue.md");
                File.WriteAllText(manifest, "# " + dialogue.Title + "\r\n\r\nGenerated Recording index. Membership is managed in Studio. Back up this folder and any externally linked Recording folders.\r\n\r\n" + string.Join("\r\n", members.Select(r => "- " + r.Title.Replace("\r", " ").Replace("\n", " ") + " — " + Relative(root, r.DatabasePath))));
            }
        }
        static string Relative(string root, string path) { return Uri.UnescapeDataString(new Uri(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).MakeRelativeUri(new Uri(path)).ToString()); }
        void Notify() { RefreshContainerFiles(); if (Changed != null) Changed(this, EventArgs.Empty); }
        public List<DialogueContainer> Dialogues() { using (var db = Open()) return db.Query<DialogueContainer>("SELECT * FROM StudioDialogues ORDER BY CASE WHEN Id='unassigned' THEN 1 ELSE 0 END,Title").ToList(); }
        public DialogueContainer CreateDialogue(string title, string parent)
        {
            if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200) throw new ArgumentException("Enter a Dialogue title of 1–200 characters.");
            title = title.Trim(); string id = Guid.NewGuid().ToString("N");
            string name = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
            var item = new DialogueContainer { Id = id, Title = title, DatabasePath = Path.Combine(Path.GetFullPath(parent), (name.Length == 0 ? "Dialogue" : name) + " - " + id.Substring(0, 8), "Dialogue.sqlite") };
            EnsureContainer(item.DatabasePath, id, title);
            using (var db = Open()) db.Execute("INSERT INTO StudioDialogues VALUES (@Id,@Title,@DatabasePath)", item);
            Notify(); return item;
        }
        public void Assign(string recordingId, string dialogueId)
        {
            using (var db = Open())
            using (var tx = db.BeginTransaction())
            {
                if (db.ExecuteScalar<int>("SELECT COUNT(*) FROM StudioDialogues WHERE Id=@dialogueId", new { dialogueId }, tx) != 1) throw new InvalidOperationException("Choose an existing Dialogue.");
                if (db.Execute("UPDATE StudioRecordings SET DialogueId=@dialogueId WHERE RecordingId=@recordingId", new { recordingId, dialogueId }, tx) != 1) throw new InvalidOperationException("Recording is not in the catalog.");
                tx.Commit();
            }
            Notify();
        }
        public void Remember(Recording recording)
        {
            using (var db = Open())
            {
                db.Execute(@"INSERT INTO StudioRecordings (RecordingId,Title,DatabasePath,OpenedUtc,DialogueId) VALUES (@Id,@Title,@Path,@Now,'unassigned')
                    ON CONFLICT(RecordingId) DO UPDATE SET Title=excluded.Title,DatabasePath=excluded.DatabasePath,OpenedUtc=excluded.OpenedUtc", new { recording.Id, recording.Title, Path = Path.Combine(recording.Folder, "Recording.sqlite"), Now = DateTime.UtcNow.ToString("O") });
            }
            Notify();
        }
        public List<CatalogEntry> List() { using (var db = Open()) return db.Query<CatalogEntry>("SELECT * FROM StudioRecordings ORDER BY OpenedUtc DESC").ToList(); }
    }
}
