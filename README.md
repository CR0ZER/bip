# ClaudeNotch

Encoche posée sur la barre des tâches Windows : un nuage par session Claude Code,
et la possibilité de répondre aux demandes de permission et aux questions
(AskUserQuestion) sans revenir dans Claude. Cadrage et décisions : `CADRAGE.md`.

## Installer / désinstaller

- `installer.cmd` : compile si besoin, ajoute les hooks ClaudeNotch dans
  `~/.claude/settings.json` (sauvegarde datée `settings.json.bak-claudenotch-*`
  à côté), ajoute ClaudeNotch au démarrage de Windows et lance l'encoche.
  Relançable sans risque : les anciennes entrées ClaudeNotch sont remplacées.
- `desinstaller.cmd` : retire uniquement les entrées ClaudeNotch (les autres
  hooks, dont ceux de ClaudeVoyant, ne sont pas touchés), le démarrage avec
  Windows, et ferme l'encoche. Le dossier peut ensuite être supprimé.

Les nouvelles sessions Claude prennent les hooks en compte ; une session déjà
ouverte peut demander un redémarrage.

## Utilisation

- **Repliée** : jusqu'à 5 nuages dans l'ordre de lancement des sessions, puis
  « +N ». Pastille verte = prêt, jaune = en cours, rouge qui respire = en
  attente, rouge avec « ! » = erreur.
- **Survol** : le panneau se déplie. Les demandes en attente sont en tête, en
  grand, avec leurs boutons. Clic sur une session ou sur l'encoche : met Claude
  au premier plan.
- **Permissions** : Autoriser / Refuser ; « Toujours (cette session) » pour les
  outils autres que les commandes shell (Bash, PowerShell).
- **Questions** : choix unique, choix multiple, « Autre… » pour taper sa propre
  réponse (seul moment où l'encoche prend le clavier).
- **Répondre dans Claude** : rend la main ; Claude affiche sa demande habituelle.
- **Clic droit** : position (automatique, en bas à gauche, au centre de la barre
  des tâches, en haut au centre), clignotement de la fenêtre Claude après
  5 min d'attente, dossier d'état, purge, Quitter.

Si l'encoche est fermée ou plante, les hooks rendent la main tout de suite et
Claude pose ses questions comme d'habitude : jamais d'autorisation implicite.

## Compiler

`build.cmd` (csc du .NET Framework livré avec Windows, aucun SDK). Quitter
l'encoche avant : un exe qui tourne est verrouillé.

## Diagnostic

- `ClaudeNotch.exe --dump-plan` : sessions lues et ce que l'encoche afficherait.
- `notch.log` : erreurs de l'encoche.
- `state\` : un fichier par session (`état|dossier|pid`).
