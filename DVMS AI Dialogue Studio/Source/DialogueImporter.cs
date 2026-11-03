using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DVMS.AIDialogueStudio
{
    public static class DialogueImporter
    {
        // Deliberately recognize explicit speaker headings only. Unknown layouts remain
        // one intact imported contribution so text is never silently discarded.
        static readonly Regex Speakers = new Regex(@"(?m)^#{2,3}[\t ]+(?:\d+\.[\t ]*)?(Grant|Nex|User|Assistant|You|ChatGPT)[\t ]*\r?$", RegexOptions.IgnoreCase);
        static readonly Regex NumberedSpeakers = new Regex(@"(?m)^##[\t ]+\d+\.[\t ]*(Grant|Nex|User|Assistant|You|ChatGPT)[\t ]*\r?$", RegexOptions.IgnoreCase);
        public static List<DialogueItem> Parse(string text)
        {
            var numbered = NumberedSpeakers.Matches(text).Cast<Match>().ToList();
            var matches = numbered.Count > 0 ? numbered : Speakers.Matches(text).Cast<Match>().ToList();
            var result = new List<DialogueItem>();
            string exchange = Guid.NewGuid().ToString("N");
            if (matches.Count == 0)
                return new List<DialogueItem> { new DialogueItem { ExchangeId = exchange, Speaker = "Imported", Text = text, OriginalText = text } };
            // Keep the export metadata and preamble as a source passage as well.
            var preamble = text.Substring(0, matches[0].Index).Trim();
            if (preamble.Length > 0) result.Add(new DialogueItem { ExchangeId = Guid.NewGuid().ToString("N"), Speaker = "Source context", Text = preamble, OriginalText = preamble });
            for (int i = 0; i < matches.Count; i++)
            {
                var m = matches[i];
                var speaker = m.Groups[1].Value;
                bool user = new[] { "Grant", "User", "You" }.Contains(speaker, StringComparer.OrdinalIgnoreCase);
                if (user) exchange = Guid.NewGuid().ToString("N");
                int start = m.Index + m.Length;
                int end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
                var body = text.Substring(start, end - start).Trim();
                result.Add(new DialogueItem { ExchangeId = exchange, Speaker = user ? "Grant" : "Nex", Text = body, OriginalText = body });
            }
            return result;
        }
    }
}
