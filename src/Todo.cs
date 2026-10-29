// Micro to-do list : todo.txt dans le dossier de Bip, une tache par ligne, modifiable a la main.
// Partagee par l'encoche (affichage, ajout, coche) et par "bip todo" (ajout depuis un terminal,
// y compris par Claude, toujours apres une demande d'autorisation).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ClaudeNotch
{
    static class Todo
    {
        public const int MaxLen = 120;

        static string FileOf(string root) { return Path.Combine(root, "todo.txt"); }

        public static List<string> Load(string root)
        {
            try
            {
                var f = FileOf(root);
                if (!File.Exists(f)) return new List<string>();
                return File.ReadAllLines(f, Encoding.UTF8).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            }
            catch { return new List<string>(); }
        }

        // Une seule ligne : sauts de ligne et tabulations remplaces, blancs resserres, 120 caracteres au plus.
        public static string Clean(string text)
        {
            var t = string.Join(" ", (text ?? "").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            return t.Length > MaxLen ? t.Substring(0, MaxLen - 1) + "…" : t;
        }

        // ponytail: relecture puis reecriture du fichier, sans verrou ; un ajout simultane d'un
        // autre processus peut se perdre (fenetre de quelques ms). Verrou de fichier si ca se voit.
        public static void Add(string root, string text)
        {
            var t = Clean(text);
            if (t.Length == 0) return;
            var all = Load(root);
            all.Add(t);
            File.WriteAllLines(FileOf(root), all, new UTF8Encoding(false));
        }

        // Retire la premiere tache identique (le fichier a pu changer entre-temps : on relit).
        public static void Remove(string root, string text)
        {
            var all = Load(root);
            if (!all.Remove(text)) return;
            File.WriteAllLines(FileOf(root), all, new UTF8Encoding(false));
        }

        // Change des que le fichier change (ajout par "bip todo", modification a la main).
        public static string Sig(string root)
        {
            try
            {
                var fi = new FileInfo(FileOf(root));
                return fi.Exists ? fi.LastWriteTimeUtc.Ticks + ":" + fi.Length : "";
            }
            catch { return ""; }
        }
    }
}
