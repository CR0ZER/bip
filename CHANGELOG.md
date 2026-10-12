# Changelog

Les versions suivent [SemVer](https://semver.org/lang/fr/). Chaque section sert de notes à la release GitHub correspondante.

## [0.2.0] - 2026-10-12

Bip suit aussi tes commandes, se fait discret pendant tes appels et garde la trace de ce que font les agents. · *Bip now follows your commands, keeps quiet during calls and records what agents do.*

### Nouveautés

- **`bip run <commande>`** : la commande tourne dans ton terminal et apparaît dans l'encoche ; Bip s'entrouvre à la fin avec la durée, ou le code de retour en cas d'échec. Le code de la commande est renvoyé.
- **Sourdine automatique pendant les appels** (Teams, Zoom, Meet, Slack, Discord, Webex…), détectée par l'utilisation du micro : bref signal au début, résumé de ce qui s'est passé à la fin. Désactivable dans le menu.
- **Journal d'audit** de toutes les actions des agents : outil, objet, qui a autorisé, résultat. Secrets masqués avant écriture, chaîne d'empreintes SHA-256, 90 jours de conservation, vérification par `ClaudeNotch.exe --verify-audit`.
- **Rapport d'audit** depuis le menu : page autonome avec filtres et export CSV.
- **Règles suggérées** après 5 autorisations de la même commande dans un projet, avec la règle exacte affichée ; portée « ce projet » ou « partout », retrait d'un clic depuis le menu.

### Sécurité

- Aucune suggestion de règle pour une commande composée, risquée ou trop large (interpréteurs, réseau, suppression, `git push`…).
- Fichier de réglages refusé s'il est un lien ; sauvegarde datée avant chaque modification.
- Export CSV protégé contre les formules.

### English summary

`bip run` to follow long commands in the notch, automatic mute during calls with an end-of-call recap, a tamper-evident audit log of every agent action with secret masking and an HTML/CSV report, and suggested Claude Code rules after repeated approvals.

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
