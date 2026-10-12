// Journal d'audit : ce que les agents font, et qui l'a autorise.
//
// Ecrit par le hook (NotchHook.exe), donc meme encoche fermee ; lu par l'encoche
// (bilan, rapport, regles suggerees). Un fichier par jour, audit\AAAA-MM-JJ.jsonl,
// une ligne JSON par evenement. Chaque ligne porte l'empreinte SHA-256 de la ligne
// precedente ("prev") : une ligne modifiee ou supprimee apres coup casse la chaine.
//
// Evenements : outil (l'agent va utiliser un outil), demande (permission demandee),
// decision (reponse donnee dans Bip, ou renvoyee a Claude), fait / echec (resultat),
// refus-auto (refuse par le mode auto), regle (regle ajoutee ou retiree par Bip).
//
// Partage entre le hook et l'encoche : aucune dependance a l'interface ni a Program.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ClaudeNotch
{
    class AuditRow
    {
        public DateTime T;
        public string Sess, Dir, Proj, Tool, Obj, Tuid, Mode, User, Host;   // Proj : racine du projet
        public bool Asked;          // une permission a ete demandee
        public string Dec;          // allow / always / deny / claude (decision brute)
        public string Result;       // fait / echec / refus-auto
        public string Decision, Via, Outcome;   // libelles finaux (Finish)
        public bool Rule;           // ligne de regle (ajout / retrait)

        public string Project
        {
            get
            {
                if (string.IsNullOrEmpty(Dir)) return "—";
                var n = Path.GetFileName(Dir.TrimEnd('\\', '/'));
                return string.IsNullOrEmpty(n) ? Dir : n;
            }
        }
        public bool ByUser { get { return Via != null && Via.StartsWith("toi"); } }
        public bool Allowed { get { return Decision != null && Decision.StartsWith("autoris"); } }
        public bool Refused { get { return Decision != null && Decision.StartsWith("refus"); } }
        public bool Hidden { get { return !Rule && Audit.Quiet.Contains(Tool ?? ""); } }
    }

    static class Audit
    {
        // Outils de lecture ou internes : enregistres, mais masques par defaut dans le rapport.
        public static readonly HashSet<string> Quiet = new HashSet<string>
            { "Read", "Grep", "Glob", "LS", "TodoWrite", "AskUserQuestion", "ExitPlanMode", "NotebookRead" };

        static string DirOf(string root) { return Path.Combine(root, "audit"); }
        static string FileOf(string root, DateTime d) { return Path.Combine(DirOf(root), d.ToString("yyyy-MM-dd") + ".jsonl"); }

        // ------------------------------------------------------------ ecriture

        // Ajoute une ligne au journal du jour, chainee a la precedente. Le fichier est
        // ouvert en exclusif le temps de lire la derniere ligne et d'ecrire la nouvelle :
        // plusieurs hooks peuvent ecrire en meme temps.
        public static void Write(string root, string ev, Dictionary<string, object> fields)
        {
            Directory.CreateDirectory(DirOf(root));
            for (int attempt = 0; attempt < 60; attempt++)
            {
                try
                {
                    string path = FileOf(root, DateTime.Now);
                    using (var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
                    {
                        // Heure prise sous le verrou : on a pu attendre, et minuit a pu passer.
                        var now = DateTime.Now;
                        if (FileOf(root, now) != path) continue;
                        string last = LastLine(fs);
                        if (last == null) last = LastLineOfPreviousDay(root, now);
                        var rec = new Dictionary<string, object>
                        {
                            { "t", now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture) },
                            { "ev", ev },
                            { "user", Environment.UserName },
                            { "host", Environment.MachineName }
                        };
                        foreach (var kv in fields) if (kv.Value != null) rec[kv.Key] = kv.Value;
                        rec["prev"] = last == null ? "debut" : Sha(last);
                        string line = Json.Write(rec);
                        // Une ecriture precedente interrompue a pu laisser une ligne sans fin :
                        // la nouvelle ligne repart sur une ligne propre.
                        bool cut = fs.Length > 0 && LastByte(fs) != '\n';
                        var bytes = new UTF8Encoding(false).GetBytes((cut ? "\n" : "") + line + "\n");
                        fs.Seek(0, SeekOrigin.End);
                        fs.Write(bytes, 0, bytes.Length);
                        fs.Flush();
                        SaveHead(root, Sha(line));   // toujours sous le verrou : l'ordre des maillons est garanti
                        return;
                    }
                }
                catch (IOException) { Thread.Sleep(25); }
            }
            // Fichier tenu ouvert par un autre programme : l'evenement est perdu, on le dit.
            try { File.AppendAllText(Path.Combine(root, "notch.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  audit : écriture abandonnée (" + ev + "), journal verrouillé" + Environment.NewLine); }
            catch { }
        }

        // Le dernier maillon de la chaine est aussi garde hors du dossier (registre de
        // l'utilisateur) : une fin de journal tronquee ou un fichier du jour supprime se voit.
        const string HeadKey = @"Software\Bip\Audit";

        // Une valeur par dossier d'installation : deux installations (ou les tests) ne se melangent pas.
        static string HeadName(string root) { return "head-" + Sha(Path.GetFullPath(root).TrimEnd('\\').ToLowerInvariant()).Substring(0, 16); }

        static void SaveHead(string root, string sha)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(HeadKey))
                    if (k != null) k.SetValue(HeadName(root), sha);
            }
            catch { }
        }

        static string LoadHead(string root)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(HeadKey))
                    return k == null ? null : k.GetValue(HeadName(root)) as string;
            }
            catch { return null; }
        }

        // Derniere ligne non vide d'un fichier ouvert (lecture des 64 derniers Ko).
        static string LastLine(FileStream fs)
        {
            if (fs.Length == 0) return null;
            long start = Math.Max(0, fs.Length - 65536);
            fs.Seek(start, SeekOrigin.Begin);
            var buf = new byte[fs.Length - start];
            int got = 0;
            while (got < buf.Length) { int n = fs.Read(buf, got, buf.Length - got); if (n <= 0) break; got += n; }
            var text = Encoding.UTF8.GetString(buf, 0, got).TrimEnd('\n', '\r');
            int i = text.LastIndexOf('\n');
            return i < 0 ? text : text.Substring(i + 1);
        }

        static int LastByte(FileStream fs)
        {
            fs.Seek(-1, SeekOrigin.End);
            return fs.ReadByte();
        }

        // Le premier evenement du jour se chaine au dernier de la veille (ou du dernier jour ecrit).
        static string LastLineOfPreviousDay(string root, DateTime now)
        {
            var today = Path.GetFileName(FileOf(root, now));
            // Un fichier vide (cree juste avant minuit) est saute.
            foreach (var prev in Files(root).Where(f => string.CompareOrdinal(Path.GetFileName(f), today) < 0).Reverse())
            {
                try
                {
                    using (var fs = new FileStream(prev, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        var l = LastLine(fs);
                        if (!string.IsNullOrEmpty(l)) return l;
                    }
                }
                catch { return null; }
            }
            return null;
        }

        static IEnumerable<string> Files(string root)
        {
            try { return Directory.GetFiles(DirOf(root), "*.jsonl").OrderBy(f => f, StringComparer.Ordinal).ToList(); }
            catch { return new List<string>(); }
        }

        public static string Sha(string line)
        {
            using (var h = SHA256.Create())
                return string.Concat(h.ComputeHash(Encoding.UTF8.GetBytes(line)).Select(b => b.ToString("x2")));
        }

        public static void Purge(string root, int days)
        {
            var limit = DateTime.Today.AddDays(-days).ToString("yyyy-MM-dd");
            foreach (var f in Files(root))
                if (string.CompareOrdinal(Path.GetFileNameWithoutExtension(f), limit) < 0)
                    try { File.Delete(f); } catch { }
        }

        // ------------------------------------------------------------ ce qu'on enregistre

        // Resume lisible de l'objet d'un outil : la commande, le fichier, l'adresse...
        // Les secrets reconnaissables sont masques avant d'etre ecrits.
        public static string Summarize(string tool, Dictionary<string, object> input)
        {
            string s;
            switch (tool)
            {
                case "Bash": case "PowerShell": s = Json.Str(input, "command"); break;
                case "Edit": case "MultiEdit": case "Write": case "Read": s = Json.Str(input, "file_path"); break;
                case "NotebookEdit": case "NotebookRead": s = Json.Str(input, "notebook_path"); break;
                case "Glob": s = Json.Str(input, "pattern"); break;
                case "Grep": s = Json.Str(input, "pattern") + (Json.Str(input, "path") != null ? "  (" + Json.Str(input, "path") + ")" : ""); break;
                case "WebFetch": s = Json.Str(input, "url"); break;
                case "WebSearch": s = Json.Str(input, "query"); break;
                case "Task": s = Json.Str(input, "description"); break;
                default: s = input == null || input.Count == 0 ? null : Json.Write(input); break;
            }
            s = Mask(s ?? "");
            return s.Length > 2000 ? s.Substring(0, 2000) + " …" : s;
        }

        // Chaque motif garde son prefixe (groupe 1) et remplace le secret par [masqué].
        static readonly Tuple<Regex, string>[] SecretPatterns =
        {
            P(@"(?i)(bearer\s+)[A-Za-z0-9._~+/=-]{8,}"),
            P(@"(?i)(authorization\s*[:=]\s*)(?:basic|bearer|token)?\s*[^\s""']+"),
            P(@"(?i)((?:cookie|set-cookie|ocp-apim-subscription-key|x-api-key)\s*:\s*)[^\r\n""']+"),
            P(@"(sk-(?:ant-)?)[A-Za-z0-9_-]{12,}"),
            P(@"(gh[pousr]_|github_pat_)[A-Za-z0-9_]{12,}"),
            P(@"(AKIA)[0-9A-Z]{16}"),
            P(@"(xox[abprs]-)[A-Za-z0-9-]{10,}"),
            P(@"(AIza)[0-9A-Za-z_-]{35}"),
            P(@"([sr]k_(?:live|test)_)[A-Za-z0-9]{10,}"),
            P(@"(npm_|hf_|glpat-)[A-Za-z0-9_-]{16,}"),
            P(@"()eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}"),
            // Identifiants dans une adresse : scheme://utilisateur:motdepasse@hote
            Tuple.Create(new Regex(@"(://[^:@/\s]+:)[^@\s/]+(@)"), "$1[masqué]$2"),
            // curl -u utilisateur:motdepasse
            P(@"(\s-u\s+[^:\s]+:)\S+"),
            // NOM=valeur, NOM: valeur (y compris DB_PASSWORD, GITHUB_TOKEN, AccountKey=...)
            P(@"(?i)((?:password|passwd|pwd|secret|token|api[_-]?key|access[_-]?key|access[_-]?token|client[_-]?secret|accountkey|sharedaccesskey|subscription[_-]?key)[""']?\s*[=:]\s*)(""[^""]*""|'[^']*'|[^\s;&""']+)"),
            P(@"(?i)(--?(?:password|passwd|token|api-key|secret)[= ])(""[^""]*""|'[^']*'|\S+)"),
        };

        static Tuple<Regex, string> P(string pattern) { return Tuple.Create(new Regex(pattern), "$1[masqué]"); }

        public static string Mask(string s)
        {
            foreach (var p in SecretPatterns) s = p.Item1.Replace(s, p.Item2);
            return s;
        }

        // ------------------------------------------------------------ lecture

        static readonly Dictionary<string, Tuple<long, List<Dictionary<string, object>>>> _cache =
            new Dictionary<string, Tuple<long, List<Dictionary<string, object>>>>();

        // Lignes brutes des N derniers jours. Les 31 derniers jours restent en cache (fichiers
        // inchanges non relus) ; au-dela (rapport sur 90 jours), lus a la demande.
        static List<Dictionary<string, object>> Lines(string root, int days)
        {
            var limit = DateTime.Today.AddDays(-days + 1).ToString("yyyy-MM-dd");
            var keep = DateTime.Today.AddDays(-30).ToString("yyyy-MM-dd");
            var all = new List<Dictionary<string, object>>();
            foreach (var f in Files(root))
            {
                var day = Path.GetFileNameWithoutExtension(f);
                if (string.CompareOrdinal(day, limit) < 0) continue;
                long len;
                try { len = new FileInfo(f).Length; } catch { continue; }
                Tuple<long, List<Dictionary<string, object>>> c;
                lock (_cache)
                    if (!_cache.TryGetValue(f, out c) || c.Item1 != len) c = null;
                if (c == null)
                {
                    var list = new List<Dictionary<string, object>>();
                    foreach (var l in ReadAllLines(f))
                    {
                        try { var d = Json.Obj(Json.Parse(l)); if (d != null) list.Add(d); } catch { }
                    }
                    c = Tuple.Create(len, list);
                    lock (_cache)
                    {
                        foreach (var old in _cache.Keys.Where(k => string.CompareOrdinal(Path.GetFileNameWithoutExtension(k), keep) < 0).ToList())
                            _cache.Remove(old);
                        if (string.CompareOrdinal(day, keep) >= 0) _cache[f] = c;
                    }
                }
                all.AddRange(c.Item2);
            }
            return all;
        }

        static List<string> ReadAllLines(string path)
        {
            var lines = new List<string>();
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var r = new StreamReader(fs, new UTF8Encoding(false)))
                {
                    string l;
                    while ((l = r.ReadLine()) != null) if (l.Length > 0) lines.Add(l);
                }
            }
            catch { }
            return lines;
        }

        // Une ligne par action : on rassemble l'outil, la demande, la decision et le
        // resultat (par identifiant d'appel quand il existe, sinon par session, outil et objet).
        // Les hooks ecrivent en parallele : un resultat peut preceder son outil dans le
        // fichier. D'ou deux passes : toutes les actions d'abord, les rattachements ensuite.
        public static List<AuditRow> Rows(string root, int days)
        {
            var lines = Lines(root, days);
            var rows = new List<AuditRow>();
            var byTuid = new Dictionary<string, AuditRow>();
            var byWhat = new Dictionary<string, List<AuditRow>>();
            foreach (var d in lines)
            {
                string ev = Json.Str(d, "ev");
                if (ev != "outil" && ev != "regle") continue;
                string tuid = Json.Str(d, "tuid");
                var r = new AuditRow
                {
                    T = TimeOf(d), Sess = Json.Str(d, "sess"), Dir = Json.Str(d, "dir"), Proj = Json.Str(d, "proj") ?? Json.Str(d, "dir"), Tool = Json.Str(d, "tool"),
                    Obj = Json.Str(d, "obj"), Tuid = tuid, Mode = Json.Str(d, "mode"), User = Json.Str(d, "user"),
                    Host = Json.Str(d, "host"), Rule = ev == "regle", Dec = ev == "regle" ? Json.Str(d, "dec") : null
                };
                rows.Add(r);
                if (r.Rule) continue;
                if (!string.IsNullOrEmpty(tuid)) byTuid[tuid] = r;
                List<AuditRow> same;
                var what = r.Sess + "\u0001" + r.Tool + "\u0001" + r.Obj;
                if (!byWhat.TryGetValue(what, out same)) byWhat[what] = same = new List<AuditRow>();
                same.Add(r);
            }

            foreach (var d in lines)
            {
                string ev = Json.Str(d, "ev"), tuid = Json.Str(d, "tuid");
                if (ev == "outil" || ev == "regle") continue;

                AuditRow row = null;
                if (!string.IsNullOrEmpty(tuid)) byTuid.TryGetValue(tuid, out row);
                List<AuditRow> same;
                if (row == null && byWhat.TryGetValue(Json.Str(d, "sess") + "\u0001" + Json.Str(d, "tool") + "\u0001" + Json.Str(d, "obj"), out same))
                {
                    // Sans identifiant d'appel : l'action libre la plus proche dans le temps.
                    var t = TimeOf(d);
                    row = same.Where(c => ev == "demande" ? !c.Asked : ev == "decision" ? c.Dec == null : c.Result == null)
                              .OrderBy(c => Math.Abs((c.T - t).Ticks)).FirstOrDefault();
                }
                if (row == null) continue;

                switch (ev)
                {
                    case "demande": row.Asked = true; break;
                    case "decision": row.Asked = true; row.Dec = Json.Str(d, "dec"); break;
                    case "fait": case "echec": case "refus-auto": row.Result = ev; break;
                }
            }
            foreach (var r in rows) Finish(r);
            return rows;
        }

        static DateTime TimeOf(Dictionary<string, object> d)
        {
            DateTime t;
            DateTime.TryParse(Json.Str(d, "t"), CultureInfo.InvariantCulture, DateTimeStyles.None, out t);
            return t;
        }

        static void Finish(AuditRow r)
        {
            if (r.Rule)
            {
                r.Decision = r.Dec == "retiree" ? "règle retirée" : "règle ajoutée";
                r.Via = "toi, dans Bip";
                r.Outcome = "—";
                return;
            }
            r.Outcome = r.Result == "fait" ? "réussi" : r.Result == "echec" ? "échoué" : "—";
            if (r.Result == "refus-auto") { r.Decision = "refusé"; r.Via = "mode auto"; r.Outcome = "—"; return; }
            switch (r.Dec)
            {
                case "allow":  r.Decision = "autorisé"; r.Via = "toi, dans Bip"; return;
                case "always": r.Decision = "autorisé (+ règle de session)"; r.Via = "toi, dans Bip"; return;
                case "deny":   r.Decision = "refusé"; r.Via = "toi, dans Bip"; return;
            }
            if (r.Asked)
            {
                // Demande renvoyee a la fenetre Claude : executee = autorisee, sinon refusee ou annulee.
                r.Decision = r.Result != null ? "autorisé" : "refusé ou annulé";
                r.Via = "toi, dans Claude";
                return;
            }
            if (r.Result != null)
            {
                r.Decision = "autorisé d'office";
                r.Via = r.Mode == "auto" ? "mode auto" : r.Mode == "acceptEdits" ? "mode modifications acceptées"
                      : r.Mode == "bypassPermissions" ? "permissions ignorées" : "règle ou outil sans risque";
                return;
            }
            r.Decision = "en cours ou interrompu";
            r.Via = "—";
        }

        // ------------------------------------------------------------ verification de la chaine

        // Rejoue la chaine d'empreintes sur tous les fichiers conserves. Renvoie null si
        // elle est intacte, sinon l'endroit de la premiere rupture.
        public static string Verify(string root, out int count)
        {
            count = 0;
            string prevLine = null;
            bool first = true;
            foreach (var f in Files(root))
            {
                var lines = ReadAllLines(f);
                for (int i = 0; i < lines.Count; i++)
                {
                    count++;
                    string prev = null;
                    try { prev = Json.Str(Json.Obj(Json.Parse(lines[i])), "prev"); } catch { }
                    // Le tout premier evenement conserve peut suivre un jour deja purge.
                    bool ok = first ? prev != null : prev == Sha(prevLine);
                    if (!ok) return Path.GetFileName(f) + ", ligne " + (i + 1);
                    prevLine = lines[i];
                    first = false;
                }
            }
            var head = LoadHead(root);
            if (head != null && (prevLine == null || Sha(prevLine) != head))
                return "fin du journal : dernières lignes ou fichier du jour supprimés";
            return null;
        }

        // ------------------------------------------------------------ cle de regle et motifs risques

        // Commandes risquees : jamais de regle suggeree pour elles.
        static readonly Regex Risky = new Regex(
            @"(?i)\brm\s+-\w*[rf]|\bRemove-Item\b|\bdel\s+/|\brd\s+/s|\brmdir\s+/s|\bgit\s+(push|reset|clean|rebase|checkout\s+--|branch\s+-D)|" +
            @"\b(DROP|TRUNCATE|DELETE\s+FROM)\b|\bformat\s+[a-z]:|\b(curl|wget|iwr|Invoke-WebRequest|irm)\b.*\|\s*(sh|bash|iex|pwsh|powershell)\b|" +
            @"\bInvoke-Expression\b|\biex\b|\bsudo\b|\bchmod\s+-R|\bchown\b|\bmkfs\b|\bdd\s+if=|\bshutdown\b|\breg\s+(add|delete)\b|\bnpm\s+publish\b|" +
            @"\bdocker\s+(rm|rmi|system\s+prune|compose\s+(run|exec|down))\b|\bkubectl\s+delete\b|\bterraform\s+(apply|destroy)\b");

        static readonly HashSet<string> MultiWord = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "npm", "pnpm", "yarn", "bun", "git", "docker", "dotnet", "cargo", "go", "poetry", "uv", "kubectl", "make", "gradle", "mvn" };

        // Ces programmes ne deviennent jamais une regle a eux seuls : interpretes, shells,
        // reseau, suppression ou deplacement de fichiers, elevation, installation.
        static readonly HashSet<string> NeverAlone = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "git", "docker", "kubectl", "npm", "pnpm", "yarn", "bun", "dotnet", "cargo", "go", "uv", "poetry", "make", "gradle", "mvn",
              "rm", "del", "erase", "rmdir", "rd", "mv", "move", "ren", "rename", "cp", "copy", "xcopy", "robocopy",
              "node", "python", "python3", "py", "ruby", "perl", "php", "java", "deno", "npx", "pip", "pip3", "pipx",
              "cmd", "cmd.exe", "pwsh", "powershell", "bash", "sh", "zsh", "wsl", "start", "Start-Process", "Invoke-Expression", "iex",
              "curl", "wget", "iwr", "irm", "Invoke-WebRequest", "Invoke-RestMethod", "ssh", "scp", "sftp", "ftp", "nc", "telnet",
              "sudo", "runas", "su", "chmod", "chown", "icacls", "takeown", "reg", "regedit", "sc", "schtasks", "net", "netsh",
              "winget", "choco", "scoop", "msiexec", "Remove-Item", "Move-Item", "Copy-Item", "Set-Content", "Out-File", "Add-Content" };

        // Sous-commandes qui executent ou installent n'importe quoi : la regle doit nommer
        // le script ("npm run test"), sinon pas de regle.
        static readonly HashSet<string> NeedsTarget = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "npm run", "npm exec", "npm x", "npm install", "npm i", "npm add", "pnpm run", "pnpm exec", "pnpm dlx", "pnpm add", "pnpm install",
              "yarn run", "yarn exec", "yarn dlx", "yarn add", "bun run", "bun x", "bun add", "dotnet run", "dotnet tool", "cargo run", "cargo install",
              "go run", "go install", "uv run", "uv pip", "uv tool", "poetry run", "poetry add", "docker run", "docker exec", "docker compose",
              "kubectl exec", "kubectl apply" };

        // "npm test -- --watch" -> "npm test" ; "pytest -q" -> "pytest". Null si la commande
        // est composee (&&, |, ;, >) ou risquee : pas de regle pour elle.
        public static string RuleKey(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            var c = command.Trim();
            if (Regex.IsMatch(c, @"&&|\|\||[|;><`]|\$\(") || Risky.IsMatch(c)) return null;
            var tok = c.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length == 0) return null;
            string key = tok[0];
            if (MultiWord.Contains(tok[0]))
            {
                // "git --no-pager log" ou "git -C x status" : pas de regle (elle vaudrait "git" entier).
                if (tok.Length < 2 || tok[1].StartsWith("-")) return null;
                key = tok[0] + " " + tok[1];
                if (NeedsTarget.Contains(key))
                {
                    if (tok.Length < 3 || tok[2].StartsWith("-")) return null;
                    key += " " + tok[2];
                }
                if (Risky.IsMatch(key)) return null;
                return key;
            }
            if (NeverAlone.Contains(key) || key.Contains("\\") || key.Contains("/")) return null;
            return key;
        }
    }
}
