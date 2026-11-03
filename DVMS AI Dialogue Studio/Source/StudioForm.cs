using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraBars;
using DevExpress.XtraBars.Ribbon;
using DVMSDesktop;
using DVMSDesktop.Framework;

namespace DVMS.AIDialogueStudio
{
    public sealed class StudioForm : Desktop_Form
    {
        readonly ApplicationServices app;
        readonly RecordingNavigation navigation;
        public StudioForm(ApplicationServices app, RecordingNavigation navigation) : base(app) { this.app = app; this.navigation = navigation; navigation.Attach(this); }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            var ribbon = Descendants(this).OfType<RibbonControl>().First();
            var hide = new HashSet<string> { "notes.new", "overview.new", "file.open", "file.export", "navigation.import", "navigation.export", "files.register", "assistant.ask", "integrations.show", "help.show", "recent.show", "data.backup" };
            foreach (BarItem item in ribbon.Items)
            {
                var command = item.Tag as string;
                if (command != null && hide.Contains(command)) { item.Visibility = BarItemVisibility.Never; ProductCommands.Remove(command); }
                item.Caption = item.Caption.Replace("Close tool", "Close Recording").Replace("Recent tools", "Recent Recordings").Replace("tools and commands", "Recordings and commands").Replace("Tools", "Recordings").Replace("Save all", "Save all Recordings");
            }
            foreach (var command in ProductCommands.All) command.Title = command.Title.Replace("tool", "Recording").Replace("tools", "Recordings");
            var page = new RibbonPage("Dialogues"); var group = new RibbonPageGroup("Dialogues and Recordings");
            Action<string, Action> add = (label, action) => { var button = new BarButtonItem(ribbon.Manager, label) { RibbonStyle = RibbonItemStyles.Large }; button.ImageOptions.SvgImage = IconCatalog.Get(label.StartsWith("New") ? "New" : label.StartsWith("Open") ? "Open" : "Workspace"); button.ItemClick += (s, args) => RunRecordingAction(action); group.ItemLinks.Add(button); };
            add("New Recording", () => ((StudioDocument)ActivateWorkspace(navigation.LibraryNode)).NewRecording());
            add("New Dialogue", () => ((StudioDocument)ActivateWorkspace(navigation.LibraryNode)).NewDialogue());
            add("Open Recording", () => ((StudioDocument)ActivateWorkspace(navigation.LibraryNode)).OpenRecording());
            add("Recording library", navigation.OpenLibrary);
            page.Groups.Add(group); ribbon.Pages.Insert(0, page); ribbon.SelectedPage = page;
            var settingsPage = new RibbonPage("Settings"); var settingsGroup = new RibbonPageGroup("Connections");
            var connectionButton = new BarButtonItem(ribbon.Manager, "OpenAI connection") { RibbonStyle = RibbonItemStyles.Large };
            connectionButton.ItemClick += (s, args) => RunRecordingAction(() => ConnectionSettings.Get(app).Show(this));
            settingsGroup.ItemLinks.Add(connectionButton); settingsPage.Groups.Add(settingsGroup); ribbon.Pages.Add(settingsPage);
            var toolbar = Descendants(this).OfType<ToolStrip>().FirstOrDefault(c => c.Name == "treeToolbar");
            if (toolbar != null)
            {
                toolbar.Items.Clear();
                var create = new ToolStripButton("New Recording"); create.Click += (s, args) => RunRecordingAction(() => ((StudioDocument)ActivateWorkspace(navigation.LibraryNode)).NewRecording());
                var open = new ToolStripButton("Open Recording"); open.Click += (s, args) => RunRecordingAction(() => ((StudioDocument)ActivateWorkspace(navigation.LibraryNode)).OpenRecording()); toolbar.Items.Add(create); toolbar.Items.Add(open);
            }
            var tree = Descendants(this).OfType<DevExpress.XtraTreeList.TreeList>().FirstOrDefault(c => c.Name == "treeList_Navigation");
            if (tree != null) { tree.ContextMenuStrip = null; tree.OptionsDragAndDrop.DragNodesMode = DevExpress.XtraTreeList.DragNodesMode.None; }
            foreach (var panel in Descendants(this).OfType<DevExpress.XtraBars.Docking.DockPanel>()) { if (panel.Text == "Navigation" || panel.Text == "Recordings") panel.Text = "Dialogues"; if (panel.Text == "Tools" || panel.Text == "Workspace") panel.Text = "Recording editor"; }
            navigation.OpenLibrary(); Notify("Ready · create or open a Recording");
        }
        void RunRecordingAction(Action action) { try { action(); } catch (Exception ex) { DevExpress.XtraEditors.XtraMessageBox.Show(this, ex.Message, "Recording", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
        static IEnumerable<Control> Descendants(Control control) { foreach (Control child in control.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    }
}
