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

    // Dossier temporaire et empreinte de fin de chaine qu'il a laissee dans le registre.
    static void Clean(string root)
    {
        try { Directory.Delete(root, true); } catch { }
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Bip\Audit", true))
                if (k != null) k.DeleteValue(Audit.HeadName(root), false);
        }
        catch { }
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
        Masked("mysql -u root -pMotDePasse42 app", "MotDePasse42");
        Masked("mysqldump -h db -pS3cr3t base > x.sql", "S3cr3t");
        Check("mysql -p seul (demande le mot de passe) intact", Audit.Mask("mysql -u root -p app") == "mysql -u root -p app");
        Masked("sshpass -p hunter2 ssh admin@host", "hunter2");
        Masked("$s = ConvertTo-SecureString \"Azerty123!\" -AsPlainText -Force", "Azerty123!");
        Masked("echo '-----BEGIN OPENSSH PRIVATE KEY-----\nb3BlbnNzaC1rZXk\n-----END OPENSSH PRIVATE KEY-----' > id", "b3BlbnNzaC1rZXk");
        Masked("set DB_PASS=tralala99", "tralala99");
        Masked("export STRIPE_KEY=rk_abcdef123456", "rk_abcdef123456");
        var mcp = Audit.Summarize("mcp__crm__update", new Dictionary<string, object>
            { { "query", "clients actifs" }, { "body", new string('x', 300) }, { "auth", "token=abcd1234efgh5678" } });
        Check("outil MCP : parametres et valeurs courtes", mcp.Contains("query=clients actifs") && mcp.Contains("body=" + new string('x', 80) + "…") && !mcp.Contains(new string('x', 81)));
        Check("outil MCP : secret masque avant la coupe", !mcp.Contains("abcd1234efgh5678") && mcp.Contains("[masqué]"));

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
        Check("& simple -> aucune", k("npm test & calc") == null);
        Check("saut de ligne -> aucune", k("npm test\nrm -rf x") == null);
        Check("retour chariot -> aucune", k("pytest\rdel x") == null);
        Check("git.exe status -> git status", k("git.exe status") == "git status");
        Check("python.exe -> aucune", k("python.exe -c x") == null);
        Check("powershell.exe -> aucune", k("powershell.exe -c x") == null);
        Check("hors liste : env, xargs, find, sed, gh -> aucune",
              k("env FOO=1 npm test") == null && k("xargs rm") == null && k("find . -delete") == null
              && k("sed -i s/a/b/ f") == null && k("gh repo delete x") == null);
        Check("bip todo -> aucune (toujours une demande)", k("bip todo \"x\"") == null);
        Check("script au nom douteux -> aucune", k("npm run $(x)") == null && k("npm run -s") == null);
        Check("dotnet build -> dotnet build", k("dotnet build -c Release") == "dotnet build");

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
        finally { Clean(root); }

        Console.WriteLine("Regroupement et reprise");
        Check("docker compose up -> aucune (hors liste blanche)", k("docker compose up") == null);
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
        finally { Clean(root); }

        Console.WriteLine("Journal allege");
        Check("outils internes non enregistres", Audit.Skipped("AskUserQuestion", "outil") && Audit.Skipped("mcp__ccd_turn__update_plan", "outil") && Audit.Skipped("SubagentHandback", "fait"));
        Check("lecture : l'action oui, le resultat non", !Audit.Skipped("Read", "outil") && Audit.Skipped("Read", "fait") && !Audit.Skipped("Read", "echec"));
        Check("commande : tout est enregistre", !Audit.Skipped("Bash", "outil") && !Audit.Skipped("Bash", "fait"));
        Check("objet sur l'action seulement (identifiant present)", Audit.NeedsObj("outil", "t1") && !Audit.NeedsObj("fait", "t1") && !Audit.NeedsObj("decision", "t1"));
        Check("objet garde sans identifiant, et pour un refus auto", Audit.NeedsObj("fait", null) && Audit.NeedsObj("refus-auto", "t1"));
        var longCmd = Audit.Summarize("Bash", new Dictionary<string, object> { { "command", new string('a', 1200) } });
        Check("objet plafonne a 500 caracteres, longueur notee", longCmd.StartsWith(new string('a', 500) + " … +700 caractères"));
        root = Path.Combine(Path.GetTempPath(), "bip-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Audit.Write(root, "outil", new Dictionary<string, object> { { "sess", "s" }, { "tool", "Bash" }, { "obj", "npm test" }, { "tuid", "c1" }, { "mode", "default" } });
            Audit.Write(root, "fait", new Dictionary<string, object> { { "sess", "s" }, { "tool", "Bash" }, { "tuid", "c1" } });
            Audit.Write(root, "outil", new Dictionary<string, object> { { "sess", "s" }, { "tool", "Read" }, { "obj", "C:\\p\\.env" }, { "tuid", "r1" }, { "mode", "default" } });
            var rows = Audit.Rows(root, 1);
            Check("resultat sans objet rattache par l'identifiant", rows.Count == 2 && rows[0].Obj == "npm test" && rows[0].Outcome == "réussi");
            Check("lecture sans resultat : autorisee d'office", rows[1].Decision == "autorisé d'office" && rows[1].Outcome == "—");
            Check("dossier au caractere invalide : pas d'exception", new AuditRow { Dir = "C:\\dev\u0007x\\api" }.Project == "api");
        }
        finally { Clean(root); }

        Console.WriteLine("Lecteur JSON");
        var o = Json.Obj(Json.Parse("{\"a\":\"\\u00e9t\\u00e9 \\\"x\\\"\\n\",\"n\":42,\"big\":12345678901,\"f\":1.5e2,\"neg\":-3,\"t\":true,\"z\":null,\"arr\":[1,{\"b\":[]}],\"e\":{}}"));
        Check("chaine avec echappements", Json.Str(o, "a") == "été \"x\"\n");
        Check("nombres : int, long, decimal", Json.Get(o, "n") is int && Json.Get(o, "big") is long && Convert.ToDouble(Json.Get(o, "f")) == 150 && (int)Json.Get(o, "neg") == -3);
        Check("vrai, null, tableau, objet vide", (bool)Json.Get(o, "t") && Json.Get(o, "z") == null && Json.Arr(Json.Get(o, "arr")).Length == 2 && Json.Obj(Json.Get(o, "e")).Count == 0);
        Check("aller-retour Write -> Parse", Json.Str(Json.Obj(Json.Parse(Json.Write(o))), "a") == "été \"x\"\n");
        bool threw = false;
        try { Json.Parse("{\"a\":1,}"); } catch (FormatException) { threw = true; }
        Check("JSON invalide refuse", threw);
        threw = false;
        try { Json.Parse("{\"a\":\"coup"); } catch (FormatException) { threw = true; }
        Check("ligne coupee refusee", threw);

        Console.WriteLine("Lecture incrementale du journal");
        root = Path.Combine(Path.GetTempPath(), "bip-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (int i = 0; i < 3; i++)
                Audit.Write(root, "outil", new Dictionary<string, object> { { "sess", "s" }, { "tool", "Bash" }, { "obj", "echo " + i }, { "tuid", "u" + i } });
            Check("3 actions lues", Audit.Rows(root, 1).Count == 3);
            Audit.Write(root, "fait", new Dictionary<string, object> { { "sess", "s" }, { "tool", "Bash" }, { "obj", "echo 1" }, { "tuid", "u1" } });
            Audit.Write(root, "outil", new Dictionary<string, object> { { "sess", "s" }, { "tool", "Bash" }, { "obj", "echo 3" }, { "tuid", "u3" } });
            var rows = Audit.Rows(root, 1);
            Check("ajouts lus, resultat rattache a une action deja en cache", rows.Count == 4 && rows[1].Outcome == "réussi");
            var file = Directory.GetFiles(Path.Combine(root, "audit"))[0];
            File.AppendAllText(file, "{\"ev\":\"outil\",\"sess\":\"s\",\"tool\":\"Bash\",\"obj\":\"moi");
            Check("ligne en cours d'ecriture ignoree", Audit.Rows(root, 1).Count == 4);
            File.AppendAllText(file, "tie\"}\n");
            Check("ligne terminee lue ensuite", Audit.Rows(root, 1).Count == 5);
            File.WriteAllText(file, "");
            Check("fichier raccourci relu en entier", Audit.Rows(root, 1).Count == 0);
        }
        finally { Clean(root); }

        Console.WriteLine("Sous-agents");
        Func<int, long> T = s => new DateTime(2026, 10, 14, 15, 38, s).Ticks;
        // Capture reelle du 2026-10-14 : deux en arriere-plan (A, B), un au premier plan (C).
        var ag = Agents.Parse(new[]
        {
            "lance|" + T(24) + "|tA|Compte les fichiers sources",
            "lance|" + T(26) + "|tB|Cherche les constantes de délai",
            "lance|" + T(28) + "|tC|Vérifie la version du CHANGELOG",
            "id|" + T(29) + "|tA|agA",
            "debut|" + T(29) + "|agA", "debut|" + T(29) + "|agB", "debut|" + T(29) + "|agC",
            "id|" + T(29) + "|tB|agB",
            "fin|" + T(35) + "|agA",
            "fin|" + T(35) + "|agC", "id|" + T(35) + "|tC|agC", "fin|" + T(35) + "|agC",
        });
        Check("trois sous-agents, ordre de lancement", ag.Count == 3 && ag[1].Description == "Cherche les constantes de délai");
        Check("arriere-plan relie par la reponse", ag[0].Id == "agA" && ag[1].Id == "agB");
        Check("premier plan apparie par ordre", ag[2].Id == "agC");
        Check("fins : A et C finis, B en cours", ag[0].End != null && ag[1].End == null && ag[2].End != null);
        // Premier plan pas encore relie (reponse pas arrivee) : appariement par le debut libre.
        var ag2 = Agents.Parse(new[] { "lance|" + T(1) + "|t1|Un", "lance|" + T(2) + "|t2|Deux", "debut|" + T(3) + "|x1", "debut|" + T(3) + "|x2", "fin|" + T(9) + "|x2" });
        Check("deux au premier plan, appariement dans l'ordre", ag2[0].Id == "x1" && ag2[1].Id == "x2" && ag2[1].End != null && ag2[0].End == null);
        var ag3 = Agents.Parse(new[] { "lance|" + T(1) + "|t1|Un", "echec|" + T(4) + "|t1", "ligne illisible" });
        Check("echec de l'outil : termine en echec", ag3.Count == 1 && ag3[0].Failed && ag3[0].End != null);

        Console.WriteLine("Titre de la session");
        Check("dernier titre retenu", Agents.LastTitle("{\"type\":\"custom-title\",\"customTitle\":\"Claude Voyant\"}\n{\"x\":1}\n{\"type\":\"custom-title\",\"customTitle\":\"Claude Voyant - Bip\"}\n") == "Claude Voyant - Bip");
        Check("titre avec accents et guillemets", Agents.LastTitle("{\"customTitle\":\"Feuille de route \\\"IA\\\" \\u00e9t\\u00e9\"}") == "Feuille de route \"IA\" été");
        Check("pas de titre", Agents.LastTitle("{\"type\":\"user\"}") == null);
        var tr = Path.Combine(Path.GetTempPath(), "bip-tests-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var filler = new string('x', 300 * 1024);   // titre au-dela du premier bloc lu depuis la fin
            File.WriteAllText(tr, "{\"type\":\"custom-title\",\"customTitle\":\"Ancien\"}\n{\"m\":\"" + filler + "\"}\n");
            Check("titre trouve en remontant plusieurs blocs", Agents.Title(tr) == "Ancien");
            File.AppendAllText(tr, "{\"type\":\"custom-title\",\"customTitle\":\"Renommé\"}\n");
            Check("renommage lu dans les nouvelles lignes", Agents.Title(tr) == "Renommé");
            Check("transcript absent : pas de titre", Agents.Title(tr + ".absent") == null);
        }
        finally { try { File.Delete(tr); } catch { } }

        Console.WriteLine(_fail == 0 ? "Tous les tests passent." : _fail + " test(s) en échec.");
        return _fail == 0 ? 0 : 1;
    }
}
