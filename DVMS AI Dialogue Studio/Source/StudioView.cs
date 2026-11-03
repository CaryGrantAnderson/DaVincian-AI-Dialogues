using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DVMSDesktop.Framework;

namespace DVMS.AIDialogueStudio
{
    public partial class StudioView : XtraUserControl
    {
        readonly ApplicationServices app;
        readonly StudioCatalog catalog;
        RecordingRepository repository;
        Recording recording;
        DialogueItem current;
        string filterTopic;
        string libraryDialogue;
        bool binding;
        string CurrentDialogueId { get { if (recording != null) return catalog.List().Single(r => r.RecordingId == recording.Id).DialogueId; try { return app.Services.Get<RecordingNavigation>().CurrentDialogueId; } catch (InvalidOperationException) { return StudioCatalog.UnassignedId; } } }
        DialogueContainer ChooseDestination() { return DialogueDestinationForm.Choose(app, this, CurrentDialogueId); }
        public void NewDialogue() { string title = XtraInputBox.Show("Dialogue title", "New Dialogue", "New Dialogue"); if (!string.IsNullOrWhiteSpace(title)) catalog.CreateDialogue(title, Path.Combine(app.Paths.Root, "Dialogues")); }
        void MoveToDialogue() { RequireRecording(); var destination = ChooseDestination(); if (destination != null) { catalog.Assign(recording.Id, destination.Id); RefreshAll(); } }
        public bool IsDirty { get; private set; }
        public event EventHandler DirtyChanged;
        public StudioView(ApplicationServices app)
        {
            this.app = app; catalog = app.Services.Get<StudioCatalog>(); InitializeComponent(); RefreshLibrary(); catalog.Changed += CatalogChanged;
        }
        void CatalogChanged(object sender, EventArgs e) { if (!IsDisposed) RefreshLibrary(); }
        protected override void Dispose(bool disposing) { if (disposing) catalog.Changed -= CatalogChanged; base.Dispose(disposing); }
        public void FilterLibrary(string dialogueId) { libraryDialogue = dialogueId; RefreshLibrary(); if (recording == null) recordingLabel.Text = dialogueId == null ? "All Dialogues · Recording library" : catalog.Dialogues().Single(d => d.Id == dialogueId).Title + " · Recordings"; }
        void Safe(Action action)
        {
            try { action(); } catch (Exception ex) { app.Log.Write("Error", "Recording operation failed", ex); XtraMessageBox.Show(this, ex.Message, "Recording Studio"); }
        }
        void Changed(bool dirty = true) { IsDirty = dirty; if (DirtyChanged != null) DirtyChanged(this, EventArgs.Empty); }
        bool CanSwitch()
        {
            if (!IsDirty) return true;
            var result = XtraMessageBox.Show(this, "Save a revision before leaving this Recording?", "Unsaved Recording", MessageBoxButtons.YesNoCancel);
            if (result == DialogResult.Cancel) return false;
            if (result == DialogResult.Yes) SaveRecording();
            return true;
        }
        void RefreshLibrary() { libraryGrid.DataSource = catalog.List().Where(r => libraryDialogue == null || r.DialogueId == libraryDialogue).ToList(); }
        void RequireRecording() { if (recording == null) throw new InvalidOperationException("Create or open a Recording first."); }
        public void NewRecording()
        {
            CreateRecordingView();
        }
        StudioView CreateRecordingView()
        {
            if (!CanSwitch()) return null;
            var destination = ChooseDestination(); if (destination == null) return null;
            var title = XtraInputBox.Show("Recording title", "New Recording", "New Recording"); if (string.IsNullOrWhiteSpace(title)) return null;
            var repo = RecordingRepository.Create(Path.Combine(Path.GetDirectoryName(destination.DatabasePath), "Recordings"), title);
            catalog.Remember(repo.Load()); catalog.Assign(repo.Load().Id, destination.Id); return NavigateOrSet(repo);
        }
        public void OpenRecording()
        {
            if (!CanSwitch()) return;
            using (var dialog = new OpenFileDialog { Title = "Open a portable Recording database", Filter = "Recording database|Recording.sqlite|SQLite files|*.sqlite" })
                if (dialog.ShowDialog(this) == DialogResult.OK) {
                    var repo = RecordingRepository.OpenExisting(dialog.FileName); var data = repo.Load();
                    if (!catalog.List().Any(r => r.RecordingId == data.Id)) { var destination = ChooseDestination(); if (destination == null) return; catalog.Remember(data); catalog.Assign(data.Id, destination.Id); }
                    NavigateOrSet(repo);
                }
        }
        void OpenCatalogSelection()
        {
            var entry = libraryView.GetFocusedRow() as CatalogEntry; if (entry == null || !CanSwitch()) return;
            NavigateOrSet(RecordingRepository.OpenExisting(entry.DatabasePath));
        }
        StudioView NavigateOrSet(RecordingRepository repo)
        {
            RecordingNavigation navigation;
            try { navigation = app.Services.Get<RecordingNavigation>(); } catch (InvalidOperationException) { SetRecording(repo); return this; }
            var data = repo.Load(); catalog.Remember(data); return (StudioView)navigation.OpenRecording(data).View;
        }
        void SetRecording(RecordingRepository repo)
        {
            repo.RecoverInterruptedCaptures();
            var next = repo.Load(); catalog.Remember(next); repository = repo; recording = next; filterTopic = null; contentSplit.Panel1Collapsed = true; Changed(false); RefreshAll();
        }
        public void OpenRecordingPath(string path) { SetRecording(RecordingRepository.OpenExisting(path)); }
        public void SelectTopic(string id) { filterTopic = id; RefreshDialogue(); }
        void StartLive()
        {
            if (recording == null) { var view = CreateRecordingView(); if (view != null) view.StartLiveInDestination(); return; }
            var destination = ChooseDestination(); if (destination == null) return; catalog.Assign(recording.Id, destination.Id); StartLiveInDestination();
        }
        void StartLiveInDestination()
        {
            if (IsDirty) SaveRecording();
            var settings = ConnectionSettings.Get(app); if (!settings.EnsureKey(this)) return;
            int before = repository.LiveSessions().Count;
            using (var dialog = new LiveRecordingForm(app, repository)) dialog.ShowDialog(this);
            SetRecording(repository); status.Text = repository.LiveSessions().Count > before ? "Live contribution preserved. Open Captured audio & timing to review the source tracks and captions." : "No live contribution started.";
        }
        void ShowCaptures()
        {
            RequireRecording();
            using (var dialog = new XtraForm { Text = "Captured audio & timing — " + recording.Title, Width = 1000, Height = 650 })
            {
                var text = new System.Text.StringBuilder("Provider transcript timestamps and local PCM capture timestamps are retained separately. Estimated alignment uses receipt of session.started; network delay is not measured.\r\n\r\n");
                foreach (var session in repository.LiveSessions())
                {
                    text.AppendLine(session.StartedUtc + " · " + session.Status + " · " + session.SampleRate + " Hz · " + session.Id);
                    text.AppendLine("Voice: " + session.VoiceId + "\r\nProvider session: " + session.ProviderSessionId);
                    text.AppendLine("Provider-to-local offset estimate: " + session.ProviderOffsetMs + " ms");
                    foreach (var who in new[] { "Grant", "Nex" }) text.AppendLine(who + " WAV: " + repository.AudioPath(session.Id, who));
                    foreach (var caption in repository.Captions(session.Id)) text.AppendLine(caption.Speaker + " [provider " + caption.StartMs + "–" + caption.EndMs + " ms; received at local " + caption.ReceivedCaptureMs + " ms]: " + caption.Delta);
                    if (!string.IsNullOrEmpty(session.Error)) text.AppendLine("Capture note: " + session.Error);
                    text.AppendLine();
                }
                var memo = new MemoEdit { Dock = DockStyle.Fill, Text = text.ToString() }; memo.Properties.ReadOnly = true; memo.Properties.ScrollBars = ScrollBars.Both;
                foreach (var path in repository.ImportedAudioPaths()) text.AppendLine("Imported original audio (no generated transcript): " + path);
                memo.Text = text.ToString();
                var open = new SimpleButton { Dock = DockStyle.Bottom, Height = 36, Text = "Open this Recording's media folder" }; open.Click += (s, e) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(repository.Folder, "Media")) { UseShellExecute = true });
                dialog.Controls.Add(memo); dialog.Controls.Add(open); dialog.ShowDialog(this);
            }
        }
        void RefreshAll()
        {
            binding = true;
            try
            {
                recordingLabel.Text = recording.Title + " · revision " + recording.Generation;
                RefreshLibrary();
                topics.Nodes.Clear();
                Action<Topic, DevExpress.XtraTreeList.Nodes.TreeListNode> add = null;
                add = (topic, parent) => { var node = topics.AppendNode(new object[] { topic.Title }, parent); node.Tag = topic; foreach (var child in recording.Topics.Where(t => t.ParentId == topic.Id)) add(child, node); };
                foreach (var topic in recording.Topics.Where(t => t.ParentId == null)) add(topic, null);
                topics.ExpandAll();
                topicPicker.Items.Clear(); topicPicker.Items.Add("No topic"); foreach (var t in recording.Topics) topicPicker.Items.Add(t); topicPicker.SelectedIndex = 0;
                revisions.Items.Clear(); foreach (var r in repository.Revisions()) revisions.Items.Add(r); if (revisions.Items.Count > 0) revisions.SelectedIndex = 0;
            }
            finally { binding = false; }
            RefreshDialogue();
        }
        void RefreshDialogue()
        {
            if (recording == null) return;
            binding = true;
            try { int p = 1; foreach (var item in recording.Items) item.Position = p++; dialogueGrid.DataSource = new BindingList<DialogueItem>(recording.Items.Where(i => filterTopic == null || i.TopicId == filterTopic).ToList()); }
            finally { binding = false; }
            LoadPassage();
        }
        void LoadPassage()
        {
            if (binding) return;
            binding = true;
            try { current = dialogueView.GetFocusedRow() as DialogueItem; edited.Text = current == null ? "" : current.Text; original.Text = current == null ? "" : current.OriginalText; speaker.Text = current == null ? "" : current.Speaker; }
            finally { binding = false; }
        }
        void PassageChanged()
        {
            if (binding || current == null) return;
            current.Text = edited.Text; current.Speaker = speaker.Text; Changed(); dialogueView.RefreshRow(dialogueView.FocusedRowHandle);
        }
        public void SaveRecording()
        {
            if (recording == null || !IsDirty) return;
            repository.Save(recording, "Edited dialogue"); catalog.Remember(recording); Changed(false); RefreshAll(); status.Text = "Saved revision " + recording.Generation + " · " + repository.DatabasePath;
        }
        void ImportSource()
        {
            if (recording == null) { var view = CreateRecordingView(); if (view != null) view.ImportInDestination(true); return; }
            ImportInDestination(false);
        }
        void ImportInDestination(bool destinationChosen)
        {
            if (IsDirty) SaveRecording();
            using (var dialog = new OpenFileDialog { Title = "Add original text or audio to this Recording", Filter = "Supported sources|*.md;*.txt;*.rtf;*.wav;*.mp3;*.m4a;*.flac;*.ogg;*.webm|Text sources|*.md;*.txt;*.rtf|Audio recordings|*.wav;*.mp3;*.m4a;*.flac;*.ogg;*.webm", Multiselect = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!destinationChosen) { var destination = ChooseDestination(); if (destination == null) return; catalog.Assign(recording.Id, destination.Id); }
                foreach (var file in dialog.FileNames)
                {
                    if (new[] { ".wav", ".mp3", ".m4a", ".flac", ".ogg", ".webm" }.Contains(Path.GetExtension(file).ToLowerInvariant())) { repository.ImportAudio(file); continue; }
                    string text;
                    if (Path.GetExtension(file).Equals(".rtf", StringComparison.OrdinalIgnoreCase)) { using (var rtf = new RichTextBox()) { rtf.LoadFile(file); text = rtf.Text; } }
                    else text = File.ReadAllText(file);
                    repository.Import(file, text);
                }
                SetRecording(repository); status.Text = "Imported " + dialog.FileNames.Length + " source(s). Original files are preserved in Sources; text interpretation should be reviewed.";
            }
        }
        void AddExchange()
        {
            RequireRecording(); string exchange = Guid.NewGuid().ToString("N");
            recording.Items.Add(new DialogueItem { ExchangeId = exchange, Speaker = "Grant", Text = "", TopicId = filterTopic });
            recording.Items.Add(new DialogueItem { ExchangeId = exchange, Speaker = "Nex", Text = "", TopicId = filterTopic });
            Changed(); RefreshDialogue(); dialogueView.FocusedRowHandle = dialogueView.RowCount - 2;
        }
        void MoveExchange(int delta)
        {
            RequireRecording(); if (current == null) return;
            var groups = recording.Items.GroupBy(i => i.ExchangeId).Select(g => g.ToList()).ToList(); int at = groups.FindIndex(g => g.Any(i => i.Id == current.Id)); int next = at + delta;
            if (next < 0 || next >= groups.Count) return;
            var group = groups[at]; groups[at] = groups[next]; groups[next] = group; recording.Items = groups.SelectMany(g => g).ToList(); Changed(); RefreshDialogue();
        }
        void RenameRecording() { RequireRecording(); string title = XtraInputBox.Show("Recording title", "Rename Recording", recording.Title); if (!string.IsNullOrWhiteSpace(title)) { recording.Title = title; recordingLabel.Text = title; Changed(); } }
        void AddTopic()
        {
            RequireRecording(); string title = XtraInputBox.Show("Topic title (a child of the selected topic, or a root topic when All passages is selected)", "Add topic", "New topic"); if (string.IsNullOrWhiteSpace(title)) return;
            recording.Topics.Add(new Topic { Title = title, ParentId = filterTopic }); Changed(); RefreshAll();
        }
        void AssignTopic()
        {
            RequireRecording(); if (current == null) return; var topic = topicPicker.SelectedItem as Topic;
            foreach (var item in recording.Items.Where(i => i.ExchangeId == current.ExchangeId)) item.TopicId = topic == null ? null : topic.Id;
            Changed(); RefreshDialogue();
        }
        void RestoreRevision()
        {
            RequireRecording(); var revision = revisions.SelectedItem as RevisionInfo; if (revision == null || !CanSwitch()) return;
            recording = repository.Load(revision.Id); filterTopic = null; Changed(); RefreshAll(); status.Text = "Historical revision " + revision.Number + " loaded as an editable draft. Save creates a new revision; history remains intact.";
        }
        void ExportMarkdown()
        {
            RequireRecording(); if (IsDirty) SaveRecording(); string path = repository.ExportMarkdown(recording); status.Text = "Exported " + path;
            XtraMessageBox.Show(this, "Markdown exported to:\n" + path, "Recording export");
        }
        public string ExportText() { return recording == null ? "" : repository.ExportText(recording); }
    }
}
