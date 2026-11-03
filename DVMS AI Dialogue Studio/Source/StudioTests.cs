using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Dapper;
using DVMSDesktop;
using DVMSDesktop.Framework;

namespace DVMS.AIDialogueStudio
{
    static class StudioTests
    {
        public static int Run()
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestEvidence", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6)); Directory.CreateDirectory(folder);
            string log = Path.Combine(folder, "results.txt");
            Action<bool, string> check = (condition, title) => { File.AppendAllText(log, (condition ? "PASS " : "FAIL ") + title + Environment.NewLine); if (!condition) throw new Exception(title); };
            try
            {
                var app = new ApplicationServices(new ProductIdentity("StudioTests", "Studio Tests"), Path.Combine(folder, "App"), null, true);
                var catalog = new StudioCatalog(app.Store);
                app.Services.Add(catalog);
                var unassigned = catalog.Dialogues().Single();
                check(unassigned.Id == StudioCatalog.UnassignedId && File.Exists(unassigned.DatabasePath) && File.Exists(Path.Combine(Path.GetDirectoryName(unassigned.DatabasePath), "Dialogue.md")), "Unassigned Recordings has its own SQLite database and file container");
                var settings = ConnectionSettings.Get(app); const string testKey = "synthetic-key-for-protection-test";
                settings.SaveKey(testKey);
                var protectedFile = Path.Combine(app.Paths.Root, "Secrets", "OpenAI.key.dpapi");
                check(new ConnectionSettings(app).ReadKey() == testKey && !Encoding.UTF8.GetString(File.ReadAllBytes(protectedFile)).Contains(testKey), "Windows-protected API key survives settings reload without plaintext storage");
                settings.ClearKey(); check(settings.ReadKey() == null, "central connection settings can remove the saved key");
                bool settingsVisible = false;
                using (var timer = new System.Windows.Forms.Timer { Interval = 350 }) {
                    timer.Tick += (s,e) => { timer.Stop(); var dialog = Application.OpenForms.Cast<Form>().Single(f => f.Text == "Settings — OpenAI connection"); using (var image = new Bitmap(dialog.Width,dialog.Height)) { dialog.DrawToBitmap(image,new Rectangle(Point.Empty,dialog.Size)); image.Save(Path.Combine(folder,"connection-settings.png")); } settingsVisible = true; dialog.Close(); };
                    timer.Start(); settings.Show(null);
                }
                check(settingsVisible, "central connection settings opens as a visible native dialog");
                check(Directory.GetFiles(app.Paths.DatabaseDirectory, "*.db").Length == 1 && app.Navigation.TreePath == app.Store.DatabasePath, "settings, catalog and navigation share one application DB");
                var tree = app.Navigation.LoadTree(); string noteId = Guid.NewGuid().ToString();
                tree.Add(new NavigationNode { Id = Guid.NewGuid().ToString(), ParentId = tree.Single().Id, NodeType = NavigationNodeType.Tool, ToolID = noteId, Name = "Import check", SortOrder = 1 });
                app.Navigation.SaveImportedDocument(tree, noteId, "Atomic shared import", app.Store.DatabasePath);
                check(app.Store.LoadDocument(noteId).Text == "Atomic shared import", "framework's generic document import works with shared database");
                var repo = RecordingRepository.Create(folder, "Purpose and Telos — recording"); var r = repo.Load();
                var legacyApp = new ApplicationServices(new ProductIdentity("LegacyStudioTests", "Legacy Studio Tests"), Path.Combine(folder, "LegacyApp"), null, true);
                legacyApp.Store.ApplyMigrations("dialogue-studio", new[] { new DatabaseMigration { Version = 1, Sql = "CREATE TABLE StudioRecordings (RecordingId TEXT PRIMARY KEY,Title TEXT NOT NULL,DatabasePath TEXT NOT NULL,OpenedUtc TEXT NOT NULL);" } });
                using (var legacyDb = new SQLiteConnection("Data Source=" + legacyApp.Store.DatabasePath)) { legacyDb.Open(); legacyDb.Execute("INSERT INTO StudioRecordings VALUES (@Id,@Title,@path,@now)", new { r.Id, r.Title, path = repo.DatabasePath, now = DateTime.UtcNow.ToString("O") }); }
                var migratedCatalog = new StudioCatalog(legacyApp.Store);
                check(migratedCatalog.List().Single().DialogueId == StudioCatalog.UnassignedId && File.Exists(repo.DatabasePath), "existing v1 catalog migrates to Unassigned Recordings while preserving Recording identity and files");
                catalog.Remember(r); check(catalog.List().Single().RecordingId == r.Id, "catalog remembers independent Recording identity and location");
                var container = catalog.CreateDialogue("Purpose and Telos", Path.Combine(folder, "Dialogues"));
                using (var destination = new DialogueDestinationForm(app, container.Id)) {
                    destination.Show(); Application.DoEvents();
                    var picker = destination.Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<System.Windows.Forms.ComboBox>().Single();
                    check((picker.SelectedItem as DialogueContainer)?.Id == container.Id && picker.Items.OfType<DialogueContainer>().Any(d => d.Id == StudioCatalog.UnassignedId) && picker.Items.OfType<string>().Any(), "destination picker preselects the current Dialogue and offers new and unassigned destinations");
                    using (var image = new Bitmap(destination.Width,destination.Height)) { destination.DrawToBitmap(image,new Rectangle(Point.Empty,destination.Size)); image.Save(Path.Combine(folder,"dialogue-destination.png")); } destination.Close();
                }
                catalog.Assign(r.Id, container.Id); catalog.Remember(r);
                check(catalog.List().Single().DialogueId == container.Id, "Recording membership survives ordinary catalog updates");
                using (var containerDb = new SQLiteConnection("Data Source=" + container.DatabasePath)) { containerDb.Open(); check(containerDb.ExecuteScalar<int>("SELECT COUNT(*) FROM Recordings WHERE RecordingId=@Id", r) == 1, "Dialogue SQLite index contains its assigned Recording"); }
                catalog.Assign(r.Id, StudioCatalog.UnassignedId);
                using (var containerDb = new SQLiteConnection("Data Source=" + container.DatabasePath)) { containerDb.Open(); check(containerDb.ExecuteScalar<int>("SELECT COUNT(*) FROM Recordings") == 0 && File.Exists(repo.DatabasePath), "moving membership removes the old index entry and keeps the Recording files"); }
                catalog.Assign(r.Id, container.Id);
                string source = Path.Combine(folder, "source.md");
                string text = "# Source dialogue\n\n## 1. Grant\n\nExplain the Triune.\n\n## 2. Nex\n\nAn initial explanation.\n\n## 3. Grant\n\nThree base apexes form a fourth realization.\n\n## 4. Nex\n\nThat corrects my earlier count.\n";
                File.WriteAllText(source, text, new UTF8Encoding(false)); byte[] original = File.ReadAllBytes(source);
                repo.Import(source, text); r = repo.Load();
                check(r.Items.Count == 5 && r.Items[1].ExchangeId == r.Items[2].ExchangeId, "speaker import retains preamble and links question with answer");
                check(original.SequenceEqual(File.ReadAllBytes(Directory.GetFiles(Path.Combine(repo.Folder, "Sources")).Single())), "import preserves original bytes");
                check(original.SequenceEqual(File.ReadAllBytes(source)), "external original unchanged");
                var oldRevision = r.RevisionId; var stable = r.Items[2].Id; var originalText = r.Items[2].OriginalText;
                r.Items[2].Text = "Edited explanation — Head, Heart, and Hara.";
                var topic = new Topic { Title = "Triune" }; r.Topics.Add(topic); r.Items[2].TopicId = topic.Id;
                var last = r.Items.Last(); r.Items.Remove(last); r.Items.Insert(0, last);
                repo.Save(r, "Edited and reorganized");
                var reopened = RecordingRepository.OpenExisting(repo.DatabasePath).Load();
                check(reopened.Items.Any(i => i.Id == stable && i.Text.StartsWith("Edited explanation") && i.OriginalText == originalText), "edits persist under stable IDs without replacing original utterances");
                check(reopened.Items.First().Id == last.Id && reopened.Topics.Single().Id == topic.Id, "order and topic survive reopen");
                check(repo.Load(oldRevision).Items.Single(i => i.Id == stable).Text == originalText, "previous revision remains intact");
                var stale = repo.Load(); repo.Save(reopened, "Concurrent save"); bool conflict = false;
                try { repo.Save(stale, "Stale save"); } catch (InvalidOperationException) { conflict = true; }
                check(conflict, "stale save rejected without overwriting newer revision");
                r = repo.Load(); var parent = new Topic { Title = "Parent" }; var child = new Topic { Title = "Child", ParentId = parent.Id }; parent.ParentId = child.Id; r.Topics.Add(parent); r.Topics.Add(child); bool cycle = false;
                try { repo.Save(r, "Invalid cycle"); } catch (InvalidOperationException) { cycle = true; }
                check(cycle, "topic cycles rejected");
                repo.Import(source, text); check(repo.Load().Items.Count == reopened.Items.Count + 5, "multiple source contributions accumulate in one Recording");
                var restored = repo.Load(oldRevision); int before = restored.Generation; repo.Save(restored, "Restored earlier revision"); check(repo.Load().Generation == before + 1 && repo.Revisions().Count == before + 1, "restoring history creates a new revision");
                string exported = repo.ExportMarkdown(repo.Load()); check(File.ReadAllText(exported).Contains("fourth realization"), "Markdown export uses saved revision");
                check(Directory.Exists(Path.Combine(repo.Folder, "Media", "Original")), "portable folder has media and source directories");
                string moved = Path.Combine(folder, "MovedRecording"); CopyFolder(repo.Folder, moved);
                var movedRepo = RecordingRepository.OpenExisting(Path.Combine(moved, "Recording.sqlite")); catalog.Remember(movedRepo.Load());
                check(movedRepo.Load().Id == r.Id && catalog.List().Single().DatabasePath.StartsWith(moved), "portable Recording reopens in new location and catalog updates");
                using (var db = new SQLiteConnection("Data Source=" + movedRepo.DatabasePath + ";Pooling=False"))
                { db.Open(); check(db.ExecuteScalar<string>("PRAGMA integrity_check") == "ok", "SQLite integrity check"); bool immutable = false; try { db.Execute("UPDATE Utterances SET OriginalText='Changed'"); } catch (SQLiteException) { immutable = true; } check(immutable, "database rejects mutation of original utterances"); }
                using (var view = new StudioView(app))
                {
                    view.Size = new Size(1600, 850); view.CreateControl();
                    typeof(StudioView).GetMethod("SetRecording", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { movedRepo });
                    view.PerformLayout();
                    check(view.ExportText().Contains("fourth realization") && !view.IsDirty, "DevExpress workspace loads real Recording and exports its saved dialogue");
                    var edit = (DevExpress.XtraEditors.MemoEdit)typeof(StudioView).GetField("edited", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
                    edit.Text += "\nUI edit verification.";
                    check(view.IsDirty, "editor changes notify the framework dirty/save lifecycle");
                    view.SaveRecording();
                    check(!view.IsDirty && movedRepo.Load().Items.First().Text.Contains("UI edit verification"), "editor Save commits a new revision and clears dirty state");
                    using (var image = new Bitmap(view.Width, view.Height)) { view.DrawToBitmap(image, new Rectangle(Point.Empty, view.Size)); image.Save(Path.Combine(folder, "studio-workspace.png")); }
                }
                app.Configuration.Set("product.managedNavigation", "true"); app.Configuration.Set("product.workspaceTitle", "Recording editor");
                app.Modules.Remove("notes"); app.Modules.Remove("overview"); app.Modules.Register(new StudioModule());
                var archive = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Purpose and Telos Chats", "Clarify Sites Meaning - Full Dialogue.md"));
                if (File.Exists(archive))
                {
                    var actual = RecordingRepository.Create(folder, "Actual archive import"); actual.Import(archive, File.ReadAllText(archive));
                    check(actual.Load().Items.Count == 59, "actual 58-message long dialogue imports with all speaker passages plus preserved preamble");
                }
                else File.AppendAllText(log, "SKIP actual archive not present at " + archive + Environment.NewLine);
                var indexed = movedRepo.Load(); var parentTopic = new Topic { Title = "Purpose and Telos" }; var subTopic = new Topic { Title = "Triune correction", ParentId = parentTopic.Id }; indexed.Topics.Add(parentTopic); indexed.Topics.Add(subTopic); indexed.Items[2].TopicId = subTopic.Id; movedRepo.Save(indexed, "Navigation fixture"); catalog.Remember(indexed);
                var navigation = new RecordingNavigation(app); app.Services.Add(navigation); navigation.Synchronize();
                check(app.Navigation.LoadTree().Single(n => n.NodeType == NavigationNodeType.Root).Name == "Dialogues", "framework root uses Dialogues terminology");
                check(app.Navigation.LoadTree().Single(n => n.ToolID == indexed.Id).ParentId == "dialogue:" + container.Id, "Recording appears under its assigned Dialogue container");
                check(app.Navigation.LoadTree().Any(n => n.ModuleId == "studio-topic" && n.Name == subTopic.Title && n.ParentId == "topic:" + indexed.Id + ":" + parentTopic.Id), "Recording topics appear beneath their parent topics in the framework tree");
                using (var form = new StudioForm(app, navigation))
                {
                    DevExpress.LookAndFeel.UserLookAndFeel.Default.SetSkinStyle("Office 2019 Colorful");
                    form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-5000, -5000); form.Size = new Size(1700, 1050); form.Show(); Application.DoEvents();
                    var studio = app.Navigation.LoadTree().Single(n => n.ToolID == indexed.Id);
                    typeof(Desktop_Form).GetMethod("OpenTool", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { studio });
                    check(studio.Name == indexed.Title && studio.IsContainer, "Recording title is the actual framework workspace and container node");
                    var opened = (System.Collections.Generic.Dictionary<string, IWorkspaceDocument>)typeof(Desktop_Form).GetField("documents", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    var editor = (StudioView)opened[studio.ToolID].View;
                    typeof(StudioView).GetMethod("SetRecording", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, new object[] { movedRepo });
                    check(navigation.Activate(app.Navigation.LoadTree().Single(n => n.ModuleId == "studio-topic" && n.Name == subTopic.Title)), "topic activation opens the owning Recording workspace");
                    form.PerformLayout(); Application.DoEvents();
                    using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(Path.Combine(folder, "studio-framework.png")); }
                    form.Close();
                }
                File.AppendAllText(log, "ALL CHECKS PASSED" + Environment.NewLine); return 0;
            }
            catch (Exception ex) { File.AppendAllText(log, ex.ToString()); return 1; }
        }
        static void CopyFolder(string source, string destination)
        { Directory.CreateDirectory(destination); foreach (string f in Directory.GetFiles(source)) File.Copy(f, Path.Combine(destination, Path.GetFileName(f))); foreach (string d in Directory.GetDirectories(source)) CopyFolder(d, Path.Combine(destination, Path.GetFileName(d))); }
    }
}
