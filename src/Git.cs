// Git dans l'encoche : branche, fichiers non commites, commits a pousser. Lecture seule.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

class GitInfo
{
    public string Branch;                                  // "(détachée)" hors branche
    public bool Upstream;                                  // branche distante suivie
    public int Ahead;
    public List<string> Files = new List<string>();        // "M src/a.cs", "? notes.txt"
    public List<string> Unpushed;                          // titres, lus a l'ouverture du detail
}

static class Git
{
    // Sans verrou (Claude peut commiter en meme temps), et sans rien qui lance une commande
    // configuree par le depot : fsmonitor, hooks, signatures, sous-modules.
    // ponytail: un filtre clean (filter.<x>.clean dans .git/config) peut encore tourner pendant
    // un status, comme pour le "git status" que Claude Code lance lui-meme.
    const string Safe = "--no-pager --no-optional-locks -c core.fsmonitor=false -c core.hooksPath=NUL -c core.quotePath=false"
                      + " -c log.showSignature=false -c status.submoduleSummary=false ";

    // null : hors depot, Git absent, ou plus de 3 s.
    public static GitInfo Status(string dir)
    {
        var o = Run(dir, "status --porcelain=v2 --branch --ignore-submodules=all");
        return o == null ? null : Parse(o);
    }

    public static List<string> Unpushed(string dir)
    {
        var o = Run(dir, "log @{u}.. --format=%s -n 5");
        if (o == null) return null;
        var l = new List<string>();
        foreach (var s in o.Split('\n')) if (s.Trim().Length > 0) l.Add(s.Trim());
        return l;
    }

    public static GitInfo Parse(string porcelain)
    {
        var g = new GitInfo();
        foreach (var raw in porcelain.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("# branch.head ")) { var b = line.Substring(14); g.Branch = b == "(detached)" ? "(détachée)" : b; }
            else if (line.StartsWith("# branch.upstream ")) g.Upstream = true;
            else if (line.StartsWith("# branch.ab ")) { int a; int.TryParse(line.Substring(12).Split(' ')[0].TrimStart('+'), out a); g.Ahead = a; }
            else if (line.StartsWith("? ")) g.Files.Add("? " + line.Substring(2));
            else if (line.StartsWith("1 ") || line.StartsWith("2 ") || line.StartsWith("u "))
            {
                int n = line[0] == '1' ? 9 : line[0] == '2' ? 10 : 11;
                var f = line.Split(new[] { ' ' }, n);
                if (f.Length < n) continue;
                string xy = f[1], path = f[n - 1].Split('\t')[0];
                string st = line[0] == 'u' ? "U" : xy.Contains("D") ? "D" : xy.Contains("A") ? "A" : line[0] == '2' ? "R" : "M";
                g.Files.Add(st + " " + path);
            }
        }
        return g.Branch == null ? null : g;
    }

    static string Run(string dir, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", Safe + args)
            {
                WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = new UTF8Encoding(false)
            };
            psi.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "0";
            using (var p = Process.Start(psi))
            {
                var output = p.StandardOutput.ReadToEndAsync();
                p.StandardError.ReadToEndAsync();   // vide, pour ne jamais bloquer Git
                if (!p.WaitForExit(3000)) { try { p.Kill(); } catch { } return null; }
                return p.ExitCode == 0 ? output.Result : null;
            }
        }
        catch { return null; }   // Git absent, dossier disparu
    }
}
