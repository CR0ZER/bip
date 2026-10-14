// Rapport d'audit : une page HTML autonome (aucune ressource externe), generee a la
// demande dans audit\rapport.html et ouverte dans le navigateur. Les actions sont
// regroupees par session (titre du chat Claude), puis par agent : l'agent principal, et
// sous lui chaque sous-agent avec ses propres actions. Filtres, synthese, export CSV.

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
            var titles = SessionTitles(rows.Select(r => r.Sess).Where(s => s != null).Distinct());

            var data = rows.OrderBy(r => r.T).Select(r => (object)new Dictionary<string, object>
            {
                { "t", r.T.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) },
                { "s", r.Rule ? "bip" : r.Sess ?? "" }, { "a", r.Agent ?? "" }, { "sp", r.Spawned ?? "" },
                { "k", Kind(r) }, { "tool", r.Tool ?? "" },
                { "o", Short(r.Obj, r.Dir) }, { "of", r.Obj ?? "" },
                { "p", r.Project }, { "dir", r.Dir ?? "" },
                { "dec", r.Decision ?? "" }, { "via", r.Via ?? "" }, { "res", r.Outcome ?? "" },
                { "who", (r.User ?? "") + " @ " + (r.Host ?? "") }, { "h", r.Hidden }
            }).ToList();

            var sessions = new Dictionary<string, object>();
            foreach (var g in rows.Where(r => !r.Rule && r.Sess != null).GroupBy(r => r.Sess))
            {
                var dir = g.Select(r => r.Dir).FirstOrDefault(d => !string.IsNullOrEmpty(d));
                string title;
                titles.TryGetValue(g.Key, out title);
                sessions[g.Key] = new Dictionary<string, object> { { "t", title ?? "" }, { "p", dir == null ? "—" : Session.NameOf(dir) } };
            }
            sessions["bip"] = new Dictionary<string, object> { { "t", "Règles créées par Bip" }, { "p", "" } };

            string status = broken == null
                ? "Chaîne d'empreintes intacte · " + count.ToString("N0", CultureInfo.GetCultureInfo("fr-FR")) + " évènements vérifiés"
                : "⚠ Chaîne d'empreintes rompue (" + broken + ") : le journal a été modifié après coup.";

            var payload = new Dictionary<string, object> { { "rows", data }, { "sessions", sessions } };
            string html = Template
                .Replace("{{STATUS_CLASS}}", broken == null ? "ok" : "ko")
                .Replace("{{STATUS}}", Html(status))
                .Replace("{{GENERATED}}", DateTime.Now.ToString("dd/MM/yyyy HH:mm"))
                .Replace("{{DAYS}}", days.ToString())
                .Replace("{{VERSION}}", AppVersion.Text)
                // Aucun "<" dans le bloc script : rien ne peut le refermer ni ouvrir un commentaire.
                .Replace("{{DATA}}", Json.Write(payload).Replace("<", "\\u003c"));

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

        // Titre du chat Claude de chaque session : son transcript porte le nom de la session.
        // Cherche d'abord le transcript note dans l'etat de la session, puis dans ~/.claude/projects.
        static Dictionary<string, string> SessionTitles(IEnumerable<string> ids)
        {
            var titles = new Dictionary<string, string>();
            try
            {
                var projects = Directory.GetDirectories(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects"));
                foreach (var id in ids)
                {
                    var known = TranscriptOf(id);
                    var title = known == null ? null : Agents.Title(known);
                    if (title != null) { titles[id] = title; continue; }
                    foreach (var p in projects)
                    {
                        var f = Path.Combine(p, id + ".jsonl");
                        if (!File.Exists(f)) continue;
                        var t = Agents.Title(f);
                        if (t != null) titles[id] = t;
                        break;
                    }
                }
            }
            catch { }
            return titles;
        }

        // Transcript note dans state\<id>.state ("etat|dossier|pid|claude|transcript"), ou null.
        static string TranscriptOf(string id)
        {
            try
            {
                var f = Path.Combine(Program.StateDir, id + ".state");
                if (!File.Exists(f)) return null;
                var p = File.ReadAllText(f).Split('|');
                return p.Length > 4 && p[3] == "claude" && p[4].Trim().Length > 0 ? p[4].Trim() : null;
            }
            catch { return null; }
        }

        static string Kind(AuditRow r)
        {
            if (r.Rule) return "rule";
            switch (r.Tool ?? "")
            {
                case "Bash": case "PowerShell": return "cmd";
                case "Edit": case "MultiEdit": case "Write": case "NotebookEdit": return "file";
                case "Read": case "Grep": case "Glob": case "LS": case "NotebookRead": return "read";
                case "WebFetch": case "WebSearch": return "web";
                case "Agent": case "Task": return "agent";
                case "SubagentHandback": return "report";
                default: return "other";
            }
        }

        // Chemins raccourcis au dossier du projet ; le chemin complet reste au survol.
        static string Short(string obj, string dir)
        {
            if (string.IsNullOrEmpty(obj) || string.IsNullOrEmpty(dir)) return obj ?? "";
            var d = dir.TrimEnd('\\', '/');
            return obj.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase) || obj.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase)
                ? "…" + obj.Substring(d.Length) : obj;
        }

        static string Html(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        // Pas un seul guillemet double dans le modele : attributs et chaines entre apostrophes.
        const string Template = @"<!doctype html>
<html lang='fr'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'>
<title>Bip — journal d'audit</title>
<style>
:root{--bg:#f6f6f4;--card:#fff;--ink:#141414;--muted:#6b6b70;--line:#e6e6e2;--soft:#f0f0ec;
 --green:#1f9d57;--red:#d0453a;--orange:#c27c0e;--blue:#2f6fd6;--purple:#7a52d6;--gray:#7d7d84;--yellow:#a8800a}
@media (prefers-color-scheme:dark){:root{--bg:#0b0b0d;--card:#141416;--ink:#f2f2f2;--muted:#a3a3a3;--line:#26262a;--soft:#1c1c1f;
 --green:#3fd07f;--red:#ff5d52;--orange:#f5a524;--blue:#6aa8ff;--purple:#b48cff;--gray:#9a9aa2;--yellow:#ffc93d}}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);font:14px/1.45 'Segoe UI Variable Text','Segoe UI',system-ui,sans-serif}
.mono{font-family:'Cascadia Mono',Consolas,monospace}
header{padding:28px 32px 6px;display:flex;gap:16px;align-items:center}
header svg{width:52px;height:52px;flex:none}
h1{margin:0;font-size:24px;letter-spacing:-.3px}.sub{color:var(--muted);margin-top:2px}
.status{display:inline-block;margin:10px 32px 0;padding:6px 12px;border-radius:999px;font-weight:600;font-size:13px}
.status.ok{color:var(--green);background:color-mix(in srgb,var(--green) 13%,transparent)}
.status.ko{color:var(--red);background:color-mix(in srgb,var(--red) 13%,transparent)}
.tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(130px,1fr));gap:10px;padding:18px 32px 4px}
.tile{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:12px 14px}
.tile b{display:block;font-size:24px;line-height:1.1}.tile span{color:var(--muted);font-size:12.5px}
.tile.g b{color:var(--green)}.tile.r b{color:var(--red)}.tile.o b{color:var(--orange)}.tile.y b{color:var(--yellow)}
.bar{display:flex;flex-wrap:wrap;gap:8px;align-items:center;padding:14px 32px;position:sticky;top:0;z-index:2;background:color-mix(in srgb,var(--bg) 92%,transparent);backdrop-filter:blur(8px);border-bottom:1px solid var(--line)}
select,input[type=search],button{font:inherit;font-size:13px;color:inherit;background:var(--card);border:1px solid var(--line);border-radius:10px;padding:7px 10px}
input[type=search]{min-width:200px;flex:1}button{cursor:pointer;font-weight:600}
label{color:var(--muted);font-size:13px;user-select:none;display:flex;gap:6px;align-items:center}
.count{color:var(--muted);font-size:13px;margin-left:auto}
main{padding:16px 32px 32px;display:flex;flex-direction:column;gap:12px}
details.sess{background:var(--card);border:1px solid var(--line);border-radius:16px;overflow:hidden}
summary{list-style:none;cursor:pointer;display:flex;gap:12px;align-items:center;padding:14px 18px}
summary::-webkit-details-marker{display:none}
summary .dot{width:10px;height:10px;border-radius:50%;background:var(--green);flex:none}
summary .dot.bip{background:var(--yellow)}
summary .name{font-weight:600}summary .folder{color:var(--muted)}
summary .meta{margin-left:auto;color:var(--muted);font-size:12.5px;white-space:nowrap}
summary .chev{color:var(--muted);transition:transform .15s}details[open] summary .chev{transform:rotate(90deg)}
.rows{border-top:1px solid var(--line)}
.row{display:grid;grid-template-columns:64px 92px minmax(0,1fr) 210px 70px;gap:12px;align-items:start;padding:8px 18px;border-top:1px solid var(--soft)}
.row:first-child{border-top:0}
.row .t{color:var(--muted);font-size:12.5px;padding-top:1px}
.row .o{font-size:12.5px;word-break:break-all;display:-webkit-box;-webkit-line-clamp:3;-webkit-box-orient:vertical;overflow:hidden;cursor:pointer}
.row .o.full{-webkit-line-clamp:unset}
.dayh{padding:10px 18px 4px;font-size:12px;font-weight:600;color:var(--muted);text-transform:capitalize;border-top:1px solid var(--line)}
.dayh:first-child{border-top:0}
.row .via{color:var(--muted);font-size:12px}
.row .res{color:var(--muted);font-size:12.5px}.row .res.ko{color:var(--red)}
.chip{display:inline-block;padding:1px 9px;border-radius:999px;font-size:12px;white-space:nowrap;color:var(--c);background:color-mix(in srgb,var(--c) 14%,transparent)}
.k-cmd{--c:var(--orange)}.k-file{--c:var(--blue)}.k-read{--c:var(--gray)}.k-web{--c:var(--purple)}.k-agent{--c:var(--green)}.k-rule{--c:var(--yellow)}.k-report{--c:var(--green)}.k-other{--c:var(--gray)}
.pill{font-weight:600;font-size:12.5px}.pill.ok{color:var(--green)}.pill.ko{color:var(--red)}.pill.na{color:var(--muted)}
.agent{margin:4px 18px 10px 40px;border-left:2px solid color-mix(in srgb,var(--green) 55%,transparent);border-radius:0 0 0 6px}
.agent-head{padding:6px 12px;font-size:13px;font-weight:600;color:var(--green)}
.agent-head span{color:var(--muted);font-weight:400;font-size:12px;margin-left:8px}
.agent .row{padding-left:12px;grid-template-columns:52px 92px minmax(0,1fr) 210px 70px}
.empty{color:var(--muted);text-align:center;padding:40px}
footer{color:var(--muted);padding:0 32px 32px;font-size:12px;line-height:1.6}
@media (max-width:820px){.row,.agent .row{grid-template-columns:52px 1fr}.row .o{grid-column:1/-1}.row>div:nth-child(4),.row>div:nth-child(5){grid-column:auto}summary .meta{display:none}}
</style></head><body>
<header>
<svg viewBox='0 0 32 32' aria-hidden='true'><path d='M24 16 L28.5 10.5' transform='rotate(-14 24 16)' fill='none' stroke='#3FD07F' stroke-width='3' stroke-linecap='round'/><rect x='7' y='11' width='18' height='14' rx='4' fill='#3FD07F'/><rect x='10' y='24' width='4' height='4' rx='1.2' fill='#3FD07F'/><rect x='18' y='24' width='4' height='4' rx='1.2' fill='#3FD07F'/><path d='M16 11 V6.5' fill='none' stroke='#3FD07F' stroke-width='1.8'/><ellipse cx='16' cy='5' rx='2.2' ry='2.2' fill='#3FD07F'/><path d='M11 18.8 Q13 15.8 15 18.8' fill='none' stroke='#141414' stroke-width='2.2' stroke-linecap='round'/><path d='M17 18.8 Q19 15.8 21 18.8' fill='none' stroke='#141414' stroke-width='2.2' stroke-linecap='round'/><path d='M14.5 21.2 Q16 22.8 17.5 21.2' fill='none' stroke='#141414' stroke-width='1.6' stroke-linecap='round'/></svg>
<div><h1>Journal d'audit</h1>
<div class='sub'>Ce que les agents ont fait sur ce poste, et qui l'a autorisé · {{DAYS}} derniers jours · généré le {{GENERATED}} par Bip v{{VERSION}}</div></div>
</header>
<div class='status {{STATUS_CLASS}}'>{{STATUS}}</div>
<section class='tiles' id='tiles'></section>
<div class='bar'>
<select id='per'><option value='1'>Aujourd'hui</option><option value='7' selected>7 jours</option><option value='30'>30 jours</option><option value='0'>Tout</option></select>
<select id='proj'><option value=''>Tous les projets</option></select>
<select id='sess'><option value=''>Toutes les sessions</option></select>
<select id='agt'><option value=''>Tous les agents</option><option value='main'>Agent principal</option><option value='sub'>Sous-agents</option></select>
<select id='dec'><option value=''>Toutes les décisions</option><option value='toi'>Autorisé ou refusé par toi</option><option value='office'>Autorisé d'office</option><option value='refus'>Refusé</option><option value='regle'>Règles</option></select>
<input type='search' id='q' placeholder='Rechercher une commande, un fichier…'>
<label><input type='checkbox' id='all'> lectures et outils internes</label>
<button id='csv'>Exporter en CSV</button><span class='count' id='n'></span></div>
<main id='list'></main>
<footer>Les secrets reconnaissables (jetons, clés d'API, mots de passe) sont masqués avant écriture. Le journal reste sur ce poste.<br>
La chaîne d'empreintes détecte une ligne modifiée ou supprimée après coup, et une fin de journal tronquée. Elle ne protège pas
contre un programme qui tourne sous ton compte et recalculerait toute la chaîne : c'est une trace de bonne foi, pas un coffre-fort.</footer>
<script>
const D = {{DATA}}, R = D.rows, S = D.sessions;
const $ = id => document.getElementById(id);
const Q = String.fromCharCode(34);
const day = d => d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');   // date locale
const esc = s => String(s).replace(/[&<>'\x22]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', ['\x27']: '&#39;', [Q]: '&quot;' }[c]));
const KIND = { cmd: 'commande', file: 'fichier', read: 'lecture', web: 'web', agent: 'sous-agent', report: 'compte rendu', rule: 'règle', other: 'outil' };
const sname = id => { const s = S[id] || {}; return (s.t || s.p || id) + (s.t && s.p ? ' · ' + s.p + '/' : (!s.t && s.p ? '/' : '')); };
const add = (sel, v, t) => { const o = document.createElement('option'); o.value = v; o.textContent = t; $(sel).appendChild(o); };
[...new Set(R.map(r => r.p))].filter(p => p !== '—').sort().forEach(p => add('proj', p, p));
[...new Set(R.map(r => r.s))].forEach(s => add('sess', s, sname(s)));

function filtered() {
  const per = +$('per').value, proj = $('proj').value, sess = $('sess').value, agt = $('agt').value, dec = $('dec').value, q = $('q').value.toLowerCase(), all = $('all').checked;
  const since = per ? day(new Date(Date.now() - (per - 1) * 864e5)) : '';
  return R.filter(r => (all || !r.h) && (!since || r.t.slice(0, 10) >= since) && (!proj || r.p === proj) && (!sess || r.s === sess)
    && (!agt || (agt === 'main' ? !r.a : !!r.a || r.k === 'agent'))
    && (!dec || (dec === 'toi' && r.via.startsWith('toi') && r.k !== 'rule') || (dec === 'office' && r.dec.includes('office'))
        || (dec === 'refus' && r.dec.startsWith('refus')) || (dec === 'regle' && r.k === 'rule'))
    && (!q || (r.of + ' ' + r.tool + ' ' + r.p + ' ' + sname(r.s)).toLowerCase().includes(q)));
}

function tiles(rows) {
  const n = f => rows.filter(f).length;
  const t = [['', rows.length, 'actions'], ['g', n(r => r.via.startsWith('toi') && r.dec.startsWith('autoris')), 'autorisées par toi'],
             ['', n(r => r.dec.includes('office')), 'autorisées d\x27office'], ['r', n(r => r.dec.startsWith('refus')), 'refus'],
             ['o', n(r => r.res === 'échoué'), 'échecs'], ['y', n(r => r.k === 'rule'), 'règles']];
  $('tiles').innerHTML = t.map(x => `<div class='tile ${x[0]}'><b>${x[1]}</b><span>${x[2]}</span></div>`).join('');
}

const pill = d => d.startsWith('refus') ? 'ko' : d.startsWith('autoris') || d.startsWith('règle') ? 'ok' : 'na';
const line = (r, sub) => `<div class='row'><div class='t'>${esc(r.t.slice(11, sub ? 16 : 16))}</div>`
  + `<div><span class='chip k-${r.k}' title='${esc(r.tool)}'>${KIND[r.k] || 'outil'}</span></div>`
  + `<div class='o mono' title='${esc(r.of)}'>${esc(r.o)}</div>`
  + `<div><div class='pill ${pill(r.dec)}'>${esc(r.dec)}</div><div class='via'>${esc(r.via)}</div></div>`
  + `<div class='res ${r.res === 'échoué' ? 'ko' : ''}' title='${esc(r.who)}'>${esc(r.res)}</div></div>`;

// '3 actions · 4 lectures masquées' : un sous-agent fait surtout des lectures, masquées par defaut.
function count(list, agent) {
  const hidden = $('all').checked ? 0 : R.filter(x => x.a === agent && x.h).length;
  return list.length + ' action' + (list.length > 1 ? 's' : '') + (hidden ? ' · ' + hidden + ' lecture' + (hidden > 1 ? 's' : '') + ' masquée' + (hidden > 1 ? 's' : '') : '');
}

function block(id, rows, open) {
  // Agent principal en tete ; chaque lancement de sous-agent suivi de ses actions, en retrait.
  const subs = {}; rows.filter(r => r.a).forEach(r => (subs[r.a] = subs[r.a] || []).push(r));
  const shownSubs = new Set(); let html = '', lastDay = '';
  const multi = rows[0].t.slice(0, 10) !== rows[rows.length - 1].t.slice(0, 10);
  rows.filter(r => !r.a).forEach(r => {
    // Session etalee sur plusieurs jours : un separateur a chaque changement de jour.
    if (multi && r.t.slice(0, 10) !== lastDay) {
      lastDay = r.t.slice(0, 10);
      html += `<div class='dayh'>${new Date(lastDay + 'T12:00:00').toLocaleDateString('fr-FR', { weekday: 'long', day: 'numeric', month: 'long' })}</div>`;
    }
    html += line(r);
    if (r.k === 'agent' && r.sp) {
      shownSubs.add(r.sp);
      const list = subs[r.sp] || [];
      html += `<div class='agent'><div class='agent-head'>↳ ${esc(r.o)}<span>${count(list, r.sp)}</span></div>`
        + list.map(x => line(x, true)).join('') + `</div>`;
    }
  });
  Object.keys(subs).filter(a => !shownSubs.has(a)).forEach(a => {
    html += `<div class='agent'><div class='agent-head'>↳ Sous-agent<span>${count(subs[a], a)}</span></div>` + subs[a].map(x => line(x, true)).join('') + `</div>`;
  });
  const first = rows[0].t, last = rows[rows.length - 1].t;
  const when = first.slice(0, 10) === last.slice(0, 10) ? first.slice(8, 10) + '/' + first.slice(5, 7) + ' · ' + first.slice(11, 16) + ' → ' + last.slice(11, 16)
                                                       : first.slice(8, 10) + '/' + first.slice(5, 7) + ' → ' + last.slice(8, 10) + '/' + last.slice(5, 7);
  const refus = rows.filter(r => r.dec.startsWith('refus')).length;
  const s = S[id] || {};
  return `<details class='sess'${open ? ' open' : ''}><summary><span class='dot ${id === 'bip' ? 'bip' : ''}'></span>`
    + (s.t ? `<span class='name'>${esc(s.t)}</span>${s.p ? `<span class='folder mono'>· ${esc(s.p)}/</span>` : ''}` : `<span class='name mono'>${esc((s.p || id) + (s.p ? '/' : ''))}</span>`)
    + `<span class='meta'>${when} · ${rows.length} action${rows.length > 1 ? 's' : ''}${refus ? ' · ' + refus + ' refus' : ''}</span><span class='chev'>›</span></summary>`
    + `<div class='rows'>${html}</div></details>`;
}

function render() {
  const rows = filtered(), shown = rows.slice(-3000);
  tiles(rows);
  $('n').textContent = rows.length + ' action' + (rows.length > 1 ? 's' : '') + (rows.length > shown.length ? ' (3000 affichées)' : '');
  const by = {}; shown.forEach(r => (by[r.s] = by[r.s] || []).push(r));
  const ids = Object.keys(by).sort((a, b) => by[b][by[b].length - 1].t.localeCompare(by[a][by[a].length - 1].t));
  $('list').innerHTML = ids.length ? ids.map((id, i) => block(id, by[id], i === 0 || ids.length <= 3)).join('') : `<div class='empty'>Aucune action pour ces filtres.</div>`;
}

$('csv').onclick = () => {
  const q = s => { s = String(s); if (/^[=+\-@\t\r]/.test(s)) s = '\x27' + s; return Q + s.split(Q).join(Q + Q) + Q; };
  const lines = [['Date', 'Session', 'Agent', 'Projet', 'Dossier', 'Outil', 'Objet', 'Décision', 'Par', 'Résultat', 'Compte et poste'].map(q).join(';')]
    .concat(filtered().map(r => [r.t, sname(r.s), r.a ? 'sous-agent ' + r.a : 'principal', r.p, r.dir, r.tool, r.of, r.dec, r.via, r.res, r.who].map(q).join(';')));
  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob(['﻿' + lines.join('\r\n')], { type: 'text/csv;charset=utf-8' }));
  a.download = 'bip-audit-' + day(new Date()) + '.csv';
  a.click();
};
['per', 'proj', 'sess', 'agt', 'dec', 'all'].forEach(id => $(id).onchange = render);
$('q').oninput = render;
// Un clic sur une commande ou un chemin long le deplie (3 lignes par defaut).
$('list').onclick = e => { const o = e.target.closest('.o'); if (o) o.classList.toggle('full'); };
render();
</script></body></html>";
    }
}
