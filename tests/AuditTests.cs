// Tests du journal d'audit : masquage des secrets, cle des regles suggerees, chainage.
// Lances par tests\run.cmd (et par la release GitHub avant de publier).

using System;
using System.Collections.Generic;
using System.IO;
using ClaudeNotch;

static class AuditTests
{
    static int _fail;

    static void Check(string label, bool ok)
    {
        Console.WriteLine((ok ? "  ok    " : "  ÉCHEC ") + label);
        if (!ok) _fail++;
    }

    static void Masked(string input, string secret)
    {
        var m = Audit.Mask(input);
        Check("masque : " + input, !m.Contains(secret) && m.Contains("[masqué]"));
    }

    static int Main()
    {
        Console.WriteLine("Masquage des secrets");
        Masked("curl -H \"Authorization: Bearer abcdefghijklmnop123\" x", "abcdefghijklmnop123");
        Masked("set DB_PASSWORD=hunter2hunter2", "hunter2hunter2");
        Masked("export GITHUB_TOKEN=ghp_abcdefghijklmnopqrstu", "abcdefghijklmnopqrstu");
        Masked("ANTHROPIC_API_KEY=sk-ant-api03-abcdefghijklmnop", "abcdefghijklmnop");
        Masked("psql postgres://admin:S3cretPass@db:5432/app", "S3cretPass");
        Masked("curl -u florian:motdepasse123 https://x", "motdepasse123");
        Masked("az storage --connection-string \"AccountKey=QWERTYUIOPasdfgh==;\"", "QWERTYUIOPasdfgh");
        Masked("https://api.example.com/v1?access_token=zzzzyyyyxxxx", "zzzzyyyyxxxx");
        Check("texte ordinaire intact", Audit.Mask("npm run build -- --prod") == "npm run build -- --prod");

        Console.WriteLine("Cle des regles suggerees");
        Func<string, string> k = Audit.RuleKey;
        Check("npm test -> npm test", k("npm test") == "npm test");
        Check("pytest -q -> pytest", k("pytest -q") == "pytest");
        Check("git status -> git status", k("git status") == "git status");
        Check("npm run test -> npm run test", k("npm run test") == "npm run test");
        Check("npm run (sans script) -> aucune", k("npm run") == null);
        Check("git --no-pager log -> aucune", k("git --no-pager log") == null);
        Check("git -C x status -> aucune", k("git -C x status") == null);
        Check("rm a.tmp -> aucune", k("rm a.tmp") == null);
        Check("python script.py -> aucune", k("python script.py") == null);
        Check("node -e ... -> aucune", k("node -e \"x\"") == null);
        Check("curl url -> aucune", k("curl https://x") == null);
        Check("git push -> aucune", k("git push origin main") == null);
        Check("commande composee -> aucune", k("npm test && npm run build") == null);
        Check("redirection -> aucune", k("echo x > f.txt") == null);

        Console.WriteLine("Chainage des empreintes");
        var root = Path.Combine(Path.GetTempPath(), "bip-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (int i = 0; i < 5; i++)
                Audit.Write(root, "outil", new Dictionary<string, object> { { "sess", "s" }, { "tool", "Bash" }, { "obj", "echo " + i } });
            int n;
            Check("chaine intacte (5 evenements)", Audit.Verify(root, out n) == null && n == 5);
            var file = Directory.GetFiles(Path.Combine(root, "audit"))[0];
            var lines = File.ReadAllLines(file);
            lines[2] = lines[2].Replace("echo 2", "echo 9");
            File.WriteAllLines(file, lines);
            Check("ligne modifiee detectee", Audit.Verify(root, out n) != null);
            File.WriteAllLines(file, new[] { lines[0], lines[1] });
            Check("fin tronquee detectee", Audit.Verify(root, out n) != null);
        }
        finally { try { Directory.Delete(root, true); } catch { } }

        Console.WriteLine("Regroupement et reprise");
        Check("docker compose up -> docker compose up", k("docker compose up") == "docker compose up");
        Check("docker compose run -> aucune", k("docker compose run web sh") == null);
        root = Path.Combine(Path.GetTempPath(), "bip-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // Hooks paralleles : le resultat arrive avant l'outil dans le fichier.
            Audit.Write(root, "fait", new Dictionary<string, object> { { "sess", "s" }, { "tool", "Bash" }, { "obj", "npm test" }, { "tuid", "t1" } });
            Audit.Write(root, "outil", new Dictionary<string, object> { { "sess", "s" }, { "tool", "Bash" }, { "obj", "npm test" }, { "tuid", "t1" } });
            var rows = Audit.Rows(root, 1);
            Check("resultat avant l'outil rattache", rows.Count == 1 && rows[0].Outcome == "réussi");

            // Ecriture interrompue : la ligne suivante repart proprement.
            var file = Directory.GetFiles(Path.Combine(root, "audit"))[0];
            File.AppendAllText(file, "{\"ev\":\"outil\",\"obj\":\"coup");
            Audit.Write(root, "outil", new Dictionary<string, object> { { "sess", "s" }, { "tool", "Bash" }, { "obj", "echo ok" } });
            var last = File.ReadAllLines(file);
            Check("ligne coupee isolee", last.Length == 4 && last[3].StartsWith("{") && last[3].Contains("echo ok"));
        }
        finally { try { Directory.Delete(root, true); } catch { } }

        Console.WriteLine(_fail == 0 ? "Tous les tests passent." : _fail + " test(s) en échec.");
        return _fail == 0 ? 0 : 1;
    }
}
