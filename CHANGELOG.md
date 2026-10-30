# Changelog

Les versions suivent [SemVer](https://semver.org/lang/fr/). Chaque section sert de notes à la release GitHub correspondante.

## [0.4.0] - 2026-10-30

Plus sûr, plus lisible, et Git, la to-do et les mises à jour dans l'encoche. · *Safer, easier to read, with Git, a to-do list and update checks in the notch.*

### Sécurité

- **Règles suggérées en liste blanche** : seules les commandes de test, de compilation, de vérification et Git en lecture (`npm test`, `pytest`, `cargo build`, `tsc`, `git status`…), et les scripts nommés (`npm run build`), peuvent devenir une règle. Jamais une commande composée (`&&`, `|`, `;`, redirection…). Une règle créée avant la 0.4 qui sort de la liste est signalée « plus suggérée par Bip » dans les réglages.
- **Secrets mieux masqués** : variables `*_TOKEN`, `*_KEY`, `*_PASSWORD`, `*_SECRET`…, `mysql -p`, `sshpass -p`, `ConvertTo-SecureString`, clés privées. Les outils MCP sont résumés par leurs arguments, masqués puis coupés à 80 caractères. Le libellé d'un `bip run` est masqué lui aussi.
- **Carte de permission : plus rien de caché** : les longues suites d'espaces sont rendues visibles (« [90 espaces] »), et ce qui ne tient pas dans la carte est signalé en rouge au-dessus des boutons (« Non affiché : … »).
- **Publication vérifiable** : actions GitHub épinglées par empreinte, attestation de provenance sur chaque zip (`gh attestation verify Bip-v0.4.0.zip -R CR0ZER/bip`).

### Nouveautés

- **Git dans l'encoche** : sous chaque session, sa branche, ses fichiers non commités et ses commits à pousser (« non publiée » sans branche distante) ; le récap de fin de tour l'ajoute. Un clic déplie le détail (8 fichiers, 5 commits). Lecture seule, jamais en continu (fin de tour, ouverture de l'encoche), sans verrou ni commande configurée par le dépôt (fsmonitor, hooks, signatures, sous-modules).
- **Micro to-do list** : dans l'encoche vide (3 tâches, puis « +N »), ou sous les sessions avec la case en haut de l'encoche dépliée. « + Ajouter à la todo liste », cocher pour effacer (3 s pour annuler). Depuis un terminal : `bip todo "texte"`. Les tâches sont dans `todo.txt`.
- **Vérification des mises à jour** : dans les réglages, à côté du numéro de version (« à jour » ou « v0.4.1 disponible ↗ »). Option « chaque jour », désactivée par défaut ; un point vert sur le ☰ signale une nouvelle version. Bip ne télécharge ni n'installe rien.
- **« Ne pas déranger » : le Bip endormi** (bleu, antenne en croissant de lune, deux « z ») remplace la lune. Repliée, l'encoche ne montre que lui ; dépliée, un bandeau en tête dit jusqu'à quand, avec **Reprendre**, et les sessions restent visibles, immobiles. Sans session, le grand Bip de l'encoche vide s'endort.
- **Journal d'audit plus léger** (−47 % sur un vrai journal) : les lectures (`Read`, `Grep`…) tiennent sur une ligne, les outils internes de Claude ne sont plus notés, objets coupés à 500 caractères. Le rapport regroupe les actions identiques qui se suivent (« ×3 »).

### Confort

- Textes secondaires plus lisibles : gris #BDBDBD au lieu de #A3A3A3.
- « Au repos » devient « inactif » : une session ouverte qui ne fait rien n'est pas un Bip qui dort.

### Corrections

- Le rapport d'audit ne se casse plus sur un chemin contenant un caractère invalide ; une session au titre illisible ne prive plus les autres de leur titre.

### Performances

Mesurées contre la 0.3.0, mêmes sessions, lancements alternés (Acer Nitro ANV15-52, i7-13620H, 32 Go, Windows 11) :

- Encoche : ~2,3 % d'un cœur avec une session au travail, comme en 0.3.0 ; ~66 Mo. Un Bip caché n'anime plus rien, et ses éléments transparents ne sont plus redessinés.
- Hook : 122 ms par appel (127 ms en 0.3.0).
- Lecture Git : ~65 ms, en arrière-plan, une fois en fin de tour ou à l'ouverture ; aucune quand l'encoche reste fermée.
- Relecture du journal d'audit après un ajout (10 000 évènements) : 0,4 ms (4,6 ms en 0.3.0).
- Rapport d'audit (1 200 évènements) : ~1 s, inchangé.

### English summary

Safer: suggested rules limited to a whitelist of test, build, check and read-only Git commands, stronger secret masking (MCP tools included), nothing hidden on the permission card any more, pinned GitHub actions and a provenance attestation on every zip. New: Git status under each session, a micro to-do list (`bip todo`), an opt-in update check, and sleeping Bip for do-not-disturb. The audit log is 47 % lighter and the report groups repeated actions. Same footprint as 0.3.0.

## [0.3.0] - 2026-10-14

Bip montre ce qui se passe dans une session : ses sous-agents, son nom, ce qu'elle a fait. · *Bip now shows what happens inside a session: its sub-agents, its name, what it did.*

### Nouveautés

- **Sous-agents visibles** dans l'encoche dépliée : sous chaque session, ses sous-agents en retrait, avec leur description et leur durée (5 au plus, puis « +N »). Un sous-agent fini passe au vert une minute, puis disparaît, y compris en arrière-plan. L'encoche repliée garde un seul Bip par session.
- **Titre du chat Claude** : chaque session s'affiche « titre · dossier/ » (« Migrations staging · data-pipeline/ ») ; l'entrouverture donne le titre seul. Plusieurs sessions dans un même dossier se distinguent enfin.
- **Récap de fin de tour** en vert sous une session prête : « 3 fichiers modifiés · 2 commandes · 1 échec ».
- **« Ne pas déranger » visible encoche dépliée**, en haut à gauche : « ☾ jusqu'à 17:18 » ou « ☾ appel en cours (Teams) » ; un clic le lève.
- **Rapport d'audit refait** : regroupé par session puis par agent (chaque sous-agent avec ses propres actions), synthèse en tête, filtres par session et par agent, nouveau style clair et sombre, commandes longues repliées.
- **Journal d'audit** : chaque action d'un sous-agent porte son identifiant.

### Corrections

- L'entrouverture s'élargit selon le nom de la session au lieu de le couper.
- Après un dépliage ou une entrouverture, les Bip cachés restaient animés : l'encoche consommait 2 à 3 fois plus (≈ 6 % d'un cœur au lieu de ≈ 2,3 %). Le panneau et l'entrouverture sont maintenant vidés une fois refermés.
- Les tests n'ajoutent plus d'entrées au registre (empreintes de fin de chaîne de leurs journaux temporaires).

### Performances

- Encoche : ~2,3 % d'un cœur avec une session au travail, y compris après des dépliages ; ~76 Mo.
- Hook : ~115 ms par appel, inchangé par rapport à la 0.2 malgré le suivi des sous-agents.

### Installation

- Deux nouveaux hooks (`SubagentStart`, `SubagentStop`) : relancer `installer.cmd` après la mise à jour.

### English summary

Sub-agents shown under each session in the open notch, sessions named after their Claude chat title, an end-of-turn recap, do-not-disturb shown in the open notch, and a redesigned audit report grouped by session and agent. Re-run `installer.cmd` to add the two new hooks.

## [0.2.0] - 2026-10-12

Bip suit aussi tes commandes, se fait discret pendant tes appels et garde la trace de ce que font les agents. · *Bip now follows your commands, keeps quiet during calls and records what agents do.*

### Nouveautés

- **`bip run <commande>`** : la commande tourne dans ton terminal et apparaît dans l'encoche ; Bip s'entrouvre à la fin avec la durée, ou le code de retour en cas d'échec. Le code de la commande est renvoyé.
- **Sourdine automatique pendant les appels** (Teams, Zoom, Meet, Slack, Discord, Webex…), détectée par l'utilisation du micro : bref signal au début, résumé de ce qui s'est passé à la fin. Désactivable dans les réglages.
- **Journal d'audit** de toutes les actions des agents : outil, objet, qui a autorisé, résultat. Secrets masqués avant écriture, chaîne d'empreintes SHA-256, 90 jours de conservation, vérification par `ClaudeNotch.exe --verify-audit`.
- **Rapport d'audit** depuis les réglages : page autonome avec filtres et export CSV.
- **Règles suggérées** après 5 autorisations de la même commande dans un projet, avec la règle exacte affichée ; portée « ce projet » ou « partout », retrait d'un clic depuis les réglages.
- **Réglages intégrés à l'encoche** : bouton ☰ en haut à droite (ou clic droit), à la place du menu contextuel qui passait sous l'encoche.
- **Croix** pour retirer une commande `bip run` de l'encoche.

### Sécurité

- Aucune suggestion de règle pour une commande composée, risquée ou trop large (interpréteurs, réseau, suppression, `git push`…).
- Fichier de réglages refusé s'il est un lien ; sauvegarde datée avant chaque modification.
- Export CSV protégé contre les formules.

### Performances

- Encoche : ~2,7 % d'un cœur avec une session au travail (contre ~4,7 % en 0.1.0) : Bips de l'encoche repliée à 8 images/s, vérification directe du processus Claude, souris suivie moins souvent quand l'encoche est repliée.
- Hook : −33 % par appel malgré le journal d'audit (lecteur JSON maison, empreinte par le fournisseur Windows).
- Journal d'audit relu de façon incrémentale : ~15 ms au lieu de ~250 ms sur une grosse journée.

### English summary

`bip run` to follow long commands in the notch, automatic mute during calls with an end-of-call recap, a tamper-evident audit log of every agent action with secret masking and an HTML/CSV report, suggested Claude Code rules after repeated approvals, and settings built into the notch. Lighter too: ~2.7 % of one core with a session working, hook 33 % faster.

## [0.1.0] - 2026-10-12

Première version publique. · *First public release.*

### Nouveautés

- **Encoche en haut au centre de l'écran** : trait quand rien ne tourne, languette avec un Bip par session (5 au plus, puis « +N »), forme entrouverte 4 s à chaque nouvelle demande, panneau déplié au survol. Repliée ou entrouverte, elle laisse passer les clics.
- **Bip**, la mascotte, en cinq états : en attente, en cours, prêt, erreur, au repos. Chacun a ses propres mouvements et se lit sans la couleur.
- **Permissions depuis l'encoche** : Autoriser, Refuser, ou « Toujours » avec sa portée affichée, limitée à la session. Jamais de « Toujours » pour les commandes shell.
- **Questions (AskUserQuestion)** depuis l'encoche : choix unique, multiple, réponse libre.
- **Encoche vide** : Bip salue et réagit au clic, montre les dernières sessions terminées et le bilan du jour ; un clic ailleurs ouvre Claude.
- **Ne pas déranger** pendant 1 h.
- **Installation en une commande** (`installer.cmd`) et désinstallation propre (`desinstaller.cmd`).

### Sécurité

- Échec sûr : toute panne rend la main à Claude, jamais d'autorisation implicite.
- Pipe nommé réservé au compte, serveur vérifié par le hook.
- Boutons armés après 600 ms, commande toujours affichée en entier, caractères invisibles rendus visibles.

### Performances

- ~4,7 % d'un cœur avec une session au travail, ~0,7 % au repos, ~68 Mo de mémoire.

### English summary

Windows notch for Claude Code with Bip, a five-state mascot: live session tracking, permissions and questions answered from the notch, an empty-notch view with recent sessions and today's summary, do-not-disturb, one-command install. Fail-safe by design and light on resources.
