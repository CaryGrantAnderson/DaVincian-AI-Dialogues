using System.Text.Json;

static class SelfTests
{
    public static void Run()
    {
        var temp = Path.Combine(AppContext.BaseDirectory, "self-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
        var file = Path.Combine(temp, "Triune - Full Dialogue.md");
        var text = "# Teaching architecture\n\nGrant: The Triune has three base apexes.\n\nNex: That corrects my earlier count.\n" + string.Join('\n', Enumerable.Range(1, 180).Select(i => "Preserved source line " + i));
        File.WriteAllText(file, text);
        File.WriteAllText(Path.Combine(temp, "Triune - Relevant Excerpts.md"), "# Excerpt\nTriune");
        File.WriteAllText(Path.Combine(temp, "duplicate.md"), text);
        var archive = new Archive(temp);
        Check(archive.List().Length == 1, "full dialogue preferred; exact duplicate omitted");
        var hits = archive.Search("Triune apexes");
        Check(hits.Length > 0 && hits[0].Text.Contains("corrects my earlier count"), "retrieval includes original statement and correction with source lines");
        var id = archive.List()[0].Id;
        var page = archive.Read(id, 1, 65);
        var next = archive.Read(id, page.NextLine!.Value, 65);
        Check(page.EndLine + 1 == next.StartLine && page.TotalLines > next.EndLine, "full documents page continuously without dropping lines");
        var rejected = false;
        try { archive.Read("../../secrets", 1, 10); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "document reads only accept indexed IDs");
        var store = new Conversations(Path.Combine(temp, "Data"));
        var c = store.Create("Unicode continuity — Grant & Nex");
        var e1 = new TranscriptEvent("a", "session.input_transcript.delta", "Our Triune", 0, 100, "call1");
        var e2 = e1 with { EventId = "b", Delta = " includes three concepts.", StartMs = 100, EndMs = 300 };
        store.Append(c.Id, e1); store.Append(c.Id, e2); store.Append(c.Id, e2);
        store.Append(c.Id, e1 with { EventId = "c", Type = "session.output_transcript.delta", Delta = "Understood, Grant.", StartMs = 500, EndMs = 700 });
        var reopened = new Conversations(Path.Combine(temp, "Data"));
        Check(reopened.Get(c.Id).Events.Length == 3, "duplicate delivery idempotent and events survive store restart");
        Check(reopened.Markdown(c.Id).Contains("Our Triune includes three concepts."), "caption spaces and speaker attribution preserved");
        Check(reopened.Markdown(c.Id).Contains("## Nex"), "assistant speech exported to Markdown");
        reopened.Append(c.Id, e1 with { ConnectionId = "call2", Delta = "Resumed." });
        Check(reopened.Get(c.Id).Events.Length == 4, "new call on existing conversation preserves earlier history");
        reopened.SaveNotes("Decision: preserve Head, Heart, and Hara.");
        Check(new Conversations(Path.Combine(temp, "Data")).Notes().Contains("Hara"), "continuity notes survive restart");
        rejected = false;
        try { reopened.Get("../outside"); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "conversation paths cannot escape store");
        Check(File.ReadAllText(file) == text, "original archive unchanged");
        // Tests deliberately retain their tiny fixture folder as inspectable evidence.
        Console.WriteLine("Self-test evidence: " + temp);
    }
}
