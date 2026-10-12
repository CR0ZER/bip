// bip run <commande>   lance la commande dans ce terminal et la suit dans l'encoche
// bip --version
//
// Compile en console (/target:exe) : la commande partage le terminal, sa sortie, ses
// couleurs et sa saisie restent intactes. bip renvoie le code de retour de la commande.
// La commande est executee par cmd.exe (pour une commande PowerShell : bip run pwsh -c "...").
//
// Etat ecrit dans state\run-<pid>.state : "etat|dossier|pid|run|commande|code|debut".

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

static class BipCli
{
    static readonly string Root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

    static int Main(string[] args)
    {
        string first = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        if (first == "--version" || first == "version")
        {
            Console.WriteLine("Bip v" + ClaudeNotch.AppVersion.Text);
            return 0;
        }
        string command = first == "run" ? RawAfterRun() : "";
        if (command.Length == 0)
        {
            Console.WriteLine("Bip v" + ClaudeNotch.AppVersion.Text + " — suivre une commande longue dans l'encoche");
            Console.WriteLine();
            Console.WriteLine("  bip run <commande>     ex. : bip run npm run build");
            Console.WriteLine("  bip --version");
            return first == "" || first == "help" || first == "-h" || first == "--help" ? 0 : 2;
        }

        int pid;
        string start;
        using (var me = Process.GetCurrentProcess())
        {
            pid = me.Id;
            // Heure de lancement : ordre dans l'encoche, et preuve que le pid est bien le notre.
            start = me.StartTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        }
        string file = Path.Combine(Root, "state", "run-" + pid + ".state");
        string dir = Environment.CurrentDirectory;
        string label = command.Replace("|", "¦").Replace("\r", " ").Replace("\n", " ");
        if (label.Length > 80) label = label.Substring(0, 77) + "…";

        bool interrupted = false;
        // Ctrl+C : la commande le recoit aussi et s'arrete ; bip reste en vie pour noter la fin.
        // Ctrl+Pause ne peut pas etre annule : bip s'arrete, l'encoche le verra disparaitre.
        Console.CancelKeyPress += (s, e) =>
        {
            if (e.SpecialKey == ConsoleSpecialKey.ControlBreak) return;
            e.Cancel = true;
            interrupted = true;
        };

        Write(file, "busy|" + dir + "|" + pid + "|run|" + label + "||" + start);
        int code;
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", "/d /s /c \"" + command + "\"") { UseShellExecute = false };
            using (var p = Process.Start(psi))
            {
                p.WaitForExit();
                code = p.ExitCode;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("bip : impossible de lancer la commande (" + ex.Message + ")");
            code = 1;
        }

        // Une commande qui gere Ctrl+C et finit proprement (code 0) n'est pas un echec.
        bool stopped = interrupted && code != 0;
        string outcome = stopped ? "interrompu" : code.ToString();
        Write(file, (code == 0 ? "ready" : "error") + "|" + dir + "|" + pid + "|run|" + label + "|" + outcome + "|" + start);
        return code;
    }

    // Tout ce qui suit "run" sur la ligne de commande, tel que tape (guillemets compris).
    static string RawAfterRun()
    {
        string cl = Environment.CommandLine;
        int i = 0;
        if (cl.StartsWith("\"")) { i = cl.IndexOf('"', 1) + 1; if (i <= 0) i = cl.Length; }
        else while (i < cl.Length && !char.IsWhiteSpace(cl[i])) i++;
        while (i < cl.Length && char.IsWhiteSpace(cl[i])) i++;
        if (string.Compare(cl, i, "run", 0, 3, StringComparison.OrdinalIgnoreCase) == 0) i += 3;
        while (i < cl.Length && char.IsWhiteSpace(cl[i])) i++;
        return i < cl.Length ? cl.Substring(i).Trim() : "";
    }

    static void Write(string file, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            for (int i = 0; ; i++)
            {
                try { File.WriteAllText(file, text, new UTF8Encoding(false)); return; }
                catch (IOException) { if (i >= 5) return; Thread.Sleep(50); }
            }
        }
        catch { }
    }
}
