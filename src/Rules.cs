// Regles suggerees : apres 5 autorisations de la meme commande dans un projet, Bip
// propose d'en faire une regle Claude Code. Rien n'est ecrit sans un clic ; chaque
// regle ajoutee ou retiree est inscrite au journal d'audit.
//
// rules\created.jsonl : les regles creees par Bip ; rules\ignored.txt : "ne plus proposer".

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ClaudeNotch
{
    class Suggestion
    {
        public string Tool, Key, Dir, Rule;
        public int Count;
    }

    class CreatedRule
    {
        public DateTime T;
        public string Scope, File, Rule, Dir;   // Scope : "projet" ou "partout"
    }

    static class Rules
    {
        const int THRESHOLD = 5;

        static string Folder { get { return Path.Combine(Program.Root, "rules"); } }
        static string CreatedFile { get { return Path.Combine(Folder, "created.jsonl"); } }
        static string IgnoredFile { get { return Path.Combine(Folder, "ignored.txt"); } }

        static bool SameDir(string a, string b)
        {
            return string.Equals((a ?? "").TrimEnd('\\', '/'), (b ?? "").TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        // Suggestion pour une demande de permission en cours, ou null.
        public static Suggestion For(string tool, string command, string dir)
        {
            if ((tool != "Bash" && tool != "PowerShell") || string.IsNullOrEmpty(dir)) return null;
            var key = Audit.RuleKey(command);
            if (key == null) return null;
            var rule = tool + "(" + key + ":*)";
            if (Ignored(dir, rule)) return null;
            if (Created().Any(c => c.Rule == rule && (c.Scope == "partout" || SameDir(c.Dir, dir)))) return null;

            int n = Audit.Rows(Program.Root, 30).Count(r =>
                r.Tool == tool && r.Allowed && r.ByUser && SameDir(r.Proj, dir) && Audit.RuleKey(r.Obj) == key);
            return n >= THRESHOLD ? new Suggestion { Tool = tool, Key = key, Dir = dir, Rule = rule, Count = n } : null;
        }

        // Ecrit la regle : reglages locaux du projet (non versionnes) ou reglages utilisateur.
        public static void Apply(Suggestion s, bool everywhere)
        {
            string file = everywhere
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json")
                : Path.Combine(s.Dir, ".claude", "settings.local.json");
            EditAllow(file, s.Rule, true);
            Directory.CreateDirectory(Folder);
            var rec = new Dictionary<string, object>
            {
                { "t", DateTime.Now.ToString("s", CultureInfo.InvariantCulture) },
                { "scope", everywhere ? "partout" : "projet" }, { "file", file }, { "rule", s.Rule }, { "dir", s.Dir }
            };
            File.AppendAllText(CreatedFile, Json.Write(rec) + "\n", new UTF8Encoding(false));
            Audit.Write(Program.Root, "regle", new Dictionary<string, object>
            {
                { "sess", "bip" }, { "dir", s.Dir }, { "tool", "Règle" },
                { "obj", s.Rule + (everywhere ? " · partout" : " · ce projet") }, { "dec", "ajoutee" }
            });
        }

        public static void Remove(CreatedRule c)
        {
            EditAllow(c.File, c.Rule, false);
            var keep = Created().Where(x => !(x.Rule == c.Rule && x.File == c.File)).Select(x => Json.Write(new Dictionary<string, object>
            {
                { "t", x.T.ToString("s", CultureInfo.InvariantCulture) }, { "scope", x.Scope }, { "file", x.File }, { "rule", x.Rule }, { "dir", x.Dir }
            })).ToList();
            File.WriteAllText(CreatedFile, string.Join("\n", keep) + (keep.Any() ? "\n" : ""), new UTF8Encoding(false));
            Audit.Write(Program.Root, "regle", new Dictionary<string, object>
            {
                { "sess", "bip" }, { "dir", c.Dir }, { "tool", "Règle" },
                { "obj", c.Rule + (c.Scope == "partout" ? " · partout" : " · ce projet") }, { "dec", "retiree" }
            });
        }

        public static void Ignore(Suggestion s)
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(IgnoredFile, s.Dir + "|" + s.Rule + Environment.NewLine, new UTF8Encoding(false));
        }

        static bool Ignored(string dir, string rule)
        {
            try
            {
                if (!File.Exists(IgnoredFile)) return false;
                return File.ReadAllLines(IgnoredFile).Any(l =>
                {
                    var p = l.Split(new[] { '|' }, 2);
                    return p.Length == 2 && p[1] == rule && SameDir(p[0], dir);
                });
            }
            catch { return false; }
        }

        public static List<CreatedRule> Created()
        {
            var list = new List<CreatedRule>();
            try
            {
                if (!File.Exists(CreatedFile)) return list;
                foreach (var l in File.ReadAllLines(CreatedFile, Encoding.UTF8))
                {
                    var d = Json.Obj(Json.Parse(l));
                    if (d == null) continue;
                    DateTime t;
                    DateTime.TryParse(Json.Str(d, "t"), CultureInfo.InvariantCulture, DateTimeStyles.None, out t);
                    list.Add(new CreatedRule { T = t, Scope = Json.Str(d, "scope"), File = Json.Str(d, "file"), Rule = Json.Str(d, "rule"), Dir = Json.Str(d, "dir") });
                }
            }
            catch { }
            return list;
        }

        // Ajoute ou retire une regle de permissions.allow, avec une sauvegarde datee du fichier.
        static void EditAllow(string file, string rule, bool add)
        {
            // Un dossier .claude ou un fichier qui serait un lien (jonction, lien symbolique)
            // pourrait rediriger l'ecriture ailleurs : on refuse.
            var dirInfo = new DirectoryInfo(Path.GetDirectoryName(file));
            if ((dirInfo.Exists && (dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                || (File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0))
                throw new Exception("le dossier .claude de ce projet est un lien, la règle n'est pas écrite.");
            Dictionary<string, object> settings = new Dictionary<string, object>();
            if (File.Exists(file))
            {
                var o = Json.Obj(Json.Parse(File.ReadAllText(file, Encoding.UTF8)));
                if (o == null) throw new Exception(Path.GetFileName(file) + " illisible : rien n'a été modifié.");
                settings = o;
                // Sauvegarde chez Bip, pas dans le projet (elle finirait versionnee).
                var backups = Path.Combine(Folder, "backups");
                Directory.CreateDirectory(backups);
                var tag = Audit.Sha(Path.GetFullPath(file).ToLowerInvariant()).Substring(0, 8);
                File.Copy(file, Path.Combine(backups, Path.GetFileName(file) + "." + tag + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss")), true);
            }
            else if (!add) return;

            var perms = Json.Obj(Json.Get(settings, "permissions"));
            if (perms == null) { perms = new Dictionary<string, object>(); settings["permissions"] = perms; }
            var allow = (Json.Arr(Json.Get(perms, "allow")) ?? new object[0]).Select(x => Convert.ToString(x)).ToList();
            if (add) { if (!allow.Contains(rule)) allow.Add(rule); }
            else allow.RemoveAll(x => x == rule);
            perms["allow"] = allow.Cast<object>().ToArray();

            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var tmp = file + ".tmp-bip";
            File.WriteAllText(tmp, Json.Write(settings, true) + "\n", new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(tmp, file, null); else File.Move(tmp, file);
        }
    }
}
