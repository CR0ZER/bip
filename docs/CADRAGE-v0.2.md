# Bip v0.2 — cadrage

maj : 2026-10-12 · statut : **réalisé** (v0.2.0), toutes les propositions validées

Deux axes pour la v0.2, issus de la discussion du 2026-10-12 :

1. **Attention** : `bip run` pour suivre n'importe quelle commande longue, et la mise en
   sourdine automatique pendant les appels (Teams, Zoom, Meet…).
2. **Confiance et traçabilité** : un journal d'audit de ce que les agents font et de ce qui
   a été autorisé, et des règles suggérées à partir de tes réponses.

Écarté : le relais vers le téléphone.

La v0.2 sort en release quand tout est en place et fonctionnel.

---

## Axe 1a — `bip run` : suivre une commande longue

**Le besoin** : un build, une suite de tests, un `docker compose up --build`, un gros
téléchargement… On les lance, on part sur autre chose, et on ne sait pas quand c'est fini,
ni si ça a échoué. Valable pour n'importe quel développeur, même sans Claude.

**Usage**

```bat
bip run npm run build
bip run docker compose up --build
bip run python train.py --epochs 20
```

- La commande s'exécute **normalement dans le terminal** : sortie, couleurs et saisie
  intactes, même code de retour que la commande elle-même.
- Pendant l'exécution, un Bip **orange** apparaît dans l'encoche, comme une session Claude au
  travail.
- À la fin :
  - **succès** → Bip **vert**, « `npm run build` a fini · 2 min 13 » ;
  - **échec** → Bip **gris avec « ! »**, « `npm run build` a échoué · code 1 · 48 s ».
- Si la commande a duré plus de **30 s**, l'encoche **s'entrouvre 4 s** pour l'annoncer. En
  dessous de 30 s, rien ne s'ouvre : on était sans doute encore devant.
- Dans le panneau déplié, la ligne affiche la commande (tronquée) et son dossier, avec une
  petite étiquette « commande » pour la distinguer des sessions Claude.
- Le Bip s'efface au bout de 4 min, comme une session « prête ».

**Mise en œuvre**

- Un troisième petit exécutable, `bip.exe`, **en mode console**, pour qu'il partage le
  terminal de la commande. Il lance la commande, attend sa fin, écrit l'état et renvoie le
  même code de retour.
- Mêmes fichiers d'état que les sessions (`state\run-<pid>.state`). La preuve de vie devient
  le PID de `bip.exe` : un Ctrl+C ou une fenêtre fermée fait disparaître le Bip proprement.
- `installer.cmd` ajoute le dossier de Bip au `PATH` de l'utilisateur, et `desinstaller.cmd`
  le retire. `bip` marche alors dans tout nouveau terminal.
- Le bilan du jour compte aussi les commandes : « 4 commandes · 1 échec ».

**Limite assumée** : sans capturer la sortie, Bip ne peut pas afficher la ligne d'erreur. La
capturer casserait les couleurs et la saisie interactive. → *Question 1.*

---

## Axe 1b — Sourdine automatique pendant les appels

**Le besoin** : en réunion, une encoche qui s'entrouvre, un éclat rouge ou une fenêtre Claude
qui clignote, c'est gênant. Mais il ne faut pas pour autant perdre ce qui s'est passé pendant
l'appel.

**Détection (vérifiée sur le poste le 2026-10-12)**

Windows enregistre quelle application utilise le micro, et depuis quand
(`CapabilityAccessManager\ConsentStore\microphone`, un `LastUsedTimeStop` à 0 signifie
« micro ouvert en ce moment »). On y trouve Teams (`MSTeams`), 3CX, Edge (pour Meet)… **et
l'app Claude elle-même** (mode vocal). Il faut donc une **liste d'applications d'appel**, sinon
dicter à Claude mettrait Bip en sourdine.

- Liste par défaut : Teams (nouveau et classique), Zoom, Webex, Slack, Discord, 3CX, Skype,
  et les navigateurs (Edge, Chrome, Firefox) pour Meet et les appels web.
- Lecture du registre toutes les 2 s : coût négligeable, aucune connexion à un calendrier ni
  à un compte.

**Comportement**

1. **Début d'appel** : « ne pas déranger » s'active tout seul. L'encoche fait une **micro-ouverture
   de 2 s** : « ☾ Ne pas déranger · appel en cours », puis se replie.
2. **Pendant l'appel** : pas d'entrouverture, pas d'éclat, pas de clignotement de Claude. Les
   Bip continuent d'afficher leur état, et les demandes restent accessibles au survol.
   Un petit **☾** reste visible à droite des Bip dans l'encoche repliée, pour qu'on sache que
   la sourdine est active.
3. **Fin d'appel** (micro fermé depuis 1 min, pour ne pas réagir à un micro coupé un instant) :
   la sourdine se lève. L'encoche s'entrouvre avec le **résumé de ce qui s'est passé pendant
   l'appel**, par exemple « Pendant ton appel : 2 sessions ont fini · data-pipeline t'attend
   depuis 12 min ». Rien ne s'affiche si rien ne s'est passé.

**Priorités**

- Une sourdine mise **à la main** n'est jamais levée par la fin d'un appel.
- Une sourdine automatique **levée à la main** pendant un appel reste levée jusqu'à la fin de
  cet appel.
- Le clic droit permet de désactiver la sourdine automatique.

---

## Axe 3a — Journal d'audit

**Le besoin** : quand on laisse des agents toucher du code, et surtout du code client, on doit
pouvoir répondre à « qu'est-ce qui a été exécuté, où, quand, et qui l'a autorisé ? ». Aucun
outil du genre ne le fait aujourd'hui, coucou compris.

**Ce qui est enregistré**, pour chaque action d'un agent :

| Champ | Exemple |
|---|---|
| Date et heure | 2026-10-12 14:03:27 |
| Poste et compte Windows | PC-FLORIAN · florian |
| Session et projet | portail_client (`C:\dev\portail_client`) |
| Outil et objet | Bash · `npm run migrate -- --env=staging` / Edit · `src/app.py` |
| Décision | autorisé / autorisé toujours (règle ajoutée) / refusé / exécuté sans demande |
| Par qui ou par quoi | encoche Bip / fenêtre Claude / règle existante / mode auto |
| Résultat | réussi / échoué |

**Ce qui le rend fiable**

- Écrit **par le hook lui-même**, pas par l'encoche : l'audit fonctionne même encoche fermée.
- Un fichier par jour en **JSON Lines**, conservé 90 jours (réglable).
- **Chaînage par empreinte** : chaque ligne contient l'empreinte SHA-256 de la précédente.
  Une ligne modifiée ou supprimée après coup se voit (`ClaudeNotch.exe --verify-audit`).
- **Masquage des secrets** avant écriture : jetons `Bearer`, clés d'API reconnaissables
  (`sk-…`, `ghp_…`, `AKIA…`), `password=…`, en-têtes d'authentification. Le journal reste
  local, il n'est envoyé nulle part.

**Le consulter**

- Clic droit → « Journal d'audit » : un **rapport HTML** généré à la demande (filtrable par
  jour, projet, décision) qui s'ouvre dans le navigateur.
- Export **CSV** pour un tableur ou un audit client.
- Dans l'encoche vide, le bilan du jour gagne une mention : « 37 actions · 5 autorisées par
  toi · 0 refus ».

---

## Axe 3b — Règles suggérées

**Le besoin** : autoriser dix fois `npm test` dans le même projet, c'est du bruit. Mais créer
des règles permanentes à l'aveugle, c'est risqué. Bip propose et tu décides.

**Comportement**

- Après la **5e autorisation** de la même commande (même début : `npm test`, `pytest`,
  `git status`…) dans le même projet, une ligne apparaît sous la carte suivante :
  « Tu as autorisé `npm test` 5 fois dans portail_client. En faire une règle ? »
  avec trois boutons : **[Ce projet] [Partout] [Non, ne plus proposer]**.
- **Jamais de suggestion pour une commande risquée** : suppression récursive, `git push`,
  `git reset --hard`, réseau vers un script exécuté (`curl … | sh`), `DROP TABLE`, formatage…
  Une liste de motifs risqués, lisible et modifiable, fait foi.
- La règle est écrite dans les réglages Claude Code du projet
  (`.claude/settings.local.json`, non versionné) ou dans tes réglages utilisateur. Chaque
  règle ajoutée par Bip est **inscrite dans le journal d'audit**.
- Clic droit → « Règles créées par Bip » : la liste, avec la date et la possibilité d'en
  retirer une.

---

## Hors v0.2, à garder en tête

- **Politique d'équipe** : un fichier de règles (interdictions, double confirmation) que Bip
  ferait respecter avant chaque action. Puissant pour une entreprise, mais cela demande un
  hook bloquant sur chaque appel d'outil (environ 70 ms de plus à chaque fois). À rediscuter
  après l'audit.
- **Relais vers le téléphone** : écarté.

---

## Questions à trancher

1. **`bip run`** : garder le terminal intact (on n'a que le code de retour), ou capturer la
   sortie pour montrer la dernière ligne d'erreur (couleurs et saisie interactive perdues) ?
   *Proposition : terminal intact.*
2. **Seuil d'annonce de fin de commande** : 30 s ? *Proposition : 30 s.*
3. **Appels** : la liste d'applications ci-dessus te va ? Faut-il ajouter la **caméra**
   (même mécanisme) comme signal d'appel ? *Proposition : micro seulement, liste ci-dessus.*
4. **Indicateur ☾** dans l'encoche repliée pendant la sourdine : OK ?
5. **Périmètre de l'audit** :
   - (a) **seulement les demandes de permission** et leur issue ;
   - (b) **toutes les actions** des agents, y compris celles autorisées d'office (règles,
     mode auto). C'est la trace complète, avec un fichier plus gros.
   *Proposition : (b), c'est ce qui donne sa valeur à l'audit.*
6. **Durée de conservation** : 90 jours ? *Proposition : 90 jours.*
7. **Règles suggérées** : 5 autorisations avant de proposer, et règle écrite côté projet par
   défaut ? *Proposition : oui aux deux.*
8. **Langue** : rapport d'audit et nouvelles phrases en français seulement (comme
   l'interface), ou bilingues ? *Proposition : français.*

## Estimation (au 2026-10-12)

| Lot | Charge estimée |
|---|---|
| Axe 1a — `bip run` | ½ session |
| Axe 1b — sourdine pendant les appels + résumé | ½ à 1 session |
| Axe 3a — journal d'audit, chaînage, rapport HTML / CSV | 1 session |
| Axe 3b — règles suggérées | ½ à 1 session |
| Relectures (justesse, sécurité), tests, captures, README, release v0.2.0 | ½ session |

Une « session » correspond à un bloc de travail comparable à la livraison de la v0.1.
