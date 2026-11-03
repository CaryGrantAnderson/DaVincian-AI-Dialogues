using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using DVMSDesktop;
using DVMSDesktop.Framework;

namespace DVMS.AIDialogueStudio
{
    public sealed class RecordingNavigation
    {
        readonly ApplicationServices app;
        readonly StudioCatalog catalog;
        Desktop_Form host;
        public string CurrentDialogueId { get; private set; } = StudioCatalog.UnassignedId;
        public const string LibraryId = "51500164-5ded-4d1f-8d88-769d453dd761";
        public RecordingNavigation(ApplicationServices app)
        {
            this.app = app; catalog = app.Services.Get<StudioCatalog>();
            catalog.Changed += (s, e) => { Synchronize(); if (host != null && !host.IsDisposed) host.ReloadProductNavigation(); };
        }
        public void Attach(Desktop_Form form) { host = form; app.NavigationActivation = Activate; }
        public NavigationNode LibraryNode { get { return new NavigationNode { Id = LibraryId, ToolID = LibraryId, ModuleId = "dialogue-studio", NodeType = NavigationNodeType.Tool, Name = "Recording library", Description = "studio-library" }; } }
        public void Synchronize()
        {
            var old = app.Navigation.LoadTree();
            var root = old.Single(n => n.NodeType == NavigationNodeType.Root).Copy(); root.Name = "Dialogues"; root.ModuleId = "studio-root";
            if (old.Any(n => n.ModuleId == "dialogue-studio" && n.Name == "Recording Studio"))
            {
                var backup = Path.Combine(app.Paths.Root, "Navigation-before-recordings.json");
                if (!File.Exists(backup)) File.WriteAllText(backup, new JavaScriptSerializer().Serialize(old));
            }
            var nodes = new List<NavigationNode> { root };
            foreach (var dialogue in catalog.Dialogues()) nodes.Add(new NavigationNode { Id = "dialogue:" + dialogue.Id, ParentId = root.Id, NodeType = NavigationNodeType.Folder, ModuleId = "studio-dialogue", Name = dialogue.Title, Description = dialogue.Id, Icon = "Folder", SortOrder = nodes.Count });
            foreach (var entry in catalog.List())
            {
                var recNode = new NavigationNode { Id = "recording:" + entry.RecordingId, ToolID = entry.RecordingId, ParentId = "dialogue:" + entry.DialogueId, NodeType = NavigationNodeType.Tool, ModuleId = "dialogue-studio", Name = entry.Title, Description = entry.DatabasePath, Icon = "Document", SortOrder = nodes.Count, IsContainer = true };
                nodes.Add(recNode);
                if (!File.Exists(entry.DatabasePath)) continue;
                try
                {
                    var recording = RecordingRepository.OpenExisting(entry.DatabasePath).Load(); recNode.Name = recording.Title;
                    foreach (var topic in recording.Topics)
                        nodes.Add(new NavigationNode { Id = "topic:" + entry.RecordingId + ":" + topic.Id, ParentId = topic.ParentId == null ? recNode.Id : "topic:" + entry.RecordingId + ":" + topic.ParentId, NodeType = NavigationNodeType.Folder, ModuleId = "studio-topic", Name = topic.Title, Description = entry.RecordingId + "|" + topic.Id, Icon = "Folder", SortOrder = nodes.Count });
                }
                catch (Exception ex) { app.Log.Write("Warning", "Recording tree could not load " + entry.Title, ex); }
            }
            app.Navigation.SaveTree(nodes);
        }
        public bool Activate(NavigationNode node)
        {
            if (host == null) return false;
            if (node.NodeType == NavigationNodeType.Root) { ((StudioDocument)host.ActivateWorkspace(LibraryNode)).FilterLibrary(null); return true; }
            if (node.ModuleId == "studio-dialogue") { CurrentDialogueId = node.Description; ((StudioDocument)host.ActivateWorkspace(LibraryNode)).FilterLibrary(CurrentDialogueId); return true; }
            if (node.ModuleId == "dialogue-studio") { CurrentDialogueId = catalog.List().FirstOrDefault(r => r.RecordingId == node.ToolID)?.DialogueId ?? CurrentDialogueId; host.ActivateWorkspace(node); return true; }
            if (node.ModuleId == "studio-topic")
            {
                var parts = node.Description.Split('|');
                var recording = app.Navigation.LoadTree().Single(n => n.ToolID == parts[0]);
                CurrentDialogueId = catalog.List().Single(r => r.RecordingId == parts[0]).DialogueId;
                ((StudioDocument)host.ActivateWorkspace(recording)).SelectTopic(parts[1]);
                return true;
            }
            return false;
        }
        public void OpenLibrary() { host.ActivateWorkspace(LibraryNode); }
        public StudioDocument OpenRecording(Recording recording)
        {
            CurrentDialogueId = catalog.List().Single(r => r.RecordingId == recording.Id).DialogueId;
            Synchronize(); host.ReloadProductNavigation();
            var node = app.Navigation.LoadTree().Single(n => n.ToolID == recording.Id);
            return (StudioDocument)host.ActivateWorkspace(node);
        }
    }
}
