using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

record ArchiveDocument(string Id, string Title, string RelativePath, int Lines, long Bytes);
record ArchivePage(string DocumentId, string Title, int StartLine, int EndLine, int? NextLine, int TotalLines, string Text);
record SearchHit(string DocumentId, string Title, int StartLine, int EndLine, string Text, double Score);

sealed class Archive
{
    readonly string root;
    readonly object gate = new();
    Dictionary<string, (ArchiveDocument Info, string[] Lines)> documents = new();
    static readonly HashSet<string> Stop = new("a an the and or of to in on for with our me you we what how did is it that this about please nex grant".Split(' '));
    public Archive(string root) { this.root = root; Refresh(); }
    public void Refresh()
    {
        var fresh = new Dictionary<string, (ArchiveDocument, string[])>();
        var hashes = new HashSet<string>();
        foreach (var path in Directory.EnumerateFiles(root, "*.md", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var parts = relative.Split('/');
            if (parts.Any(p => p is ".git" or ".codex" or ".runtime" or "_sources" or "bin" or "obj" or "node_modules")) continue;
            if (relative.StartsWith("Nex Voice App/", StringComparison.OrdinalIgnoreCase) && !relative.StartsWith("Nex Voice App/Data/Conversations/", StringComparison.OrdinalIgnoreCase)) continue;
            if (Path.GetFileName(path).Equals("INDEX.md", StringComparison.OrdinalIgnoreCase)) continue;
            if (path.Contains(" - Relevant Excerpts.md") && File.Exists(path.Replace(" - Relevant Excerpts.md", " - Full Dialogue.md"))) continue;
            string text;
            try { text = File.ReadAllText(path); } catch (IOException) { continue; }
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            if (!hashes.Add(hash)) continue;
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(relative)))[..16].ToLowerInvariant();
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var title = lines.FirstOrDefault(l => l.StartsWith("# "))?[2..].Trim() ?? Path.GetFileNameWithoutExtension(path);
            fresh[id] = (new(id, title, relative, lines.Length, Encoding.UTF8.GetByteCount(text)), lines);
        }
        lock (gate) documents = fresh;
    }
    public ArchiveDocument[] List() { lock (gate) return documents.Values.Select(v => v.Info).OrderBy(d => d.Title).ToArray(); }
    public ArchivePage Read(string id, int start, int count)
    {
        lock (gate)
        {
            if (!documents.TryGetValue(id, out var doc)) throw new ArgumentException("Unknown document ID. Search or refresh the catalog first.");
            start = Math.Clamp(start, 1, doc.Lines.Length);
            count = count == int.MaxValue ? doc.Lines.Length : Math.Clamp(count, 1, 65);
            var end = (int)Math.Min((long)doc.Lines.Length, (long)start + count - 1);
            return new(id, doc.Info.Title, start, end, end < doc.Lines.Length ? end + 1 : null, doc.Lines.Length, string.Join('\n', doc.Lines.Skip(start - 1).Take(end - start + 1)));
        }
    }
    public SearchHit[] Search(string query)
    {
        if (query.Length > 1000) throw new ArgumentException("Use a shorter search query.");
        var terms = Regex.Matches(query.ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => m.Value).Where(w => w.Length > 1 && !Stop.Contains(w)).Distinct().ToArray();
        if (terms.Length == 0) return [];
        lock (gate)
        {
            var results = new List<SearchHit>();
            foreach (var doc in documents.Values)
            {
                for (var i = 0; i < doc.Lines.Length; i += 35)
                {
                    var text = string.Join('\n', doc.Lines.Skip(i).Take(50));
                    var lower = text.ToLowerInvariant();
                    double score = 0;
                    foreach (var term in terms)
                    {
                        var matches = Regex.Matches(lower, @"(?<![\p{L}\p{N}])" + Regex.Escape(term) + @"(?![\p{L}\p{N}])").Count;
                        if (matches > 0) score += 2 + Math.Log(1 + matches);
                        if (doc.Info.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 1;
                    }
                    if (score > 0) results.Add(new(doc.Info.Id, doc.Info.Title, i + 1, Math.Min(i + 50, doc.Lines.Length), text, score));
                }
            }
            return results.OrderByDescending(r => r.Score).Take(6).ToArray();
        }
    }
}
