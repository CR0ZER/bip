// ClaudeNotch.exe                 lance l'encoche (une seule instance)
// ClaudeNotch.exe --install       branche les hooks dans ~/.claude/settings.json + demarrage Windows
// ClaudeNotch.exe --uninstall     les retire, et ferme l'encoche si elle tourne
// ClaudeNotch.exe --dump-plan     affiche ce que l'encoche montrerait (diagnostic)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ClaudeNotch
{
    static class Program
    {
        public static readonly string Root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        public static string StateDir { get { return Path.Combine(Root, "state"); } }
        public const string QUIT_EVENT = "ClaudeNotch.Quit";

        [DllImport("kernel32.dll")] static extern bool AttachConsole(int processId);

        public static void Log(string msg)
        {
            try
            {
                var f = Path.Combine(Root, "notch.log");
                if (File.Exists(f) && new FileInfo(f).Length > 512 * 1024) File.Delete(f);
                File.AppendAllText(f, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
            }
            catch { }
        }

        [STAThread]
        static int Main(string[] args)
        {
            string cmd = args.Length > 0 ? args[0] : "";

            if (cmd == "--install" || cmd == "--uninstall" || cmd == "--dump-plan" || cmd == "--version")
            {
                try { AttachConsole(-1); } catch { }
                try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }
                Console.WriteLine();
                try
                {
                    if (cmd == "--version") Console.WriteLine("Bip v" + AppVersion.Text);
                    else if (cmd == "--install") Installer.Install();
                    else if (cmd == "--uninstall") Installer.Uninstall();
                    else DumpPlan();
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Erreur : " + ex.Message);
                    return 1;
                }
            }

            bool isNew;
            using (var mtx = new Mutex(true, "ClaudeNotch.SingleInstance", out isNew))
            {
                if (!isNew) return 0;

                // Rendu logiciel : pour une si petite fenetre, le peripherique Direct3D (et le
                // pilote graphique charge avec lui) coutait ~200 Mo et ~76 threads pour rien.
                System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += (s, e) =>
                {
                    Log("exception : " + e.Exception);
                    e.Handled = true;   // une encoche qui plante ne doit rien emporter
                };

                // Signal de fermeture envoye par --uninstall.
                var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QUIT_EVENT);
                new Thread(() => { quit.WaitOne(); app.Dispatcher.BeginInvoke((Action)(() => app.Shutdown())); })
                    { IsBackground = true }.Start();

                var win = new NotchWindow();
                win.Show();
                app.Run();
            }
            return 0;
        }

        static void DumpPlan()
        {
            var all = Sessions.Load(StateDir) ?? new List<Session>();
            Console.WriteLine("dossier : " + StateDir);
            Console.WriteLine("{0,-12} {1,-11} {2,7}  {3}", "SESSION", "ETAT", "AGE", "ORDRE / NOM");
            int rank = 0;
            foreach (var s in all)
            {
                bool on = Sessions.IsActive(s.State);
                Console.WriteLine("{0,-12} {1,-11} {2,5:N0} m  {3}",
                    s.Id.Length > 12 ? s.Id.Substring(0, 12) : s.Id, States.Label(s.State),
                    s.Elapsed.TotalMinutes, on ? "#" + (++rank) + "  " + s.Name : "-   " + s.Name);
            }
            Console.WriteLine();
            Console.WriteLine(rank == 0 ? "=> encoche au repos" :
                rank <= 5 ? "=> " + rank + " nuage(s)" : "=> 5 nuages + " + (rank - 5));
        }
    }

    // ------------------------------------------------------------ installation

    static class Installer
    {
        const string HOOK_NAME = "NotchHook.exe";

        static string SettingsPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json"); }
        }

        static string StartupLink
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "ClaudeNotch.lnk"); }
        }

        // Marqueur : toute entree dont la commande pointe vers NotchHook.exe est a nous.
        static bool IsOurs(object hook)
        {
            var cmd = Json.Str(Json.Obj(hook), "command") ?? "";
            return cmd.EndsWith("\\" + HOOK_NAME, StringComparison.OrdinalIgnoreCase)
                || cmd.EndsWith("/" + HOOK_NAME, StringComparison.OrdinalIgnoreCase);
        }

        public static void Install()
        {
            string hook = Path.Combine(Program.Root, HOOK_NAME);
            if (!File.Exists(hook)) throw new Exception(HOOK_NAME + " introuvable : lance build.cmd d'abord.");

            var settings = Load();
            var hooks = Json.Obj(Json.Get(settings, "hooks"));
            if (hooks == null) { hooks = new Dictionary<string, object>(); settings["hooks"] = hooks; }
            int removed = Strip(hooks);

            Func<string[], bool, int, object> entry = (a, isAsync, timeout) =>
            {
                var h = new Dictionary<string, object>
                {
                    { "type", "command" },
                    { "command", hook },
                    { "args", a }
                };
                if (isAsync) h["async"] = true; else h["timeout"] = timeout;
                return h;
            };
            Action<string, string, object> add = (evt, matcher, h) =>
            {
                var groups = (Json.Arr(Json.Get(hooks, evt)) ?? new object[0]).ToList();
                var g = new Dictionary<string, object>();
                if (matcher != null) g["matcher"] = matcher;
                g["hooks"] = new object[] { h };
                groups.Add(g);
                hooks[evt] = groups.ToArray();
            };

            // Affichage : async, ne bloque jamais la session.
            add("SessionStart",       null, entry(new[] { "state", "idle" }, true, 0));
            add("UserPromptSubmit",   null, entry(new[] { "state", "busy" }, true, 0));
            add("PreToolUse",         null, entry(new[] { "state", "busy" }, true, 0));
            add("PostToolUse",        null, entry(new[] { "state", "busy" }, true, 0));
            add("PostToolUseFailure", null, entry(new[] { "state", "busy" }, true, 0));
            add("PermissionRequest",  null, entry(new[] { "state", "waiting" }, true, 0));
            add("Elicitation",        null, entry(new[] { "state", "waiting" }, true, 0));
            add("Notification", "idle_prompt|agent_needs_input", entry(new[] { "state", "waiting" }, true, 0));
            add("Stop",               null, entry(new[] { "state", "ready" }, true, 0));
            add("StopFailure",        null, entry(new[] { "state", "error" }, true, 0));
            // Fin de session : bloquant, pour que l'etat soit retire avant la sortie.
            add("SessionEnd",         null, entry(new[] { "state", "end" }, false, 5));

            // Reponses depuis l'encoche : synchrones. Le hook attend le clic ; ce delai
            // n'est qu'un filet, a son expiration Claude pose la question lui-meme.
            add("PermissionRequest",  null, entry(new[] { "permission" }, false, 3600));
            add("PreToolUse", "AskUserQuestion", entry(new[] { "ask" }, false, 3600));

            Save(settings);
            CreateStartupLink();

            Console.WriteLine("Hooks ClaudeNotch installés dans " + SettingsPath +
                              (removed > 0 ? " (" + removed + " anciennes entrées remplacées)." : "."));
            Console.WriteLine("Démarrage avec Windows : " + StartupLink);
        }

        public static void Uninstall()
        {
            var settings = Load();
            var hooks = Json.Obj(Json.Get(settings, "hooks"));
            int removed = hooks == null ? 0 : Strip(hooks);
            if (hooks != null && hooks.Count == 0) settings.Remove("hooks");
            if (removed > 0) Save(settings);

            try { if (File.Exists(StartupLink)) File.Delete(StartupLink); } catch { }

            EventWaitHandle ev;
            if (EventWaitHandle.TryOpenExisting(Program.QUIT_EVENT, out ev)) using (ev) ev.Set();

            Console.WriteLine(removed + " entrées ClaudeNotch retirées de " + SettingsPath + ".");
            Console.WriteLine("Démarrage avec Windows retiré. Le dossier ClaudeNotch peut être supprimé.");
        }

        // Retire nos entrees, puis les groupes et evenements devenus vides.
        static int Strip(Dictionary<string, object> hooks)
        {
            int removed = 0;
            foreach (var evt in hooks.Keys.ToList())
            {
                var groups = Json.Arr(hooks[evt]);
                if (groups == null) continue;
                var keep = new List<object>();
                foreach (var g in groups)
                {
                    var gd = Json.Obj(g);
                    var hs = gd == null ? null : Json.Arr(Json.Get(gd, "hooks"));
                    if (hs == null) { keep.Add(g); continue; }
                    var rest = hs.Where(h => !IsOurs(h)).ToArray();
                    removed += hs.Length - rest.Length;
                    if (rest.Length == hs.Length) keep.Add(g);
                    else if (rest.Length > 0) { gd["hooks"] = rest; keep.Add(gd); }
                }
                if (keep.Count == 0) hooks.Remove(evt); else hooks[evt] = keep.ToArray();
            }
            return removed;
        }

        static Dictionary<string, object> Load()
        {
            var path = SettingsPath;
            if (!File.Exists(path)) return new Dictionary<string, object>();
            var text = File.ReadAllText(path, Encoding.UTF8);
            var o = Json.Obj(Json.Parse(text));
            if (o == null) throw new Exception("settings.json illisible : rien n'a été modifié.");
            return o;
        }

        // Sauvegarde datee, puis ecriture via un fichier temporaire.
        static void Save(Dictionary<string, object> settings)
        {
            var path = SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path))
            {
                var bak = path + ".bak-claudenotch-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(path, bak, true);
                Console.WriteLine("Sauvegarde : " + bak);
            }
            var tmp = path + ".tmp-claudenotch";
            File.WriteAllText(tmp, Json.Write(settings, true) + "\n", new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
        }

        static void CreateStartupLink()
        {
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object lnk = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { StartupLink });
                var lt = lnk.GetType();
                lt.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { Path.Combine(Program.Root, "ClaudeNotch.exe") });
                lt.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { Program.Root });
                lt.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { "ClaudeNotch — suivi des sessions Claude Code" });
                lt.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch (Exception ex) { Console.WriteLine("Raccourci de démarrage non créé : " + ex.Message); }
        }
    }

    // ------------------------------------------------------------ appels systeme

    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }

        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FLASHWINFO pwfi);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int value);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;

        // Plein ecran, jeu ou presentation : l'encoche s'efface.
        public static bool FullScreenApp()
        {
            int s;
            if (SHQueryUserNotificationState(out s) != 0) return false;
            return s == 2 || s == 3 || s == 4;   // QUNS_BUSY, RUNNING_D3D_FULL_SCREEN, PRESENTATION_MODE
        }

        // L'app Claude desktop n'a qu'une fenetre pour toutes les sessions.
        public static IntPtr FindClaudeWindow()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("claude"))
                    if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle;
            }
            catch { }
            return IntPtr.Zero;
        }

        public static void FocusClaude()
        {
            var h = FindClaudeWindow();
            if (h == IntPtr.Zero) return;
            if (IsIconic(h)) ShowWindow(h, 9 /* SW_RESTORE */);
            SetForegroundWindow(h);
        }

        public static void FlashClaude()
        {
            var h = FindClaudeWindow();
            if (h == IntPtr.Zero) return;
            var fi = new FLASHWINFO { hwnd = h, dwFlags = 3 | 12 /* ALL | TIMERNOFG */, uCount = 3 };
            fi.cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO));
            FlashWindowEx(ref fi);
        }
    }
}
