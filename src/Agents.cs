// Sous-agents et titre des sessions Claude, lus par l'encoche (et le rapport d'audit).
//
// Sous-agents : state\<session>.agents, ecrit par NotchHook (voir TrackAgentTool). Le lancement
// donne la description, SubagentStart et SubagentStop l'identifiant, le debut et la vraie fin.
// En arriere-plan, la reponse de l'outil Agent relie tout de suite l'appel a l'identifiant ; au
// premier plan, l'identifiant n'arrive qu'avec SubagentStart : on apparie par ordre de lancement.
//
// Titre : la derniere ligne "custom-title" du transcript de la session, lue depuis la fin la
// premiere fois, puis seulement dans les nouvelles lignes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeNotch
{
    class SubAgent
    {
        public string Tuid, Id, Description;
        public DateTime Start;
        public DateTime? End;
        public bool Failed;
    }

    static class Agents
    {
        // Lignes "type|ticks|..." -> sous-agents dans l'ordre de lancement.
        public static List<SubAgent> Parse(IEnumerable<string> lines)
        {
            var launched = new List<SubAgent>();
            var idOf = new Dictionary<string, string>();          // tool_use_id -> agent_id
            var started = new List<string>();                     // agent_id, dans l'ordre
            var ends = new Dictionary<string, DateTime>();        // agent_id -> fin
            var failed = new Dictionary<string, DateTime>();      // tool_use_id -> echec
            foreach (var l in lines)
            {
                var p = l.Split(new[] { '|' }, 4);
                long ticks;
                if (p.Length < 3 || !long.TryParse(p[1], out ticks)) continue;
                var t = new DateTime(ticks);
                switch (p[0])
                {
                    case "lance": if (p.Length == 4) launched.Add(new SubAgent { Tuid = p[2], Description = p[3], Start = t }); break;
                    case "id":    if (p.Length == 4) idOf[p[2]] = p[3]; break;
                    case "debut": if (!started.Contains(p[2])) started.Add(p[2]); break;
                    case "fin":   if (!ends.ContainsKey(p[2])) ends[p[2]] = t; break;
                    case "echec": failed[p[2]] = t; break;
                }
            }

            foreach (var a in launched) { string id; if (idOf.TryGetValue(a.Tuid, out id)) a.Id = id; }
            // Premier plan : les debuts sans appel connu vont, dans l'ordre, aux lancements sans identifiant.
            var known = new HashSet<string>(idOf.Values);
            var freeStarts = new Queue<string>(started.Where(s => !known.Contains(s)));
            foreach (var a in launched.Where(x => x.Id == null))
                if (freeStarts.Count > 0) a.Id = freeStarts.Dequeue();

            foreach (var a in launched)
            {
                DateTime t;
                if (a.Id != null && ends.TryGetValue(a.Id, out t)) a.End = t;
                if (failed.TryGetValue(a.Tuid, out t)) { a.End = t; a.Failed = true; }
            }
            return launched;
        }

        public static List<SubAgent> Load(string stateDir, string sessionId)
        {
            try
            {
                var f = Path.Combine(stateDir, sessionId + ".agents");
                if (!File.Exists(f)) return new List<SubAgent>();
                using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs, Encoding.UTF8))
                    return Parse(r.ReadToEnd().Split('\n'));
            }
            catch { return new List<SubAgent>(); }
        }

        // ------------------------------------------------------------ titre de la session

        static readonly Regex TitleRx = new Regex("\"customTitle\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        const int CHUNK = 256 * 1024, MAX_BACK = 8 * 1024 * 1024;
        // transcript -> (octets deja lus, dernier titre)
        static readonly Dictionary<string, Tuple<long, string>> _titles = new Dictionary<string, Tuple<long, string>>(StringComparer.OrdinalIgnoreCase);

        // Dernier titre d'un morceau de transcript, ou null.
        public static string LastTitle(string text)
        {
            Match last = null;
            foreach (Match m in TitleRx.Matches(text)) last = m;
            if (last == null) return null;
            try { return Json.Parse("\"" + last.Groups[1].Value + "\"") as string; }
            catch { return null; }
        }

        public static string Title(string transcript)
        {
            if (string.IsNullOrEmpty(transcript)) return null;
            try
            {
                using (var fs = new FileStream(transcript, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long len = fs.Length;
                    Tuple<long, string> c;
                    lock (_titles) _titles.TryGetValue(transcript, out c);
                    if (c != null && c.Item1 == len) return c.Item2;

                    string title = c == null ? null : c.Item2;
                    long read;
                    if (c != null && len > c.Item1)
                    {
                        // Nouvelles lignes seulement (un renommage y apparait).
                        read = c.Item1;
                        var buf = ReadRange(fs, read, len - read);
                        int end = Array.LastIndexOf(buf, (byte)'\n');
                        if (end >= 0)
                        {
                            title = LastTitle(Encoding.UTF8.GetString(buf, 0, end + 1)) ?? title;
                            read += end + 1;
                        }
                    }
                    else
                    {
                        // Premiere lecture (ou fichier raccourci) : depuis la fin, par blocs.
                        title = null;
                        read = len;
                        for (long pos = len; pos > 0 && len - pos < MAX_BACK && title == null; )
                        {
                            long start = Math.Max(0, pos - CHUNK);
                            // Recouvrement de 4 Ko : une ligne de titre a cheval sur deux blocs.
                            var buf = ReadRange(fs, start, Math.Min(len, pos + 4096) - start);
                            title = LastTitle(Encoding.UTF8.GetString(buf));
                            pos = start;
                        }
                    }
                    lock (_titles) _titles[transcript] = Tuple.Create(read, title);
                    return title;
                }
            }
            catch { return null; }
        }

        static byte[] ReadRange(FileStream fs, long start, long count)
        {
            var buf = new byte[count];
            fs.Seek(start, SeekOrigin.Begin);
            int got = 0;
            while (got < count) { int n = fs.Read(buf, got, (int)count - got); if (n <= 0) break; got += n; }
            if (got < count) Array.Resize(ref buf, got);
            return buf;
        }
    }
}
