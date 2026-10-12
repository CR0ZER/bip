// Modele : sessions lues dans state\, demandes en attente recues par le pipe,
// et reglages. Aucune dependance a l'interface.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace ClaudeNotch
{
    enum St { Stale = 0, Ready = 1, Busy = 2, Error = 3, Waiting = 4, Idle = 5 }

    static class States
    {
        public static St Parse(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "busy":    return St.Busy;
                case "waiting": return St.Waiting;
                case "error":   return St.Error;
                case "idle":    return St.Idle;
                default:        return St.Ready;
            }
        }

        public static string Label(St s)
        {
            switch (s)
            {
                case St.Ready:   return "prêt";
                case St.Busy:    return "en cours";
                case St.Waiting: return "en attente";
                case St.Error:   return "erreur";
                case St.Idle:    return "au repos";
                default:         return "inactif";
            }
        }
    }

    class Session
    {
        public string Id;
        public string Dir;
        public string Name;
        public St State;
        public DateTime Since;   // entree dans l'etat courant
        public DateTime Order;   // lancement de la session : ordre stable dans l'encoche

        public TimeSpan Elapsed { get { return DateTime.Now - Since; } }

        public static string NameOf(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "session";
            var d = dir.TrimEnd('\\', '/');
            var n = Path.GetFileName(d);
            return string.IsNullOrEmpty(n) ? d : n;
        }
    }

    // ------------------------------------------------------------ reglages

    class Config
    {
        readonly string _path;
        public bool Flash = true;

        public Config(string path)
        {
            _path = path;
            try
            {
                if (!File.Exists(path)) return;
                foreach (var line in File.ReadAllLines(path))
                {
                    var kv = line.Split(new[] { '=' }, 2);
                    if (kv.Length != 2) continue;
                    var k = kv[0].Trim(); var v = kv[1].Trim();
                    if (k == "flash") Flash = v != "0";
                }
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(_path, "flash=" + (Flash ? "1" : "0") + Environment.NewLine);
            }
            catch { }
        }
    }

    // ------------------------------------------------------------ sessions

    static class Sessions
    {
        static readonly TimeSpan STALE_BUSY    = TimeSpan.FromMinutes(20);
        // Une interruption (Echap) n'emet pas de Stop : sans ce plafond, une session
        // vivante resterait "en cours" indefiniment. Chaque outil reecrit l'etat.
        static readonly TimeSpan STALE_BUSY_ALIVE = TimeSpan.FromHours(1);
        static readonly TimeSpan STALE_WAITING = TimeSpan.FromHours(6);
        static readonly TimeSpan STALE_READY   = TimeSpan.FromMinutes(4);   // pret / au repos, puis le Bip s'efface
        static readonly TimeSpan PURGE_AGE     = TimeSpan.FromHours(12);

        // Ce qui merite une place dans l'encoche ("pret" s'efface au bout de 10 min).
        public static bool IsActive(St s) { return s != St.Stale; }

        // Memes regles que ClaudeVoyant : preuve de vie par PID, sinon anciennete.
        public static List<Session> Load(string stateDir)
        {
            var list = new List<Session>();
            string[] files;
            try { files = Directory.GetFiles(stateDir, "*.state"); }
            catch { return null; }

            foreach (var f in files)
            {
                try
                {
                    var raw = ReadShared(f);
                    if (raw.Length == 0) continue;

                    var parts = raw.Split(new[] { '|' }, 3);
                    var st = States.Parse(parts[0]);
                    var dir = parts.Length > 1 ? parts[1].Trim() : "";
                    var since = File.GetLastWriteTime(f);
                    int pid = 0;
                    if (parts.Length > 2) int.TryParse(parts[2].Trim(), out pid);

                    if (pid > 0 && !ClaudeAlive(pid)) st = St.Stale;
                    else
                    {
                        var age = DateTime.Now - since;
                        if (st == St.Busy && age > (pid == 0 ? STALE_BUSY : STALE_BUSY_ALIVE)) st = St.Stale;
                        else if (st == St.Waiting && age > STALE_WAITING) st = St.Stale;
                        else if ((st == St.Ready || st == St.Idle) && age > STALE_READY) st = St.Stale;
                    }

                    list.Add(new Session
                    {
                        Id = Path.GetFileNameWithoutExtension(f),
                        Dir = dir,
                        Name = Session.NameOf(dir),
                        State = st,
                        Since = since,
                        Order = File.GetCreationTime(f)
                    });
                }
                catch { }
            }

            list.Sort((a, b) => a.Order.CompareTo(b.Order));
            return list;
        }

        // Lecture partagee : un hook peut ecrire le fichier au meme instant. Un fichier
        // vu vide (en cours de reecriture) est relu une fois.
        static string ReadShared(string path)
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var r = new StreamReader(fs, Encoding.UTF8))
                    {
                        var t = r.ReadToEnd().Trim();
                        if (t.Length > 0) return t;
                    }
                }
                catch (IOException) { }
                Thread.Sleep(20);
            }
            return "";
        }

        public static void Purge(string stateDir)
        {
            try
            {
                foreach (var f in Directory.GetFiles(stateDir, "*.state"))
                    if (DateTime.Now - File.GetLastWriteTime(f) > PURGE_AGE) File.Delete(f);
            }
            catch { }
        }

        // Enumerer les processus coute ~10 ms : cache de 3 s.
        static HashSet<int> _livePids;
        static DateTime _pidsAt = DateTime.MinValue;

        static bool ClaudeAlive(int pid)
        {
            if (_livePids == null || DateTime.Now - _pidsAt > TimeSpan.FromSeconds(3))
            {
                var set = new HashSet<int>();
                try
                {
                    foreach (var p in Process.GetProcessesByName("claude")) { set.Add(p.Id); p.Dispose(); }
                }
                catch { return true; }   // enumeration impossible : on ne declare personne mort a tort
                _livePids = set;
                _pidsAt = DateTime.Now;
            }
            return _livePids.Contains(pid);
        }
    }

    // ------------------------------------------------------------ demandes en attente

    // Une demande de permission ou une question, bloquee cote hook jusqu'a la reponse.
    class Pending
    {
        public string Kind;                       // "permission" | "ask"
        public string SessionId;
        public string Cwd;
        public Dictionary<string, object> Payload;
        public DateTime Since = DateTime.Now;

        public string ToolName { get { return Json.Str(Payload, "tool_name") ?? ""; } }
        public Dictionary<string, object> ToolInput { get { return Json.Obj(Json.Get(Payload, "tool_input")) ?? new Dictionary<string, object>(); } }

        internal Action<string> Send;             // ecrit la reponse dans le pipe (une seule fois)
        int _done;

        public bool Done { get { return _done != 0; } }

        // Reponse vide = Claude pose la question lui-meme.
        public void Reply(string hookStdout)
        {
            if (Interlocked.Exchange(ref _done, 1) != 0) return;
            try { Send(hookStdout ?? ""); } catch { }
        }

        internal void MarkGone() { Interlocked.Exchange(ref _done, 1); }
    }

    static class Replies
    {
        public static string Allow()
        {
            return Json.Write(Hso("PermissionRequest", "decision", new Dictionary<string, object> { { "behavior", "allow" } }));
        }

        // "Toujours" : une seule des suggestions de Claude Code, celle dont l'utilisateur
        // a lu la portee sur le bouton, limitee a la session.
        public static string AllowAlways(Dictionary<string, object> suggestion)
        {
            var copy = new Dictionary<string, object>(suggestion);
            copy["destination"] = "session";
            var decision = new Dictionary<string, object>
            {
                { "behavior", "allow" },
                { "updatedPermissions", new object[] { copy } }
            };
            return Json.Write(Hso("PermissionRequest", "decision", decision));
        }

        // Libelle du bouton, ou null pour un type de suggestion inconnu (non propose).
        public static string AlwaysLabel(Dictionary<string, object> s)
        {
            switch (Json.Str(s, "type"))
            {
                case "setMode":
                    var mode = Json.Str(s, "mode");
                    return mode == "acceptEdits" ? "Toujours : toutes les modifications de fichiers"
                                                 : "Toujours : mode « " + mode + " »";
                case "addRules":
                    var rules = new List<string>();
                    foreach (var r in Json.Arr(Json.Get(s, "rules")) ?? new object[0])
                    {
                        var rd = Json.Obj(r);
                        var tool = Json.Str(rd, "toolName");
                        if (string.IsNullOrEmpty(tool)) continue;
                        var content = Json.Str(rd, "ruleContent");
                        rules.Add(string.IsNullOrEmpty(content) ? tool : tool + "(" + content + ")");
                    }
                    return rules.Count == 0 ? null : "Toujours : " + string.Join(", ", rules);
                case "addDirectories":
                    var dirs = (Json.Arr(Json.Get(s, "directories")) ?? new object[0]).Select(d => Convert.ToString(d)).ToList();
                    return dirs.Count == 0 ? null : "Toujours : accès à " + string.Join(", ", dirs);
                default:
                    return null;
            }
        }

        public static string Deny()
        {
            return Json.Write(Hso("PermissionRequest", "decision", new Dictionary<string, object>
            {
                { "behavior", "deny" },
                { "message", "Refusé par l'utilisateur depuis ClaudeNotch." }
            }));
        }

        // AskUserQuestion : on reprend l'entree d'origine et on y ajoute les reponses,
        // indexees par le texte de la question (choix multiples joints par ", ").
        public static string Answers(Pending p, Dictionary<string, string> answers)
        {
            var input = new Dictionary<string, object>(p.ToolInput);
            var a = new Dictionary<string, object>();
            foreach (var kv in answers) a[kv.Key] = kv.Value;
            input["answers"] = a;
            var hso = new Dictionary<string, object>
            {
                { "hookEventName", "PreToolUse" },
                { "permissionDecision", "allow" },
                { "updatedInput", input }
            };
            return Json.Write(new Dictionary<string, object> { { "hookSpecificOutput", hso } });
        }

        static Dictionary<string, object> Hso(string evt, string key, object value)
        {
            return new Dictionary<string, object>
            {
                { "hookSpecificOutput", new Dictionary<string, object> { { "hookEventName", evt }, { key, value } } }
            };
        }
    }

    // ------------------------------------------------------------ pipe

    // Un pipe nomme reserve au compte courant. Chaque connexion = une demande :
    // "<mode> <taille>\n<json>", puis une ligne de reponse. Si le hook disparait
    // (session interrompue, timeout), la demande est retiree.
    class PipeServer
    {
        public static string Name { get { return "ClaudeNotch-" + WindowsIdentity.GetCurrent().User.Value; } }

        readonly Action<Pending> _onNew, _onGone;
        readonly Action<string> _log;

        public PipeServer(Action<Pending> onNew, Action<Pending> onGone, Action<string> log)
        {
            _onNew = onNew; _onGone = onGone; _log = log;
        }

        public void Start()
        {
            var t = new Thread(AcceptLoop) { IsBackground = true, Name = "pipe" };
            t.Start();
        }

        void AcceptLoop()
        {
            var sec = new PipeSecurity();
            sec.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,
                                                 PipeAccessRights.FullControl, AccessControlType.Allow));
            while (true)
            {
                NamedPipeServerStream pipe = null;
                try
                {
                    // Asynchronous : la lecture qui guette la deconnexion et l'ecriture de
                    // la reponse se font en meme temps sur le meme handle.
                    pipe = new NamedPipeServerStream(Name, PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous, 0, 0, sec);
                    pipe.WaitForConnection();
                    var conn = pipe;
                    new Thread(() => Handle(conn)) { IsBackground = true, Name = "demande" }.Start();
                }
                catch (Exception ex)
                {
                    _log("pipe : " + ex.Message);
                    if (pipe != null) pipe.Dispose();
                    Thread.Sleep(1000);
                }
            }
        }

        void Handle(NamedPipeServerStream pipe)
        {
            Pending p = null;
            try
            {
                var header = ReadLine(pipe, 64);
                var parts = (header ?? "").Split(' ');
                int len;
                if (parts.Length != 2 || !int.TryParse(parts[1], out len) || len < 0 || len > 16 * 1024 * 1024)
                { pipe.Dispose(); return; }

                var buf = new byte[len];
                int got = 0;
                while (got < len)
                {
                    int n = pipe.Read(buf, got, len - got);
                    if (n <= 0) { pipe.Dispose(); return; }
                    got += n;
                }

                var payload = Json.Obj(Json.Parse(Encoding.UTF8.GetString(buf)));
                if (payload == null || (parts[0] != "permission" && parts[0] != "ask")) { pipe.Dispose(); return; }

                p = new Pending
                {
                    Kind = parts[0],
                    SessionId = Json.Str(payload, "session_id") ?? "",
                    Cwd = Json.Str(payload, "cwd") ?? "",
                    Payload = payload
                };
                var lk = new object();
                p.Send = line =>
                {
                    lock (lk)
                    {
                        var bytes = Encoding.UTF8.GetBytes(line + "\n");
                        pipe.Write(bytes, 0, bytes.Length);
                        pipe.Flush();
                        try { pipe.WaitForPipeDrain(); } catch { }
                        pipe.Dispose();
                    }
                };

                if (!Usable(p)) { p.Reply(""); return; }
                _onNew(p);

                // Le hook n'ecrit plus rien : Read ne revient que s'il ferme le pipe.
                var one = new byte[1];
                while (pipe.Read(one, 0, 1) > 0) { }
                if (!p.Done) { p.MarkGone(); _onGone(p); }
                pipe.Dispose();
            }
            catch (ObjectDisposedException) { /* reponse envoyee */ }
            catch (Exception ex)
            {
                if (p != null && !p.Done) { p.MarkGone(); _onGone(p); }
                else if (p == null) _log("demande illisible : " + ex.Message);
                try { pipe.Dispose(); } catch { }
            }
        }

        // L'encoche ne doit jamais retenir une demande qu'elle ne saura pas afficher.
        static bool Usable(Pending p)
        {
            var tool = p.ToolName;
            if (p.Kind == "permission") return tool != "" && tool != "ExitPlanMode" && tool != "AskUserQuestion";
            if (tool != "AskUserQuestion") return false;
            var qs = Json.Arr(Json.Get(p.ToolInput, "questions"));
            if (qs == null || qs.Length == 0) return false;
            foreach (var q in qs)
            {
                var qd = Json.Obj(q);
                if (qd == null || string.IsNullOrEmpty(Json.Str(qd, "question"))) return false;
                var opts = Json.Arr(Json.Get(qd, "options"));
                if (opts == null || opts.Length == 0) return false;
            }
            return true;
        }

        static string ReadLine(Stream s, int max)
        {
            var sb = new StringBuilder();
            while (sb.Length < max)
            {
                int b = s.ReadByte();
                if (b < 0) return null;
                if (b == '\n') return sb.ToString();
                sb.Append((char)b);
            }
            return null;
        }
    }
}
