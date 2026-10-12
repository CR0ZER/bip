// Rapport d'audit : une page HTML autonome (aucune ressource externe), generee a la
// demande dans audit\rapport.html et ouverte dans le navigateur. Filtres par periode,
// projet, decision et texte ; export CSV des lignes filtrees.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ClaudeNotch
{
    static class Report
    {
        public static string Generate(int days)
        {
            var rows = Audit.Rows(Program.Root, days);
            int count;
            string broken = Audit.Verify(Program.Root, out count);

            var data = rows.OrderByDescending(r => r.T).Select(r => (object)new Dictionary<string, object>
            {
                { "t", r.T.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) },
                { "p", r.Project }, { "dir", r.Dir ?? "" }, { "tool", r.Tool ?? "" }, { "obj", r.Obj ?? "" },
                { "dec", r.Decision ?? "" }, { "via", r.Via ?? "" }, { "res", r.Outcome ?? "" },
                { "who", (r.User ?? "") + " @ " + (r.Host ?? "") }, { "h", r.Hidden }
            }).ToList();

            string status = broken == null
                ? "Chaîne d'empreintes intacte · " + count.ToString("N0", CultureInfo.GetCultureInfo("fr-FR")) + " évènements vérifiés"
                : "⚠ Chaîne d'empreintes rompue (" + broken + ") : le journal a été modifié après coup.";

            string html = Template
                .Replace("{{STATUS_CLASS}}", broken == null ? "ok" : "ko")
                .Replace("{{STATUS}}", Html(status))
                .Replace("{{GENERATED}}", DateTime.Now.ToString("dd/MM/yyyy HH:mm"))
                .Replace("{{DAYS}}", days.ToString())
                .Replace("{{VERSION}}", AppVersion.Text)
                // Aucun "<" dans le bloc script : rien ne peut le refermer ni ouvrir un commentaire.
                .Replace("{{DATA}}", Json.Write(data).Replace("<", "\\u003c"));

            var path = Path.Combine(Program.Root, "audit", "rapport.html");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, html, new UTF8Encoding(false));
            return path;
        }

        public static void Open(int days)
        {
            try { Process.Start(Generate(days)); }
            catch (Exception ex) { Program.Log("rapport d'audit : " + ex.Message); }
        }

        static string Html(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        const string Template = @"<!doctype html>
<html lang=""fr""><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>Bip — journal d'audit</title>
<style>
:root{--bg:#f7f7f8;--card:#fff;--ink:#16161a;--muted:#6b6b76;--line:#e4e4e8;--ok:#1f8a4c;--ko:#c0392b;--chip:#efeff2}
@media (prefers-color-scheme:dark){:root{--bg:#0d0d0f;--card:#161618;--ink:#f2f2f2;--muted:#a3a3a3;--line:#2a2a2e;--ok:#3fd07f;--ko:#ff5d52;--chip:#232327}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:14px/1.45 ""Segoe UI Variable Text"",""Segoe UI"",system-ui,sans-serif}
header{padding:24px 28px 8px}h1{margin:0 0 4px;font-size:22px}.sub{color:var(--muted)}
.status{display:inline-block;margin-top:12px;padding:6px 12px;border-radius:8px;font-weight:600}
.status.ok{color:var(--ok);background:color-mix(in srgb,var(--ok) 12%,transparent)}.status.ko{color:var(--ko);background:color-mix(in srgb,var(--ko) 12%,transparent)}
.bar{display:flex;flex-wrap:wrap;gap:10px;align-items:center;padding:12px 28px;position:sticky;top:0;background:var(--bg);border-bottom:1px solid var(--line)}
select,input[type=search],button{font:inherit;color:inherit;background:var(--card);border:1px solid var(--line);border-radius:8px;padding:6px 10px}
input[type=search]{min-width:220px;flex:1}button{cursor:pointer;font-weight:600}label{color:var(--muted);user-select:none}
.count{color:var(--muted);margin-left:auto}
main{padding:12px 28px 40px}table{width:100%;border-collapse:collapse;background:var(--card);border:1px solid var(--line);border-radius:10px;overflow:hidden}
th,td{text-align:left;padding:8px 10px;border-bottom:1px solid var(--line);vertical-align:top}th{font-size:12px;color:var(--muted);font-weight:600;background:var(--chip)}
td.obj{font-family:""Cascadia Mono"",Consolas,monospace;font-size:12.5px;word-break:break-all;max-width:520px}
td.t{white-space:nowrap;color:var(--muted)}.chip{display:inline-block;padding:1px 8px;border-radius:99px;background:var(--chip);font-size:12px;white-space:nowrap}
.d-ok{color:var(--ok)}.d-ko{color:var(--ko)}.via{color:var(--muted);font-size:12px}footer{color:var(--muted);padding:0 28px 28px;font-size:12px}
</style></head><body>
<header><h1>Journal d'audit</h1>
<div class=""sub"">Ce que les agents ont fait sur ce poste, et qui l'a autorisé · {{DAYS}} derniers jours · généré le {{GENERATED}} par Bip v{{VERSION}}</div>
<div class=""status {{STATUS_CLASS}}"">{{STATUS}}</div></header>
<div class=""bar"">
<select id=""per""><option value=""1"">Aujourd'hui</option><option value=""7"" selected>7 jours</option><option value=""30"">30 jours</option><option value=""0"">Tout</option></select>
<select id=""proj""><option value="""">Tous les projets</option></select>
<select id=""dec""><option value="""">Toutes les décisions</option><option value=""toi"">Autorisé ou refusé par toi</option><option value=""office"">Autorisé d'office</option><option value=""refus"">Refusé</option><option value=""regle"">Règles</option></select>
<input type=""search"" id=""q"" placeholder=""Rechercher une commande, un fichier…"">
<label><input type=""checkbox"" id=""all""> lectures et outils internes</label>
<button id=""csv"">Exporter en CSV</button><span class=""count"" id=""n""></span></div>
<main><table><thead><tr><th>Date</th><th>Projet</th><th>Outil</th><th>Objet</th><th>Décision</th><th>Résultat</th></tr></thead><tbody id=""tb""></tbody></table></main>
<footer>Les secrets reconnaissables (jetons, clés d'API, mots de passe) sont masqués avant écriture. Le journal reste sur ce poste.<br>
La chaîne d'empreintes détecte une ligne modifiée ou supprimée après coup, et une fin de journal tronquée. Elle ne protège pas
contre un programme qui tourne sous ton compte et recalculerait toute la chaîne : c'est une trace de bonne foi, pas un coffre-fort.</footer>
<script>
const DATA = {{DATA}};
const $ = id => document.getElementById(id);
const day = d => d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');   // date locale
const esc = s => String(s).replace(/[&<>""]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','""':'&quot;'}[c]));
[...new Set(DATA.map(r => r.p))].sort().forEach(p => { const o = document.createElement('option'); o.value = o.textContent = p; $('proj').appendChild(o); });
function filtered() {
  const per = +$('per').value, proj = $('proj').value, dec = $('dec').value, q = $('q').value.toLowerCase(), all = $('all').checked;
  const since = per ? day(new Date(Date.now() - (per - 1) * 864e5)) : '';
  return DATA.filter(r => (all || !r.h) && (!since || r.t.slice(0, 10) >= since) && (!proj || r.p === proj)
    && (!dec || (dec === 'toi' && r.via.startsWith('toi') && r.tool !== 'Règle') || (dec === 'office' && r.dec.includes('office'))
        || (dec === 'refus' && r.dec.startsWith('refus')) || (dec === 'regle' && r.tool === 'Règle'))
    && (!q || (r.obj + ' ' + r.tool + ' ' + r.p).toLowerCase().includes(q)));
}
function render() {
  const rows = filtered(), shown = rows.slice(0, 3000);
  $('n').textContent = rows.length + ' action' + (rows.length > 1 ? 's' : '') + (rows.length > shown.length ? ' (3000 affichées)' : '');
  $('tb').innerHTML = shown.map(r => `<tr><td class=""t"">${esc(r.t)}</td><td title=""${esc(r.dir)}"">${esc(r.p)}</td><td><span class=""chip"">${esc(r.tool)}</span></td>`
    + `<td class=""obj"">${esc(r.obj)}</td><td class=""${r.dec.startsWith('refus') ? 'd-ko' : r.dec.startsWith('autoris') ? 'd-ok' : ''}"">${esc(r.dec)}<div class=""via"">${esc(r.via)}</div></td>`
    + `<td title=""${esc(r.who)}"">${esc(r.res)}</td></tr>`).join('');
}
$('csv').onclick = () => {
  const q = s => { s = String(s); if (/^[=+\-@\t\r]/.test(s)) s = ""'"" + s; return '""' + s.replace(/""/g, '""""') + '""'; };
  const lines = [['Date', 'Projet', 'Dossier', 'Outil', 'Objet', 'Décision', 'Par', 'Résultat', 'Compte et poste'].map(q).join(';')]
    .concat(filtered().map(r => [r.t, r.p, r.dir, r.tool, r.obj, r.dec, r.via, r.res, r.who].map(q).join(';')));
  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob(['﻿' + lines.join('\r\n')], { type: 'text/csv;charset=utf-8' }));
  a.download = 'bip-audit-' + day(new Date()) + '.csv';
  a.click();
};
['per', 'proj', 'dec', 'all'].forEach(id => $(id).onchange = render);
$('q').oninput = render;
render();
</script></body></html>";
    }
}
