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

L'encoche est un bloc noir collé en haut au centre de l'écran principal. Chaque
session Claude Code y est représentée par **Bip**, la mascotte (kit complet dans
`design/bip-mascotte`).

- **Formes** :
  - **trait** (120 × 4) quand aucune session n'est active ;
  - **repliée** (22 px de haut) : un Bip par session dans l'ordre de lancement,
    5 au plus puis « +N » ;
  - **entrouverte** (320 × 60) pendant 4 s à chaque nouvelle demande : « projet
    attend ta réponse » ;
  - **dépliée** au survol (après 200 ms) : demandes en tête, puis les sessions.
  Repliée ou entrouverte, l'encoche laisse passer les clics vers l'application
  en dessous.
- **États de Bip** : rouge, bras levé = en attente ; orange qui flotte = en cours ;
  vert qui saute = prêt ; gris avec « ! » = erreur ; gris endormi = au repos
  (session ouverte, rien demandé encore). Un éclat rouge balaie le bord bas
  de l'encoche tant qu'une session attend.
- **Permissions** : Autoriser / Refuser ; « Toujours : … (session) » pour les
  outils autres que les commandes shell (Bash, PowerShell).
- **Questions** : choix unique, choix multiple, « Autre… » pour taper sa propre
  réponse (seul moment où l'encoche prend le clavier).
- **Répondre dans Claude** : rend la main ; Claude affiche sa demande habituelle.
- **Encoche vide survolée** : Bip se réveille et salue ; un clic sur lui le fait
  réagir (saut, clignement, signe). Dessous : les 3 dernières sessions terminées
  et le bilan du jour (sessions, temps de travail, réponses données depuis
  l'encoche). Un clic ailleurs sur l'encoche vide ouvre Claude.
- **Ne pas déranger** (bouton de l'encoche vide ou clic droit) : pendant 1 h,
  ni entrouverture, ni éclat, ni clignotement de Claude ; les Bip restent visibles.
- **Clic droit** (encoche dépliée) : ne pas déranger, clignotement de la fenêtre
  Claude après 5 min d'attente, dossier d'état, purge, Quitter.
- Aucun son.

Si l'encoche est fermée ou plante, les hooks rendent la main tout de suite et
Claude pose ses questions comme d'habitude : jamais d'autorisation implicite.

## Compiler

`build.cmd` (csc du .NET Framework livré avec Windows, aucun SDK). Quitter
l'encoche avant : un exe qui tourne est verrouillé.

## Diagnostic

- `ClaudeNotch.exe --dump-plan` : sessions lues et ce que l'encoche afficherait.
- `notch.log` : erreurs de l'encoche.
- `state\` : un fichier par session (`état|dossier|pid`).
- `journal\` : un fichier par jour pour le bilan, gardé 7 jours.
