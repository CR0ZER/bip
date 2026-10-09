// NotchHook.exe state <etat>    hooks async : ecrit l'etat de la session dans state\
// NotchHook.exe permission      hook PermissionRequest synchrone : relaie a l'encoche
// NotchHook.exe ask             hook PreToolUse (AskUserQuestion) synchrone : idem
//
// Compile en /target:winexe : aucune console, donc aucun clignotement de fenetre.
// En mode relais, le hook ne decide rien : il transmet le JSON recu a ClaudeNotch.exe
// par un pipe nomme et imprime tel quel ce que l'encoche renvoie. Reponse vide,
// encoche absente ou erreur : il n'imprime rien, et Claude pose la question lui-meme.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

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

    // Champ texte de premier niveau, sans dependance JSON.
    static string JsonField(string json, string name)
    {
        var m = Regex.Match(json, "\"" + name + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        if (!m.Success) return null;
        try { return Regex.Unescape(m.Groups[1].Value); } catch { return null; }
    }

    static int Main(string[] args)
    {
        try
        {
            // L'instant de lancement fixe l'ordre des evenements (hooks async).
            DateTime started = Process.GetCurrentProcess().StartTime;
            string mode = args.Length > 0 ? args[0] : "state";
            string input = ReadStdin();

            string id = JsonField(input, "session_id");
            if (string.IsNullOrEmpty(id)) id = Environment.GetEnvironmentVariable("CLAUDE_CODE_SESSION_ID");
            string dir = JsonField(input, "cwd");
            if (string.IsNullOrEmpty(dir)) dir = Environment.GetEnvironmentVariable("CLAUDE_PROJECT_DIR") ?? "";

            if (string.IsNullOrEmpty(id))
            {
                id = dir;
                foreach (char c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '_');
                id = id.Replace(' ', '_');
            }
            if (string.IsNullOrEmpty(id)) id = "session";

            if (mode == "state")
            {
                WriteState(id, args.Length > 1 ? args[1] : "ready", dir, started);
                return 0;
            }

            if (mode == "permission" || mode == "ask")
            {
                // Hors perimetre de l'encoche : validation de plan, et les questions qui
                // repassent par PermissionRequest apres "Repondre dans Claude".
                string tool = JsonField(input, "tool_name") ?? "";
                if (mode == "permission" && (tool == "ExitPlanMode" || tool == "AskUserQuestion")) return 0;

                string reply = Relay(mode, input);
                if (!string.IsNullOrEmpty(reply))
                {
                    var bytes = new UTF8Encoding(false).GetBytes(reply);
                    using (var o = Console.OpenStandardOutput()) o.Write(bytes, 0, bytes.Length);
                    // L'utilisateur a repondu : la session repart (l'outil autorise tourne,
                    // ou Claude enchaine apres un refus).
                    WriteState(id, "busy", dir, DateTime.Now);
                }
                else
                {
                    // Claude va poser la question lui-meme : la session attend l'utilisateur.
                    // Horodatage "maintenant" : cet evenement suit le busy du PreToolUse.
                    WriteState(id, "waiting", dir, DateTime.Now);
                }
            }
        }
        catch { /* un hook ne doit jamais faire echouer une session */ }

        return 0;
    }

    static void WriteState(string id, string state, string dir, DateTime when)
    {
        string stateDir = Path.Combine(Root, "state");
        Directory.CreateDirectory(stateDir);
        string file = Path.Combine(stateDir, id + ".state");

        if (state == "end")
        {
            if (File.Exists(file)) File.Delete(file);
            return;
        }

        // Un hook lance apres nous a deja ecrit : notre etat est perime.
        // ponytail: lecture/ecriture non atomiques, fenetre de quelques ms ; verrou de fichier si un jour ca se voit.
        if (File.Exists(file) && File.GetLastWriteTime(file) > when) return;

        // Format : etat|dossier|pid. La date de creation du fichier, conservee par
        // les reecritures, donne l'ordre de lancement des sessions dans l'encoche.
        // Quelques essais : l'encoche lit le fichier, un autre hook peut l'ecrire.
        string text = state + "|" + dir + "|" + ParentPid();
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
