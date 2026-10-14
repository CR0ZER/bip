// NotchHook.exe state <etat>    hooks async : ecrit l'etat de la session dans state\
// NotchHook.exe permission      hook PermissionRequest synchrone : relaie a l'encoche
// NotchHook.exe ask             hook PreToolUse (AskUserQuestion) synchrone : idem
// NotchHook.exe audit           hook PermissionDenied async : journal d'audit seulement
// NotchHook.exe agent           hooks SubagentStart / SubagentStop async : suivi des sous-agents
//
// Chaque utilisation d'outil est aussi inscrite au journal d'audit (Audit.cs), par le
// hook lui-meme : l'audit fonctionne meme encoche fermee.
//
// Compile en /target:winexe : aucune console, donc aucun clignotement de fenetre.
// En mode relais, le hook ne decide rien : il transmet le JSON recu a ClaudeNotch.exe
// par un pipe nomme et imprime tel quel ce que l'encoche renvoie. Reponse vide,
// encoche absente ou erreur : il n'imprime rien, et Claude pose la question lui-meme.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using ClaudeNotch;

static class NotchHook
{
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    static extern int NtQueryInformationProcess(IntPtr handle, int infoClass,
        ref PROCESS_BASIC_INFORMATION info, int size, out int returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint pid);

    static readonly string Root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

    // Le processus qui lance le hook est le claude.exe de la session : son PID est
    // la preuve de vie que l'encoche verifie.
    static int ParentPid()
    {
        try
        {
            var pbi = new PROCESS_BASIC_INFORMATION();
            int len;
            if (NtQueryInformationProcess(Process.GetCurrentProcess().Handle, 0,
                                          ref pbi, Marshal.SizeOf(pbi), out len) != 0) return 0;
            return (int)pbi.InheritedFromUniqueProcessId;
        }
        catch { return 0; }
    }

    // Lecture bornee : Claude Code n'impose pas de delai aux hooks async.
    static string ReadStdin()
    {
        try
        {
            if (!Console.IsInputRedirected) return "";
            string text = "";
            var t = new Thread(() =>
            {
                try
                {
                    using (var r = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)))
                        text = r.ReadToEnd();
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
            return t.Join(2000) ? text : "";
        }
        catch { return ""; }
    }

    static int Main(string[] args)
    {
        try
        {
            // L'instant de lancement fixe l'ordre des evenements (hooks async).
            DateTime started = Process.GetCurrentProcess().StartTime;
            string mode = args.Length > 0 ? args[0] : "state";
            string input = ReadStdin();
            // JSON illisible : d reste null, les variables d'environnement prennent le relais.
            Dictionary<string, object> d = null;
            try { d = Json.Obj(Json.Parse(input)); } catch { }

            string id = Json.Str(d, "session_id");
            if (string.IsNullOrEmpty(id)) id = Environment.GetEnvironmentVariable("CLAUDE_CODE_SESSION_ID");
            string dir = Json.Str(d, "cwd");
            if (string.IsNullOrEmpty(dir)) dir = Environment.GetEnvironmentVariable("CLAUDE_PROJECT_DIR") ?? "";

            if (string.IsNullOrEmpty(id))
            {
                id = dir;
                foreach (char c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '_');
                id = id.Replace(' ', '_');
            }
            if (string.IsNullOrEmpty(id)) id = "session";

            string evt = Json.Str(d, "hook_event_name") ?? "";
            // Racine du projet (les regles "ce projet" y sont ecrites) ; dir peut etre un sous-dossier.
            string proj = Environment.GetEnvironmentVariable("CLAUDE_PROJECT_DIR");
            if (string.IsNullOrEmpty(proj)) proj = dir;

            string transcript = Json.Str(d, "transcript_path") ?? "";
            if (mode == "state")
            {
                // Etat, sous-agents et audit independants : l'echec de l'un ne prive pas les autres.
                try { WriteState(id, args.Length > 1 ? args[1] : "ready", dir, started, transcript); } catch { }
                try { TrackAgentTool(evt, d, id); } catch { }
                if (evt == "PreToolUse" || evt == "PostToolUse" || evt == "PostToolUseFailure") AuditTool(evt, d, id, dir, proj);
                return 0;
            }
            if (mode == "agent")
            {
                // SubagentStart / SubagentStop : debut et vraie fin d'un sous-agent (aussi en arriere-plan).
                string agent = Json.Str(d, "agent_id");
                if (!string.IsNullOrEmpty(agent)) AppendAgent(id, evt == "SubagentStop" ? "fin" : "debut", agent);
                return 0;
            }
            if (mode == "audit")
            {
                AuditTool(evt, d, id, dir, proj);
                return 0;
            }

            if (mode == "permission" || mode == "ask")
            {
                // Hors perimetre de l'encoche : validation de plan, et les questions qui
                // repassent par PermissionRequest apres "Repondre dans Claude".
                string tool = Json.Str(d, "tool_name") ?? "";
                if (mode == "permission" && (tool == "ExitPlanMode" || tool == "AskUserQuestion")) return 0;

                if (mode == "permission") AuditTool("PermissionRequest", d, id, dir, proj);
                string reply = Relay(mode, WithProject(input, proj));
                if (!string.IsNullOrEmpty(reply))
                {
                    // La reponse d'abord : Claude attend, le journal peut suivre.
                    var bytes = new UTF8Encoding(false).GetBytes(reply);
                    using (var o = Console.OpenStandardOutput()) o.Write(bytes, 0, bytes.Length);
                }
                if (mode == "permission") AuditDecision(reply, d, id);
                // Repondu : la session repart. Sinon Claude pose la question lui-meme et la
                // session attend l'utilisateur. Horodatage "maintenant" : suit le busy du PreToolUse.
                WriteState(id, string.IsNullOrEmpty(reply) ? "waiting" : "busy", dir, DateTime.Now, transcript);
            }
        }
        catch { /* un hook ne doit jamais faire echouer une session */ }

        return 0;
    }

    // ------------------------------------------------------------ journal d'audit

    // Ajoute la racine du projet au JSON relaye a l'encoche (champ "bip_proj").
    static string WithProject(string input, string proj)
    {
        var t = input.TrimStart();
        if (string.IsNullOrEmpty(proj) || !t.StartsWith("{")) return input;
        return "{\"bip_proj\":" + Json.Write(proj) + "," + t.Substring(1);
    }

    static void AuditTool(string evt, Dictionary<string, object> d, string id, string dir, string proj)
    {
        try
        {
            string ev;
            switch (evt)
            {
                case "PreToolUse":         ev = "outil"; break;
                case "PermissionRequest":  ev = "demande"; break;
                case "PostToolUse":        ev = "fait"; break;
                case "PostToolUseFailure": ev = "echec"; break;
                case "PermissionDenied":   ev = "refus-auto"; break;
                default: return;
            }
            if (d == null) return;
            string tool = Json.Str(d, "tool_name") ?? "";
            var fields = new Dictionary<string, object>
            {
                { "sess", id },
                { "tool", tool },
                { "obj", Audit.Summarize(tool, Json.Obj(Json.Get(d, "tool_input"))) },
                { "tuid", Json.Str(d, "tool_use_id") }
            };
            // Action faite par un sous-agent : son identifiant (rapport d'audit par agent).
            string agent = Json.Str(d, "agent_id");
            if (!string.IsNullOrEmpty(agent)) fields["agent"] = agent;
            // Lancement d'un sous-agent : son identifiant, pour regrouper ses actions sous lui.
            if (ev == "fait" && (tool == "Agent" || tool == "Task"))
            {
                string spawned = Json.Str(Json.Obj(Json.Get(d, "tool_response")), "agentId");
                if (!string.IsNullOrEmpty(spawned)) fields["spawn"] = spawned;
            }
            if (ev == "outil")
            {
                fields["dir"] = dir;
                if (proj != dir) fields["proj"] = proj;
                fields["mode"] = Json.Str(d, "permission_mode");
            }
            Audit.Write(Root, ev, fields);
        }
        catch { }
    }

    // Reponse de l'encoche : autorise / toujours / refuse ; vide = la fenetre Claude decide.
    static void AuditDecision(string reply, Dictionary<string, object> d, string id)
    {
        try
        {
            if (d == null) return;
            string tool = Json.Str(d, "tool_name") ?? "";
            string dec = string.IsNullOrEmpty(reply) ? "claude"
                       : reply.Contains("\"behavior\":\"deny\"") ? "deny"
                       : reply.Contains("updatedPermissions") ? "always" : "allow";
            Audit.Write(Root, "decision", new Dictionary<string, object>
            {
                { "sess", id }, { "tool", tool },
                { "obj", Audit.Summarize(tool, Json.Obj(Json.Get(d, "tool_input"))) },
                { "tuid", Json.Str(d, "tool_use_id") }, { "dec", dec }
            });
        }
        catch { }
    }

    // ------------------------------------------------------------ sous-agents

    // state\<session>.agents : une ligne par evenement, "type|ticks|cle|valeur", lue par l'encoche.
    //   lance|t|tool_use_id|description   l'outil Agent est appele
    //   id|t|tool_use_id|agent_id          reponse de l'outil : relie l'appel au sous-agent
    //   debut|t|agent_id                   SubagentStart
    //   fin|t|agent_id                     SubagentStop, ou fin de l'outil au premier plan
    //   echec|t|tool_use_id                l'outil Agent a echoue
    static void TrackAgentTool(string evt, Dictionary<string, object> d, string id)
    {
        string tool = Json.Str(d, "tool_name");
        if (tool != "Agent" && tool != "Task") return;
        string tuid = Json.Str(d, "tool_use_id") ?? "";
        if (evt == "PreToolUse")
        {
            string desc = Json.Str(Json.Obj(Json.Get(d, "tool_input")), "description") ?? "Sous-agent";
            AppendAgent(id, "lance", tuid + "|" + desc.Replace("|", "¦").Replace("\r", " ").Replace("\n", " "));
        }
        else if (evt == "PostToolUse")
        {
            // Arriere-plan : reponse immediate (async_launched) ; premier plan : reponse a la fin (completed).
            var resp = Json.Obj(Json.Get(d, "tool_response"));
            string agent = Json.Str(resp, "agentId");
            if (string.IsNullOrEmpty(agent)) return;
            AppendAgent(id, "id", tuid + "|" + agent);
            if (Json.Str(resp, "status") == "completed") AppendAgent(id, "fin", agent);
        }
        else if (evt == "PostToolUseFailure") AppendAgent(id, "echec", tuid);
    }

    static void AppendAgent(string id, string type, string rest)
    {
        Directory.CreateDirectory(Path.Combine(Root, "state"));
        string file = Path.Combine(Root, "state", id + ".agents");
        string line = type + "|" + DateTime.Now.Ticks + "|" + rest + "\n";
        for (int i = 0; ; i++)
        {
            try { File.AppendAllText(file, line, new UTF8Encoding(false)); return; }
            catch (IOException) { if (i >= 5) return; Thread.Sleep(30); }
        }
    }

    // ------------------------------------------------------------ etat de la session

    static void WriteState(string id, string state, string dir, DateTime when, string transcript)
    {
        string stateDir = Path.Combine(Root, "state");
        Directory.CreateDirectory(stateDir);
        string file = Path.Combine(stateDir, id + ".state");

        if (state == "end")
        {
            if (File.Exists(file)) File.Delete(file);
            try { File.Delete(Path.Combine(stateDir, id + ".agents")); } catch { }
            return;
        }

        // Un hook lance apres nous a deja ecrit : notre etat est perime.
        // ponytail: lecture/ecriture non atomiques, fenetre de quelques ms ; verrou de fichier si un jour ca se voit.
        if (File.Exists(file) && File.GetLastWriteTime(file) > when) return;

        // Format : etat|dossier|pid|claude|transcript (le transcript donne le titre de la session).
        // La date de creation du fichier, conservee par les reecritures, donne l'ordre de
        // lancement des sessions dans l'encoche. Quelques essais : l'encoche lit le fichier, un
        // autre hook peut l'ecrire.
        string text = state + "|" + dir + "|" + ParentPid() + "|claude|" + transcript;
        for (int i = 0; ; i++)
        {
            try
            {
                File.WriteAllText(file, text, new UTF8Encoding(false));
                File.SetLastWriteTime(file, when);
                return;
            }
            catch (IOException) { if (i >= 3) throw; Thread.Sleep(50); }
        }
    }

    // Envoie "<mode> <taille>\n<json>" et attend une ligne de reponse.
    // Aucune limite de temps ici : c'est le timeout du hook (settings.json) qui borne.
    static string Relay(string mode, string input)
    {
        string name = "ClaudeNotch-" + WindowsIdentity.GetCurrent().User.Value;
        using (var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.None, TokenImpersonationLevel.Identification))
        {
            try { pipe.Connect(300); } catch { return null; }   // encoche fermee
            if (!IsOurServer(pipe)) return null;

            var payload = Encoding.UTF8.GetBytes(input);
            var header = Encoding.ASCII.GetBytes(mode + " " + payload.Length + "\n");
            pipe.Write(header, 0, header.Length);
            pipe.Write(payload, 0, payload.Length);
            pipe.Flush();

            var buf = new MemoryStream();
            int b;
            while ((b = pipe.ReadByte()) >= 0 && b != '\n') buf.WriteByte((byte)b);
            return Encoding.UTF8.GetString(buf.ToArray()).Trim();
        }
    }

    // Un autre compte pourrait creer ce pipe avant l'encoche et repondre "allow" a
    // sa place. On n'accepte que le ClaudeNotch.exe installe a cote de ce hook.
    static bool IsOurServer(NamedPipeClientStream pipe)
    {
        try
        {
            uint pid;
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out pid)) return false;
            using (var p = Process.GetProcessById((int)pid))
                return string.Equals(Path.GetFullPath(p.MainModule.FileName),
                                     Path.GetFullPath(Path.Combine(Root, "ClaudeNotch.exe")),
                                     StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
