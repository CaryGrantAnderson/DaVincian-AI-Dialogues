using System;
using System.Windows.Forms;
using DVMSDesktop;
using DVMSDesktop.Framework;

namespace DVMS.AIDialogueStudio
{
    public sealed class StudioModule : IDesktopModule
    {
        public string Id { get { return "dialogue-studio"; } }
        public string Name { get { return "AI Dialogue Studio"; } }
        public string RequiredFeature { get { return "core"; } }
        public void Register(ModuleRegistration registration)
        {
            registration.Application.Configuration.Register(new SettingDefinition { Key = "studio.recordingsRoot", Label = "Default Recordings folder", DefaultValue = System.IO.Path.Combine(registration.Application.Paths.Root, "Recordings") });
        }
        public IWorkspaceDocument Open(NavigationNode node, ApplicationServices app) { return new StudioDocument(node, app); }
    }
    public sealed class StudioDocument : IWorkspaceDocument
    {
        readonly StudioView view;
        public StudioDocument(NavigationNode node, ApplicationServices app)
        {
            Id = node.ToolID; Title = node.Name; view = new StudioView(app);
            if (node.Description != "studio-library" && System.IO.File.Exists(node.Description)) view.OpenRecordingPath(node.Description);
            view.DirtyChanged += (s, e) => { if (Changed != null) Changed(this, EventArgs.Empty); };
        }
        public string Id { get; private set; }
        public string Title { get; set; }
        public Control View { get { return view; } }
        public bool IsDirty { get { return view.IsDirty; } }
        public event EventHandler Changed;
        public void Save() { view.SaveRecording(); }
        public void SelectTopic(string id) { view.SelectTopic(id); }
        public void NewRecording() { view.NewRecording(); }
        public void NewDialogue() { view.NewDialogue(); }
        public void FilterLibrary(string dialogueId) { view.FilterLibrary(dialogueId); }
        public void OpenRecording() { view.OpenRecording(); }
        public string ExportText() { return view.ExportText(); }
        public void ImportText(string text) { throw new InvalidOperationException("Use Recording Studio's Import source command to preserve the original file and provenance."); }
        public void Dispose() { view.Dispose(); }
    }
}
