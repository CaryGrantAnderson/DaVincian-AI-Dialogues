using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

record TranscriptEvent(string EventId, string Type, string Delta, double StartMs, double EndMs, string ConnectionId);
record ConversationInfo(string Id, string Title, DateTimeOffset Created, DateTimeOffset Updated);
record ConversationDetail(ConversationInfo Info, TranscriptEvent[] Events);

sealed class Conversations
{
    readonly string folder;
    readonly string notesPath;
    readonly object gate = new();
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    public Conversations(string data)
    {
        folder = Path.Combine(data, "Conversations");
        notesPath = Path.Combine(data, "Continuity.md");
        Directory.CreateDirectory(folder);
    }
    string FilePath(string id, string ext)
    {
        if (!Regex.IsMatch(id, @"\A[0-9a-f]{32}\z")) throw new ArgumentException("Invalid conversation ID.");
        return Path.Combine(folder, id + ext);
    }
    static void Atomic(string path, string text)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(false));
        File.Move(temp, path, true);
    }
    public ConversationInfo Create(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 160) throw new ArgumentException("Give the conversation a title of 1–160 characters.");
        lock (gate)
        {
            var info = new ConversationInfo(Guid.NewGuid().ToString("N"), title.Replace('\n', ' ').Replace('\r', ' '), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            Atomic(FilePath(info.Id, ".json"), JsonSerializer.Serialize(info, Json));
            File.WriteAllText(FilePath(info.Id, ".jsonl"), "");
            Atomic(FilePath(info.Id, ".md"), Markdown(info.Id));
            return info;
        }
    }
    public ConversationInfo[] List()
    {
        lock (gate) return Directory.EnumerateFiles(folder, "*.json").Select(p => JsonSerializer.Deserialize<ConversationInfo>(File.ReadAllText(p), Json)!).OrderByDescending(c => c.Updated).ToArray();
    }
    public ConversationDetail Get(string id)
    {
        lock (gate)
        {
            var path = FilePath(id, ".json");
            if (!File.Exists(path)) throw new ArgumentException("Conversation not found.");
            var info = JsonSerializer.Deserialize<ConversationInfo>(File.ReadAllText(path), Json)!;
            var events = new List<TranscriptEvent>();
            foreach (var line in File.ReadLines(FilePath(id, ".jsonl")))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try { events.Add(JsonSerializer.Deserialize<TranscriptEvent>(line, Json)!); }
                catch (JsonException) { throw new InvalidOperationException("A transcript journal is damaged. Keep the JSONL file for recovery; no data was overwritten."); }
            }
            return new(info, events.ToArray());
        }
    }
    public void Append(string id, TranscriptEvent item)
    {
        if (item.Type is not ("session.input_transcript.delta" or "session.output_transcript.delta")) throw new ArgumentException("Unsupported transcript event.");
        if (string.IsNullOrEmpty(item.EventId) || item.EventId.Length > 200 || string.IsNullOrEmpty(item.ConnectionId) || item.ConnectionId.Length > 100 || item.Delta is null || item.Delta.Length > 100_000 || !double.IsFinite(item.StartMs) || !double.IsFinite(item.EndMs) || item.StartMs < 0 || item.EndMs < item.StartMs) throw new ArgumentException("Invalid caption fragment.");
        lock (gate)
        {
            var doc = Get(id);
            if (doc.Events.Any(e => e.ConnectionId == item.ConnectionId && e.EventId == item.EventId))
            {
                // A retry may follow a successful journal flush but failed Markdown update.
                Atomic(FilePath(id, ".md"), Markdown(id));
                return;
            }
            // Flush the append-only journal before reporting a successful save.
            using (var stream = new FileStream(FilePath(id, ".jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(item, Json) + "\n");
                stream.Write(bytes); stream.Flush(true);
            }
            Atomic(FilePath(id, ".json"), JsonSerializer.Serialize(doc.Info with { Updated = DateTimeOffset.UtcNow }, Json));
            Atomic(FilePath(id, ".md"), Markdown(id));
        }
    }
    public string Markdown(string id)
    {
        lock (gate)
        {
            var doc = Get(id);
            var b = new StringBuilder($"# {doc.Info.Title}\n\nCreated: {doc.Info.Created:O}\n\nAutomatically saved speech transcripts. Captions may contain recognition errors; overlapping speech is grouped approximately. Audio is not recorded.\n\n");
            string? lastRole = null, lastConnection = null;
            double lastEnd = -1;
            var connections = doc.Events.Select(e => e.ConnectionId).Distinct().Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
            foreach (var e in doc.Events.OrderBy(e => connections[e.ConnectionId]).ThenBy(e => e.StartMs))
            {
                var role = e.Type == "session.input_transcript.delta" ? "Grant" : "Nex";
                if (role != lastRole || e.ConnectionId != lastConnection || e.StartMs - lastEnd > 4000)
                    b.Append($"\n\n## {role} · {TimeSpan.FromMilliseconds(e.StartMs):hh\\:mm\\:ss}\n\n");
                b.Append(e.Delta); // Do not trim: caption deltas carry meaningful spaces.
                lastRole = role; lastConnection = e.ConnectionId; lastEnd = e.EndMs;
            }
            return b.ToString() + "\n";
        }
    }
    public string RecentText(string id, int chars)
    {
        var text = Markdown(id);
        return text.Length <= chars ? text : "[Older history available through archive search.]\n" + text[^chars..];
    }
    public string Notes() { lock (gate) return File.Exists(notesPath) ? File.ReadAllText(notesPath) : ""; }
    public void SaveNotes(string text)
    {
        if (text is null || text.Length > 20_000) throw new ArgumentException("Continuity notes must be at most 20,000 characters.");
        lock (gate) Atomic(notesPath, text);
    }
}
