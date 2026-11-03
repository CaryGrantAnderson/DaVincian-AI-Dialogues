using System;
using System.Collections.Generic;

namespace DVMS.AIDialogueStudio
{
    public sealed class Recording
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public int Generation { get; set; }
        public string RevisionId { get; set; }
        public string Folder { get; set; }
        public List<DialogueItem> Items { get; set; } = new List<DialogueItem>();
        public List<Topic> Topics { get; set; } = new List<Topic>();
    }
    public sealed class DialogueItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string UtteranceId { get; set; }
        public string ExchangeId { get; set; }
        public string Speaker { get; set; }
        public string Text { get; set; }
        public string OriginalText { get; set; }
        public string TopicId { get; set; }
        public int Position { get; set; }
        public string SourceId { get; set; }
    }
    public sealed class Topic
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string ParentId { get; set; }
        public string Title { get; set; }
        public override string ToString() { return Title; }
    }
    public sealed class CatalogEntry
    {
        public string DialogueId { get; set; }
        public string RecordingId { get; set; }
        public string Title { get; set; }
        public string DatabasePath { get; set; }
        public string OpenedUtc { get; set; }
    }
    public sealed class RevisionInfo
    {
        public string Id { get; set; }
        public int Number { get; set; }
        public string CreatedUtc { get; set; }
        public string Note { get; set; }
        public override string ToString() { return "Revision " + Number + " · " + CreatedUtc + " · " + Note; }
    }
}
