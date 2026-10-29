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
        public string Agent;        // sous-agent qui a fait l'action (null : l'agent principal)
        public string Spawned;      // ligne de lancement d'un sous-agent : son identifiant
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
        public const int KeepDays = 90;   // conservation du journal, et periode du rapport

        // Outils de lecture ou internes : enregistres, mais masques par defaut dans le rapport.
        public static readonly HashSet<string> Quiet = new HashSet<string>
            { "Read", "Grep", "Glob", "LS", "TodoWrite", "AskUserQuestion", "ExitPlanMode", "NotebookRead", "SubagentHandback" };

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
        internal static string HeadName(string root) { return "head-" + Sha(Path.GetFullPath(root).TrimEnd('\\').ToLowerInvariant()).Substring(0, 16); }

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

        // Fournisseur de Windows (CAPI) : ~5 ms au premier appel contre ~23 pour SHA256.Create(),
        // qui passe par CryptoConfig. Compatible avec le mode FIPS.
        public static string Sha(string line)
        {
            using (var h = new SHA256CryptoServiceProvider())
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
                case "Task": case "Agent": s = Json.Str(input, "description"); break;
                // Outils MCP et inconnus : les noms des parametres et des valeurs courtes, jamais
                // l'entree entiere (un corps de requete, un document peuvent porter un secret).
                default: s = input == null || input.Count == 0 ? null : string.Join(", ", input.Select(kv => kv.Key + "=" + Brief(kv.Value))); break;
            }
            s = Mask(s ?? "");
            return s.Length > 2000 ? s.Substring(0, 2000) + " …" : s;
        }

        // Masquee avant d'etre coupee : un secret coupe en deux echapperait aux motifs.
        static string Brief(object v)
        {
            var t = Mask(v as string ?? Json.Write(v));
            return t.Length > 80 ? t.Substring(0, 80) + "…" : t;
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
            // DB_PASS=..., STRIPE_KEY=... : variables en majuscules au nom de secret
            P(@"(\b[A-Z0-9_]*_(?:PASS|PASSWD|PWD|KEY|SECRET|TOKEN)\b[""']?\s*[=:]\s*)(""[^""]*""|'[^']*'|[^\s;&""']+)"),
            // mysql -pMotDePasse (colle a l'option ; "-p" seul demande le mot de passe)
            P(@"(\bmysql(?:dump|admin)?\b[^\r\n|;&]*?\s-p)(""[^""]*""|'[^']*'|[^\s""']+)"),
            // sshpass -p motdepasse
            P(@"(?i)(\bsshpass\s+(?:-\w+\s+)*?-p\s*)(""[^""]*""|'[^']*'|\S+)"),
            // ConvertTo-SecureString "motdepasse" -AsPlainText
            P(@"(?i)(ConvertTo-SecureString\s+(?:-String\s+)?)(""[^""]*""|'[^']*'|\S+)"),
            // Cle privee collee dans une commande : tout le bloc
            P(@"(-----BEGIN [A-Z ]*PRIVATE KEY-----)[\s\S]*?(?:-----END [A-Z ]*PRIVATE KEY-----|$)"),
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

        // Lignes brutes des N derniers jours. Le cache garde, par fichier, les lignes deja lues
        // et la position atteinte : un fichier qui grandit (celui du jour) n'est lu qu'a partir
        // de la. Les 31 derniers jours restent en cache ; au-dela (rapport sur 90 jours), lus a la demande.
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
                lock (_cache) _cache.TryGetValue(f, out c);
                if (c == null || c.Item1 != len)
                {
                    long off = c == null ? 0 : c.Item1;
                    var added = ReadFrom(f, ref off);
                    if (added == null) { c = null; off = 0; added = ReadFrom(f, ref off) ?? new List<Dictionary<string, object>>(); }   // fichier raccourci : relu en entier
                    var list = added;
                    if (c != null) { list = new List<Dictionary<string, object>>(c.Item2.Count + added.Count); list.AddRange(c.Item2); list.AddRange(added); }
                    c = Tuple.Create(off, list);
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

        // Lignes completes a partir de l'octet offset, qui avance jusqu'apres le dernier saut
        // de ligne (une ligne en cours d'ecriture sera lue la fois suivante). Null si le
        // fichier est plus court que offset.
        static List<Dictionary<string, object>> ReadFrom(string path, ref long offset)
        {
            var list = new List<Dictionary<string, object>>();
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length < offset) return null;
                    fs.Seek(offset, SeekOrigin.Begin);
                    var buf = new byte[fs.Length - offset];
                    int got = 0;
                    while (got < buf.Length) { int n = fs.Read(buf, got, buf.Length - got); if (n <= 0) break; got += n; }
                    int end = got == 0 ? -1 : Array.LastIndexOf(buf, (byte)'\n', got - 1);
                    if (end < 0) return list;
                    foreach (var raw in new UTF8Encoding(false).GetString(buf, 0, end + 1).Split('\n'))
                    {
                        var l = raw.TrimEnd('\r');
                        if (l.Length == 0) continue;
                        try { var d = Json.Obj(Json.Parse(l)); if (d != null) list.Add(d); } catch { }
                    }
                    offset += end + 1;
                }
            }
            catch (IOException) { }
            return list;
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
                    Host = Json.Str(d, "host"), Agent = Json.Str(d, "agent"), Rule = ev == "regle", Dec = ev == "regle" ? Json.Str(d, "dec") : null
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
                    case "fait": case "echec": case "refus-auto":
                        row.Result = ev;
                        if (ev == "fait" && Json.Str(d, "spawn") != null) row.Spawned = Json.Str(d, "spawn");
                        break;
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

        // ------------------------------------------------------------ cle de regle

        // Liste blanche : seules ces commandes (tests, compilation, verification, Git en lecture)
        // peuvent devenir une regle. Une liste d'exclusion ne serait jamais complete (env, xargs,
        // find -exec, sed...) : tout le reste n'est jamais suggere.
        static readonly HashSet<string> RuleAllowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "pytest", "jest", "vitest", "tsc", "eslint", "ruff", "mypy",
              "npm test", "pnpm test", "yarn test", "bun test",
              "dotnet test", "dotnet build", "cargo test", "cargo build", "cargo check", "cargo clippy",
              "go test", "go build", "go vet",
              "git status", "git diff", "git log", "git show" };

        // Scripts du projet : la regle doit nommer le script ("npm run build").
        static readonly HashSet<string> RuleScripts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "npm run", "pnpm run", "yarn run" };

        // "npm test -- --watch" -> "npm test" ; "pytest -q" -> "pytest". Null si la commande est
        // composee (&&, &, |, ;, >, <, saut de ligne...) ou hors liste blanche : pas de regle pour elle.
        public static string RuleKey(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            var c = command.Trim();
            if (Regex.IsMatch(c, @"[&|;<>`\r\n]|\$\(")) return null;
            var tok = c.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            // "git.exe status" = "git status"
            if (tok[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) tok[0] = tok[0].Substring(0, tok[0].Length - 4);
            if (tok.Length >= 2)
            {
                var two = tok[0] + " " + tok[1];
                if (RuleAllowed.Contains(two)) return two;
                if (RuleScripts.Contains(two))
                    return tok.Length >= 3 && Regex.IsMatch(tok[2], @"^[A-Za-z0-9][\w:.-]*$") ? two + " " + tok[2] : null;
            }
            return RuleAllowed.Contains(tok[0]) ? tok[0] : null;
        }
    }
}
