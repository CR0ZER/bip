<div align="center">

<img src="design/bip-mascotte/svg/bip-pret.svg" width="96" alt="Bip">

# Bip

**L'encoche Claude Code pour Windows.**
Un petit robot par session, collé en haut de l'écran : tu vois d'un coup d'œil qui travaille,
qui a fini et qui t'attend, et tu réponds sans changer de fenêtre.

![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4)
![.NET Framework 4.x](https://img.shields.io/badge/.NET%20Framework-4.x-512BD4)
![Sans SDK](https://img.shields.io/badge/compil%C3%A9%20avec-csc%20de%20Windows-555)
![Licence MIT](https://img.shields.io/badge/licence-MIT-2EA44F)
[![Dernière version](https://img.shields.io/github/v/release/CR0ZER/bip?label=version)](https://github.com/CR0ZER/bip/releases/latest)

**🇫🇷 Français** · [🇬🇧 English](#english)

<img src="docs/screenshots/permission.png" width="640" alt="Encoche dépliée : une demande de permission et deux sessions">

</div>

---

## Pourquoi

Claude Code travaille seul pendant de longues minutes, souvent dans plusieurs sessions à la fois.
Le problème n'est pas de le lancer, c'est de **savoir quand il a besoin de toi** : une commande
à autoriser, une question à trancher. Tant que personne ne répond, la session est bloquée.

Bip vit dans une encoche noire en haut au centre de l'écran, façon *Dynamic Island*. Repliée,
elle se fait oublier et laisse passer les clics ; au survol, elle se déplie et te laisse
répondre directement.

## Aperçu

| Repliée | Une demande arrive |
|:---:|:---:|
| <img src="docs/screenshots/repliee.png" width="400" alt="Encoche repliée, trois sessions"> | <img src="docs/screenshots/entrouverte.png" width="400" alt="Encoche entrouverte : data-pipeline attend ta réponse"> |
| Un Bip par session, dans l'ordre de lancement (5 au plus, puis « +N »). | L'encoche s'entrouvre 4 s pour dire qui attend, puis se replie ; un éclat rouge balaie son bord tant que la demande est ouverte. |

| Répondre à une question | Encoche vide |
|:---:|:---:|
| <img src="docs/screenshots/question.png" width="400" alt="Carte de question à choix"> | <img src="docs/screenshots/encoche-vide.png" width="400" alt="Bip salue, dernières sessions et bilan du jour"> |
| Choix unique, choix multiple ou réponse libre (« Autre… »). | Plus rien ne tourne : Bip se réveille, salue, montre les dernières sessions et le bilan du jour. |

## Bip

<img src="design/bip-mascotte/apercu-planche.png" alt="Les cinq états de Bip à 128, 40, 20 et 16 px">

| État | Quand | Ce qu'il fait |
|---|---|---|
| **En attente** (rouge) | Une permission ou une question attend ta réponse | Lève le bras, respire de plus en plus vite, tremble de temps en temps |
| **En cours** (orange) | Claude réfléchit, lit, exécute | Flotte doucement |
| **Prêt** (vert) | Claude a fini et rend la main | Fait un petit saut, puis s'efface au bout de 4 min |
| **Erreur** (gris, « ! ») | Le tour s'est arrêté sur une erreur de l'API | S'affaisse |
| **Au repos** (gris foncé) | Session ouverte, rien demandé encore | Dort, antenne pliée |

Chaque état se lit aussi sans la couleur (bras, pastille, antenne), même à 16 px.
Le kit complet (SVG, PNG, valeurs de référence, aperçu animé) est dans [`design/bip-mascotte`](design/bip-mascotte).

## Fonctionnalités

- **Suivi en direct** de toutes les sessions Claude Code du poste (app desktop, terminal, IDE).
- **Permissions depuis l'encoche** : Autoriser, Refuser, ou « Toujours » avec la portée écrite
  sur le bouton (« Toujours : toutes les modifications de fichiers », « Toujours : Edit(src/\*\*) »…),
  limitée à la session. Jamais de « Toujours » pour les commandes shell.
- **Questions (AskUserQuestion)** : choix unique, multiple, plusieurs questions, réponse libre.
- **Répondre dans Claude** à tout moment : la demande repart dans la fenêtre habituelle.
- **Encoche vide** : Bip salue (et réagit si tu cliques dessus), dernières sessions terminées,
  bilan du jour (sessions, temps de travail, réponses). Un clic ailleurs ouvre Claude.
- **Ne pas déranger** pendant 1 h : ni entrouverture, ni éclat, ni clignotement.
- **Escalade douce** : après 5 min d'attente, la fenêtre Claude clignote dans la barre des
  tâches (désactivable). Jamais de mise au premier plan forcée, aucun son.
- **Invisible quand il le faut** : masquée pendant les applis en plein écran.

## Installation

Prérequis : Windows 10 ou 11 (le .NET Framework 4.x est déjà présent) et Claude Code.
Répondre aux questions depuis l'encoche demande Claude Code 2.1.85 ou plus récent.

**Télécharger** (le plus simple) : prends `Bip-vX.Y.Z.zip` dans la
[dernière release](https://github.com/CR0ZER/bip/releases/latest), décompresse-le dans un dossier
permanent (par exemple `Documents\Bip`, pas dans Téléchargements : les hooks pointent vers ce
dossier), puis lance `installer.cmd`.

> Les exécutables ne sont pas signés : au premier lancement, Windows SmartScreen peut afficher
> « Windows a protégé votre ordinateur ». Clique sur *Informations complémentaires* →
> *Exécuter quand même*. L'empreinte SHA-256 du zip est publiée avec chaque release, et tu
> peux toujours compiler toi-même.

**Depuis les sources** (compilé sur ton poste, sans rien installer) :

```bat
git clone https://github.com/CR0ZER/bip.git
cd bip
installer.cmd
```

`installer.cmd` compile les deux exécutables si besoin, ajoute les hooks de Bip dans
`~/.claude/settings.json` (une sauvegarde datée est faite à côté), ajoute l'encoche au
démarrage de Windows et la lance. Il peut être relancé sans risque.

Pour tout retirer : `desinstaller.cmd`, puis supprimer le dossier. Seules les entrées de Bip
sont retirées de `settings.json` ; tes autres hooks ne sont pas touchés.

> Les nouvelles sessions Claude prennent les hooks en compte immédiatement ; une session déjà
> ouverte peut demander un redémarrage.

## Comment ça marche

```mermaid
flowchart LR
    CC["Claude Code<br/>(chaque session)"] -- "hooks async<br/>état de la session" --> H1["NotchHook.exe state"]
    H1 --> ST[("state/<br/>un fichier par session")]
    ST --> N["ClaudeNotch.exe<br/>l'encoche"]
    CC -- "hook synchrone<br/>PermissionRequest / AskUserQuestion" --> H2["NotchHook.exe permission | ask"]
    H2 <-- "pipe nommé<br/>réservé à ton compte" --> N
    H2 -- "décision JSON" --> CC
```

- **L'état des sessions** passe par des hooks asynchrones : ils ne ralentissent jamais Claude.
  `NotchHook.exe` écrit `état|dossier|pid` dans `state\`, l'encoche relit ces fichiers.
- **Les réponses** passent par des hooks synchrones : le hook transmet la demande à l'encoche
  par un pipe nommé, attend ton clic, puis renvoie la décision à Claude Code.
- **Tout est compilé par le `csc.exe` livré avec Windows**, sans SDK : `build.cmd` suffit.

## Sécurité

- **Échec sûr** : encoche fermée, plantée ou injoignable, délai dépassé, demande illisible →
  le hook ne répond rien et Claude pose la question lui-même. **Jamais d'autorisation implicite.**
- **Pipe réservé** au compte Windows courant ; le hook vérifie que le serveur est bien le
  `ClaudeNotch.exe` installé à côté de lui avant d'envoyer quoi que ce soit.
- **Rien ne se valide au survol** : un clic complet est nécessaire, et les boutons ne s'arment
  qu'après 600 ms (ouverture, nouvelle carte ou liste qui bouge).
- **Ce que tu lis est ce qui sera exécuté** : commande toujours affichée en entier (avec le
  nombre de lignes si elle est longue), caractères invisibles ou d'inversion (U+202E…) rendus
  visibles, description de Claude présentée comme telle (« Selon Claude : … »).

## Performances

Mesuré sur un poste de travail, sur 20 s :

| Situation | Processeur | Mémoire |
|---|---|---|
| Une session au travail (Bip animé) | ~4,7 % d'un cœur | ~68 Mo |
| Aucun Bip animé | ~0,7 % d'un cœur | ~69 Mo |
| `NotchHook.exe`, par appel d'outil | ~70 ms, en arrière-plan | — |

Rendu logiciel, animations en boucle à 15–30 images/s, éclat seulement quand quelqu'un attend,
fenêtre à la taille de l'encoche. Le détail des mesures est dans [`docs/CONCEPTION.md`](docs/CONCEPTION.md).

## Structure

```
src/
  App.cs          point d'entrée, installation / désinstallation, appels Windows
  NotchWindow.cs  l'encoche : formes, transformations, cartes, encoche vide
  Bip.cs          la mascotte (portage C# du contrôle WPF du kit)
  Model.cs        sessions, journal du jour, demandes en attente, pipe nommé
  NotchHook.cs    le hook appelé par Claude Code
  Json.cs         lecture / écriture JSON
  Version.cs      numéro de version (vérifié par la release)
design/bip-mascotte/   kit de la mascotte (SVG, PNG, tokens.json, aperçu animé)
docs/                  journal de conception et captures
.github/workflows/     release automatique à chaque tag vX.Y.Z
build.cmd · installer.cmd · desinstaller.cmd · CHANGELOG.md
```

**Diagnostic** : `ClaudeNotch.exe --dump-plan` affiche les sessions lues et ce que l'encoche
montrerait ; `notch.log` recueille les erreurs ; `journal\` garde 7 jours de bilan.

## Crédits

- **Inspiré de [coucou](https://github.com/Louis-CFM/coucou)** de Louis-CFM, l'encoche compagnon
  pour agents de code sur macOS (licence MIT). Bip en reprend l'idée (un bloc noir qui se
  transforme, des réponses depuis l'encoche) ; le code, la mascotte et le design sont propres
  à ce projet.
- **Mascotte et design** conçus avec Claude Design ; application développée avec Claude Code.
- Projet indépendant, non affilié à Anthropic. « Claude » et « Claude Code » sont des marques
  d'Anthropic.

## Licence

[MIT](LICENSE) © 2026 CR0ZER

<br>

---

<br>

## English

[🇫🇷 Version française ↑](#bip) · **🇬🇧 English**

**The Claude Code notch for Windows.** One little robot per session, docked at the top of your
screen: see at a glance who is working, who is done and who is waiting for you, and answer
without switching windows. *The interface is in French.*

### Why

Claude Code works on its own for long minutes, often in several sessions at once. Starting it
is easy; the hard part is **knowing when it needs you**: a command to approve, a question to
answer. Until someone replies, the session is stuck.

Bip lives in a black notch at the top centre of the screen, *Dynamic Island* style. Collapsed,
it stays out of the way and lets clicks through; hover it and it unfolds so you can answer
right there.

### Preview

| Collapsed | A request comes in |
|:---:|:---:|
| <img src="docs/screenshots/repliee.png" width="400" alt="Collapsed notch, three sessions"> | <img src="docs/screenshots/entrouverte.png" width="400" alt="Peeking notch: data-pipeline is waiting for you"> |
| One Bip per session, in launch order (5 at most, then "+N"). | The notch peeks open for 4 s to say who is waiting, then folds back; a red glow sweeps its edge while the request is open. |

| Answering a question | Empty notch |
|:---:|:---:|
| <img src="docs/screenshots/question.png" width="400" alt="Multiple-choice question card"> | <img src="docs/screenshots/encoche-vide.png" width="400" alt="Bip says hello, recent sessions and today's summary"> |
| Single choice, multiple choice or free text ("Autre…"). | Nothing running: Bip wakes up, says hello, and shows recent sessions and today's summary. |

### Meet Bip

<img src="design/bip-mascotte/apercu-planche.png" alt="Bip's five states at 128, 40, 20 and 16 px">

| State | When | What it does |
|---|---|---|
| **Waiting** (red) | A permission or a question needs your answer | Raises its arm, breathes faster and faster, trembles now and then |
| **Running** (orange) | Claude is thinking, reading, executing | Floats gently |
| **Ready** (green) | Claude is done and hands back control | Does a little hop, then fades out after 4 min |
| **Error** (grey, "!") | The turn stopped on an API error | Slumps |
| **Idle** (dark grey) | Session open, nothing asked yet | Sleeps, antenna folded |

Every state also reads without colour (arm, badge, antenna), even at 16 px.
The full kit (SVG, PNG, reference values, animated preview) is in [`design/bip-mascotte`](design/bip-mascotte).

### Features

- **Live tracking** of every Claude Code session on the machine (desktop app, terminal, IDE).
- **Permissions from the notch**: Allow, Deny, or "Always" with its scope written on the button
  ("Always: all file edits", "Always: Edit(src/\*\*)"…), limited to the session. Never "Always"
  for shell commands.
- **Questions (AskUserQuestion)**: single choice, multiple choice, several questions, free text.
- **Answer in Claude** at any time: the request goes back to the usual Claude window.
- **Empty notch**: Bip says hello (and reacts when you click it), recent finished sessions,
  today's summary (sessions, working time, answers). Clicking anywhere else opens Claude.
- **Do not disturb** for 1 h: no peeking, no glow, no flashing.
- **Gentle escalation**: after 5 min of waiting, the Claude window flashes in the taskbar
  (can be turned off). Never forced to the foreground, no sound at all.
- **Out of the way when needed**: hidden while a full-screen app is running.

### Installation

Requirements: Windows 10 or 11 (.NET Framework 4.x is already there) and Claude Code.
Answering questions from the notch needs Claude Code 2.1.85 or later.

**Download** (easiest): grab `Bip-vX.Y.Z.zip` from the
[latest release](https://github.com/CR0ZER/bip/releases/latest), unzip it into a permanent
folder (for instance `Documents\Bip`, not Downloads: the hooks point to that folder), then run
`installer.cmd`.

> The executables are not signed: on first launch, Windows SmartScreen may say "Windows protected
> your PC". Click *More info* → *Run anyway*. Each release ships the zip's SHA-256 checksum, and
> you can always build it yourself.

**From source** (built on your machine, nothing to install):

```bat
git clone https://github.com/CR0ZER/bip.git
cd bip
installer.cmd
```

`installer.cmd` builds both executables if needed, adds Bip's hooks to `~/.claude/settings.json`
(a dated backup is saved next to it), adds the notch to Windows startup and launches it. It is
safe to run again.

To remove everything: `desinstaller.cmd`, then delete the folder. Only Bip's entries are removed
from `settings.json`; your other hooks are left untouched.

> New Claude sessions pick up the hooks immediately; an already open session may need a restart.

### How it works

```mermaid
flowchart LR
    CC["Claude Code<br/>(each session)"] -- "async hooks<br/>session state" --> H1["NotchHook.exe state"]
    H1 --> ST[("state/<br/>one file per session")]
    ST --> N["ClaudeNotch.exe<br/>the notch"]
    CC -- "blocking hook<br/>PermissionRequest / AskUserQuestion" --> H2["NotchHook.exe permission | ask"]
    H2 <-- "named pipe<br/>restricted to your account" --> N
    H2 -- "JSON decision" --> CC
```

- **Session state** goes through async hooks, so they never slow Claude down. `NotchHook.exe`
  writes `state|folder|pid` into `state\`, and the notch reads those files.
- **Answers** go through blocking hooks: the hook hands the request to the notch over a named
  pipe, waits for your click, then returns the decision to Claude Code.
- **Everything is built with the `csc.exe` that ships with Windows**, no SDK: `build.cmd` is enough.

### Security

- **Fail-safe**: notch closed, crashed or unreachable, timeout, unreadable request → the hook
  returns nothing and Claude asks the question itself. **Never an implicit approval.**
- **Restricted pipe**, open to the current Windows account only; before sending anything, the
  hook checks that the server is the `ClaudeNotch.exe` installed next to it.
- **Nothing is approved on hover**: a full click is required, and buttons only arm after 600 ms
  (opening, new card or a list that moves).
- **What you read is what runs**: the command is always shown in full (with its line count when
  long), invisible or bidirectional characters (U+202E…) are made visible, and Claude's own
  description is labelled as such ("Selon Claude : …").

### Performance

Measured on a work machine, over 20 s:

| Situation | CPU | Memory |
|---|---|---|
| One session working (Bip animated) | ~4.7 % of one core | ~68 MB |
| No animated Bip | ~0.7 % of one core | ~69 MB |
| `NotchHook.exe`, per tool call | ~70 ms, in the background | — |

Software rendering, looping animations at 15–30 fps, glow only while someone is waiting, window
sized to the notch. Measurement details are in [`docs/CONCEPTION.md`](docs/CONCEPTION.md) (in French).

### Layout

```
src/
  App.cs          entry point, install / uninstall, Windows calls
  NotchWindow.cs  the notch: shapes, transitions, cards, empty notch
  Bip.cs          the mascot (C# port of the kit's WPF control)
  Model.cs        sessions, daily log, pending requests, named pipe
  NotchHook.cs    the hook called by Claude Code
  Json.cs         JSON reading / writing
  Version.cs      version number (checked by the release)
design/bip-mascotte/   mascot kit (SVG, PNG, tokens.json, animated preview)
docs/                  design journal and screenshots
.github/workflows/     automatic release on every vX.Y.Z tag
build.cmd · installer.cmd · desinstaller.cmd · CHANGELOG.md
```

**Troubleshooting**: `ClaudeNotch.exe --dump-plan` prints the sessions read and what the notch
would show; `notch.log` collects errors; `journal\` keeps 7 days of summaries.

### Credits

- **Inspired by [coucou](https://github.com/Louis-CFM/coucou)** by Louis-CFM, the companion notch
  for coding agents on macOS (MIT licence). Bip borrows the idea (a black block that morphs,
  answering from the notch); the code, the mascot and the design are this project's own.
- **Mascot and design** made with Claude Design; app built with Claude Code.
- Independent project, not affiliated with Anthropic. "Claude" and "Claude Code" are trademarks
  of Anthropic.

### Licence

[MIT](LICENSE) © 2026 CR0ZER
