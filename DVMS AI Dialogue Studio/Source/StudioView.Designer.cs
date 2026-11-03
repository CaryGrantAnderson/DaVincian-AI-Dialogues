using System.Drawing;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraTreeList;

namespace DVMS.AIDialogueStudio
{
    public partial class StudioView
    {
        GridControl libraryGrid, dialogueGrid;
        GridView libraryView, dialogueView;
        TreeList topics;
        MemoEdit edited, original;
        TextEdit speaker;
        LabelControl recordingLabel, status;
        System.Windows.Forms.ComboBox topicPicker, revisions;
        FlowLayoutPanel actions;
        SplitContainer contentSplit;
        void InitializeComponent()
        {
            Dock = DockStyle.Fill; Size = new Size(1100, 740);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(10) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            recordingLabel = new LabelControl { Text = "DVMS AI Dialogue Studio · choose or create a Recording", Dock = DockStyle.Fill, AutoSizeMode = LabelAutoSizeMode.None };
            recordingLabel.Appearance.Font = new Font("Segoe UI", 14, FontStyle.Bold); layout.Controls.Add(recordingLabel, 0, 0);
            actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };
            AddButton("New Dialogue", NewDialogue); AddButton("New Recording", NewRecording); AddButton("Open Recording", OpenRecording); AddButton("Move to Dialogue", MoveToDialogue); AddButton("Import source", ImportSource); AddButton("Save revision", SaveRecording); AddButton("Export Markdown", ExportMarkdown); AddButton("Add exchange", AddExchange); AddButton("Move exchange up", () => MoveExchange(-1)); AddButton("Move exchange down", () => MoveExchange(1)); AddButton("Rename Recording", RenameRecording);
            AddButton("Add topic", AddTopic); AddButton("All passages", () => SelectTopic(null)); AddButton("Live Recording", StartLive); AddButton("Captured audio & timing", ShowCaptures);
            layout.Controls.Add(actions, 0, 1);
            var outer = new SplitContainer { Size = new Size(1080, 600), Dock = DockStyle.Fill, SplitterDistance = 245, FixedPanel = FixedPanel.Panel1 };
            contentSplit = outer;
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            left.Controls.Add(new LabelControl { Text = "Recording library", Dock = DockStyle.Fill }, 0, 0);
            libraryGrid = new GridControl { Dock = DockStyle.Fill }; libraryView = new GridView(libraryGrid); libraryGrid.MainView = libraryView; libraryView.OptionsBehavior.Editable = false; libraryView.OptionsView.ShowGroupPanel = false; libraryView.OptionsView.ShowIndicator = false;
            libraryView.Columns.AddVisible("Title", "Recording"); libraryView.DoubleClick += (s, e) => Safe(() => OpenCatalogSelection()); left.Controls.Add(libraryGrid, 0, 1);
            var topicButtons = new FlowLayoutPanel { Dock = DockStyle.Fill }; var add = new SimpleButton { Text = "Add topic", Width = 95 }; add.Click += (s, e) => Safe(AddTopic); var all = new SimpleButton { Text = "All passages", Width = 100 }; all.Click += (s, e) => { filterTopic = null; RefreshDialogue(); }; topicButtons.Controls.AddRange(new Control[] { add, all }); left.Controls.Add(topicButtons, 0, 2);
            topics = new TreeList { Dock = DockStyle.Fill }; topics.Columns.AddVisible("Title").Caption = "Topics"; topics.OptionsBehavior.Editable = false; topics.FocusedNodeChanged += (s, e) => { if (!binding) { var topic = topics.FocusedNode == null ? null : topics.FocusedNode.Tag as Topic; filterTopic = topic == null ? null : topic.Id; RefreshDialogue(); } }; left.Controls.Add(topics, 0, 3); outer.Panel1.Controls.Add(left);
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(8, 0, 0, 0) };
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 42)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            dialogueGrid = new GridControl { Dock = DockStyle.Fill }; dialogueView = new GridView(dialogueGrid); dialogueGrid.MainView = dialogueView; dialogueView.OptionsBehavior.Editable = false; dialogueView.OptionsView.ShowGroupPanel = false; dialogueView.OptionsView.ShowAutoFilterRow = true;
            dialogueView.Columns.AddVisible("Position", "Order").Width = 45; dialogueView.Columns.AddVisible("Speaker", "Speaker").Width = 90; dialogueView.Columns.AddVisible("Text", "Dialogue passage").Width = 600;
            dialogueView.FocusedRowChanged += (s, e) => LoadPassage(); right.Controls.Add(dialogueGrid, 0, 0);
            var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false }; tools.Controls.Add(new Label { Text = "Speaker", AutoSize = true, Margin = new Padding(0, 8, 4, 0) }); speaker = new TextEdit { Width = 110 }; speaker.EditValueChanged += (s, e) => PassageChanged(); tools.Controls.Add(speaker);
            topicPicker = new System.Windows.Forms.ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList }; tools.Controls.Add(topicPicker); var assign = new SimpleButton { Text = "Assign topic", Width = 95 }; assign.Click += (s, e) => Safe(AssignTopic); tools.Controls.Add(assign);
            revisions = new System.Windows.Forms.ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList }; tools.Controls.Add(revisions); var restore = new SimpleButton { Text = "Restore revision", Width = 110 }; restore.Click += (s, e) => Safe(RestoreRevision); tools.Controls.Add(restore); right.Controls.Add(tools, 0, 1);
            var editors = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 2 }; editors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); editors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40)); editors.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); editors.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            editors.Controls.Add(new LabelControl { Text = "Edited passage · saved as a new revision", Dock = DockStyle.Fill }, 0, 0); editors.Controls.Add(new LabelControl { Text = "Original source · preserved", Dock = DockStyle.Fill }, 1, 0);
            edited = new MemoEdit { Dock = DockStyle.Fill }; edited.Properties.ScrollBars = ScrollBars.Both; edited.EditValueChanged += (s, e) => PassageChanged(); original = new MemoEdit { Dock = DockStyle.Fill }; original.Properties.ReadOnly = true; original.Properties.ScrollBars = ScrollBars.Both; editors.Controls.Add(edited, 0, 1); editors.Controls.Add(original, 1, 1); right.Controls.Add(editors, 0, 2);
            outer.Panel2.Controls.Add(right); layout.Controls.Add(outer, 0, 2);
            status = new LabelControl { Dock = DockStyle.Fill, AutoSizeMode = LabelAutoSizeMode.None, Text = "Recording library · live capture saves separate Grant/Nex audio tracks and timestamped captions." }; layout.Controls.Add(status, 0, 3); Controls.Add(layout);
        }
        void AddButton(string text, System.Action action)
        {
            var b = new SimpleButton { Text = text, AutoSize = true, Height = 26, Margin = new Padding(2) }; b.Click += (s, e) => Safe(action); actions.Controls.Add(b);
        }
    }
}
