# ClaudeNotch — cadrage

maj : 2026-10-12 · statut : v2 (encoche en haut, mascotte Bip) livrée et installée

Évolution de ClaudeVoyant (`Documents\ClaudeVoyant`) en « encoche » posée sur la
barre des tâches Windows, inspirée de coucou (github.com/Louis-CFM/coucou, MIT).
Projet **séparé** : ClaudeVoyant n'est pas modifié.

## Décisions actées

### Organisation
- Dossier propre `Documents\ClaudeNotch` : hook, dossier d'état et logos `clawd`
  (copiés) qui lui sont propres. Supprimer le dossier doit suffire à tout retirer,
  une fois les hooks désinstallés.
- Scripts **installer / désinstaller** : ajoutent ou retirent uniquement les
  entrées ClaudeNotch de `~/.claude/settings.json`, repérées par un marqueur,
  avec une sauvegarde datée.
- Livraison en **un bloc** (« semi one-shot ») : une appli testable et
  fonctionnelle, pas de découpage en étapes. Claude code le cœur lui-même ; les
  sous-agents servent à la relecture, à l'attaque sécurité et aux scénarios de
  test.
- Techno : C# + WPF écrit sans XAML, compilé par le `csc` du .NET Framework
  livré avec Windows (aucun SDK), comme ClaudeVoyant.
- Pendant les tests, ClaudeVoyant et ClaudeNotch tournent **en parallèle**
  (deux jeux de hooks), pour comparer.

### Position
- Détection automatique de l'alignement de la barre des tâches (registre
  Windows 11) :
  - applis Windows **au centre** → encoche **en bas à gauche** de l'écran
    principal ;
  - applis Windows **à gauche** → encoche **au centre** de la barre des tâches.
- Un paramètre permet de forcer la position.
- L'encoche est en superposition sur la barre des tâches. Si la barre repasse
  par-dessus de façon trop gênante, repli envisagé : **en haut au centre**,
  collée au bord, comme coucou.
- Masquée quand une appli est en plein écran. Écran principal uniquement.

### Affichage
- Même esprit que ClaudeVoyant : un petit nuage `clawd` par session, avec sa
  pastille de couleur selon l'état.
- Repliée : l'encoche dépasse à peine et montre **jusqu'à 5 sessions**, puis
  « +1 », « +2 »…
- **Ordre stable = ordre de lancement des sessions.** Une session en attente
  clignote à sa place, sans bouger.
- Au survol : l'encoche se déplie pour donner le détail. Une session en attente
  passe en tête, en grand, avec de quoi répondre.
- Session en attente : le nuage clignote en rouge, avec la même escalade que
  ClaudeVoyant (respiration qui accélère, puis clignotement de la fenêtre
  Claude). **Jamais** de mise au premier plan forcée de Claude.
- Clic sur une session : met Claude au premier plan (comme aujourd'hui).
- Logo de base pour l'instant. Les animations viendront plus tard.

### Réponses depuis l'encoche
- **Permissions d'outils** : oui. Hook `PermissionRequest` **synchrone**, en
  entrée séparée des hooks d'affichage (qui restent `async`).
  - Bash : **Autoriser / Refuser** seulement, pas de « Toujours autoriser ».
  - Autres outils : Autoriser / Refuser / Toujours autoriser (d'après les
    `permission_suggestions` de Claude Code).
- **AskUserQuestion** : oui, par un hook `PreToolUse` sur `AskUserQuestion` qui
  renvoie `updatedInput.answers`. Mécanisme hors de la doc officielle (mais
  utilisé par coucou) : retour propre vers Claude en cas d'échec.
  - Gérés : choix unique, choix multiple, plusieurs questions par appel,
    réponse libre « Autre » (seul cas où l'encoche prend le clavier).
  - Non gérés : les aperçus visuels des options.
- **Hors périmètre** : validation de plan, textes à relire. L'encoche sert au
  suivi des sessions.
- Bouton **« Répondre dans Claude »** sur chaque carte : libère le hook tout de
  suite, et Claude affiche sa fenêtre normale.
- Pas de délai fixe imposé côté encoche. Le hook garde forcément un délai
  technique maximal (à fixer au test, le plus long possible) ; s'il expire, on
  revient à la fenêtre normale de Claude, jamais à une autorisation implicite.

### Sécurité
- Pipe nommé réservé au compte de l'utilisateur.
- Commande complète toujours affichée.
- Rien ne se valide au survol : seulement par un clic explicite sur un bouton.

## Premier test, qui conditionne le projet
Vérifier dans l'**app desktop** qu'une décision renvoyée par un hook
`PermissionRequest` est bien prise en compte, et ce que Claude affiche pendant
que le hook attend. Si ça ne marche pas : le dire tout de suite à Florian, et
l'on arrête.

## Questions encore ouvertes
Les choix par défaut s'appliquent sauf réponse contraire de Florian.

1. **Sessions affichées** : mêmes règles que ClaudeVoyant (en cours, en attente,
   en erreur ; « prête » pendant 10 min ; disparaît si Claude est fermé).
   *Défaut : oui.*
2. **Ordre de lancement** : une session qui disparaît puis revient garde sa
   place d'origine ou passe en dernier ? *Défaut : garde sa place tant que
   Claude tourne pour cette session.*
3. **Nom affiché** : le nom du dossier de travail (les hooks ne transmettent pas
   le titre de la session). *Défaut : nom du dossier.*
4. **Carte de permission** : commande complète pour Bash ; pour une
   modification de fichier, chemin seul ou extrait des changements ?
   *Défaut : chemin + quelques lignes de changement.*
5. **« Toujours autoriser » hors Bash** : pour la session seulement, ou
   enregistré dans les réglages ? *Défaut : session seulement.*
6. **Réglages** : pas d'icône dans la zone de notification, clic droit sur
   l'encoche (position auto / gauche / centre / haut, clignotement de la
   fenêtre, Quitter). *Défaut : oui.*
7. **Démarrage avec Windows** via le script d'installation, retiré à la
   désinstallation. *Défaut : oui.*
8. **Encoche dépliée** : se replie quand la souris sort ; une demande non
   répondue reste en attente. *Défaut : oui.*
9. **Attente / erreur** : attente = rouge qui clignote, erreur = rouge fixe avec
   un « ! ». *Défaut : oui.*
10. **Son** à l'arrivée d'une demande. *Défaut : non.*

## Livraison du 2026-10-09

### Réponses aux questions ouvertes
Florian n'a pas répondu : les choix par défaut s'appliquent, avec deux
ajustements issus des relectures.
- Une session « en cours » dont Claude tourne toujours expire au bout de 1 h
  sans nouvel outil. Une interruption (Échap) n'émet pas de `Stop`, et la
  session resterait sinon jaune indéfiniment. Sans PID, ClaudeVoyant expirait
  au bout de 20 min.
- « Toujours » : un bouton par suggestion de Claude, avec sa portée en clair
  (« toutes les modifications de fichiers », « Edit(src/**) », « accès à un
  dossier »), et non un bouton unique qui appliquerait tout d'un coup.

### Vérifié
- Affichage : 5 nuages dans l'ordre de lancement, panneau au survol, demandes
  en tête, position automatique en bas à gauche (barre des tâches centrée).
- Permissions : Autoriser, Refuser, Toujours (par suggestion), Répondre dans
  Claude ; pas de Toujours pour Bash et PowerShell. Sorties JSON conformes.
- AskUserQuestion : choix unique, multiple, « Autre… » au clavier, accents.
- **Test réel dans l'app desktop** : une vraie AskUserQuestion de Claude a été
  interceptée, affichée et répondue depuis l'encoche ; Claude a reçu la
  réponse sans afficher sa propre question.
- **Test réel de permission dans l'app desktop** (2026-10-09) : session passée
  en mode normal, `mkdir` demandé. La carte est apparue dans l'encoche, Florian
  a cliqué Autoriser, et le journal de session enregistre une décision
  « allow » venant du hook, sans fenêtre de permission de Claude.
- Repli sûr : encoche fermée → le hook rend la main en moins d'une seconde ;
  hook tué → la carte disparaît ; validation de plan et demandes illisibles
  rendues aussitôt à Claude.
- Installeur : n'ajoute que ses entrées ; désinstallation → `settings.json`
  identique octet pour octet à l'original.
- Relectures justesse et sécurité (sous-agents) : corrections appliquées.
  Parmi elles : caractères invisibles neutralisés, délai d'armement quand la
  liste bouge, commande jamais coupée en silence, retour à « en cours » après
  une réponse.

### Reste à vérifier par Florian
- Le comportement de la barre des tâches quand on clique dessus : l'encoche se
  replace au premier plan, avec un léger scintillement possible. Si c'est
  gênant, clic droit → Position → En haut au centre.

### Limites connues
- En plein écran, l'encoche se masque et une demande reste en attente (au plus
  1 h, puis Claude reprend la main) jusqu'au retour sur le bureau.
- Les aperçus visuels des options AskUserQuestion ne sont pas affichés.
- Le clic sur une session met l'app Claude au premier plan, pas l'onglet précis.

## v2 du 2026-10-12 : encoche en haut et mascotte Bip

Décisions de Florian (remplacent celles de « Position » et « Affichage » plus haut) :
- **Toujours en haut au centre** de l'écran principal ; les positions sur la barre
  des tâches sont supprimées.
- **Mascotte Bip** (kit `design/bip-mascotte`, conçu par Claude Design depuis le
  brief « ClaudeNotch — brief design ») : 5 états, dont « au repos » pour une
  session ouverte qui n'a encore rien fait (hook `SessionStart` → `idle`).
- **Entrouverte** : à chaque nouvelle demande, l'encoche s'agrandit 4 s pour dire
  quelle session attend, puis se replie.
- Ouverture **au survol** (200 ms). **Aucun son**, quel qu'il soit.
- Animations : celles du kit (`tokens.json`), à l'identique.

Mise en œuvre :
- Le `BipControl` du kit (XAML, C# récent) est porté en code C# 5 (`src/Bip.cs`) :
  le projet se compile toujours sans SDK. Géométrie, couleurs, mouvements et
  durées identiques.
- La fenêtre est fixe et transparente ; seul le bloc noir change de taille.
  Repliée et entrouverte, elle laisse passer les clics et la souris est suivie
  toutes les 30 ms pour détecter le survol.

Vérifié par captures : trait et forme repliée avec congés, Bip par état, forme
entrouverte puis repli, panneau déplié avec carte de permission et lignes de
session, éclat d'attente. Les animations elles-mêmes sont à juger à l'œil.

## Performances (2026-10-12)

Mesuré sur 20 s par configuration, avec les mêmes sessions. ClaudeVoyant est
désactivé : icône arrêtée, raccourci de démarrage rangé dans son dossier, hooks
retirés de `settings.json` avec une sauvegarde à côté.

| | Processeur (1 cœur) | Mémoire privée | Threads |
|---|---|---|---|
| v2 avant optimisation | ~25 % | 279 Mo | 88 |
| v2 optimisée, une session au travail | 4,7 % | 68 Mo | 12 |
| v2 optimisée, aucun Bip animé | ~0,7 % | 69 Mo | 12 |
| ClaudeVoyant, pour mémoire | 0,26 % | 32 Mo | 4 |

Causes et corrections :
- **L'éclat d'attente** balayait le bord en continu, même invisible, ce qui
  forçait un rendu à 60 images/s : il ne tourne plus que si une session attend.
  C'était le principal poste.
- **Le rendu par la carte graphique** chargeait le pilote dans le processus
  (~200 Mo, ~76 threads). L'encoche est maintenant en rendu logiciel, sans
  différence visible.
- **Les boucles d'animation** tournent à 15 images/s pour Bip et à 30 pour
  l'éclat. Les ouvertures et fermetures restent à 60.
- **La fenêtre suit la hauteur de l'encoche** au lieu de couvrir 90 % de
  l'écran. Elle grandit juste avant une transformation et se resserre après.
- Le suivi de la souris passe de 30 à 50 ms, la lecture des états de 300 à
  500 ms.

Écarté après mesure : rétrécir aussi la largeur de la fenêtre (aucun gain).
`NotchHook.exe` coûte environ 70 ms par lancement, en arrière-plan. Chaque appel
d'outil lance désormais 2 processus, contre 4 avec ClaudeVoyant.
