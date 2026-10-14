<div align="center">

<img src="svg/bip-coucou.svg" width="96" alt="Bip fait coucou">

# Bip, la mascotte

</div>

Bip est **un petit robot par session Claude Code**, debout sur le bord de l'encoche. Sa couleur,
son visage et ses mouvements disent où en est la session ; chaque état se lit aussi sans la
couleur, même à 16 px.

Ce dossier rassemble tout le design de Bip et de l'encoche : la mascotte en vectoriel et en
images, ses valeurs de référence et un aperçu animé. `tokens.json` fait foi ; ce README
l'explique ; l'application l'applique dans [`src/Bip.cs`](../../src/Bip.cs) et
[`src/NotchWindow.cs`](../../src/NotchWindow.cs).

## Contenu

| Chemin | Rôle |
|---|---|
| `svg/bip-*.svg` | Les 5 états en vectoriel, fond transparent, grille 32 × 32. |
| `svg/bip-coucou.svg` | Bip qui fait coucou, animé (SVG natif, s'affiche aussi dans une page GitHub). |
| `png/{16,20,40,256}/bip-*.png` | Les 5 états rendus à chaque taille d'affichage, fond transparent. |
| `apercu-planche.png` | Les 5 états à 128, 40, 20 et 16 px sur fond noir. |
| `apercu-anime.html` | À ouvrir dans un navigateur pour voir les mouvements. |
| `tokens.json` | Toutes les valeurs : géométrie, couleurs, typographie, encoche, durées et courbes. |
| `wpf/` | Bip en contrôle WPF autonome (XAML + C#), pour le réutiliser dans une autre application. L'encoche utilise son portage en C# pur, sans XAML. |

## Les états

| | État | Quand | Couleur | Lecture sans couleur | Mouvement |
|:---:|---|---|---|---|---|
| <img src="png/40/bip-attente.png" width="32" alt=""> | **En attente** | Une permission ou une question attend ta réponse | `#FF5D52` | **Bras levé**, antenne gonflée, yeux ronds, bouche ouverte | Respiration et bras qui s'agite, de plus en plus vite ; tremblement de temps en temps |
| <img src="png/40/bip-en-cours.png" width="32" alt=""> | **En cours** | Claude réfléchit, lit, exécute | `#F5A524` | Yeux plissés en traits, antenne droite | Flottement doux |
| <img src="png/40/bip-pret.png" width="32" alt=""> | **Prêt** | Claude a fini et rend la main | `#3FD07F` | Yeux rieurs, sourire | Un saut à l'arrivée, puis immobile ; s'efface au bout de 4 min |
| <img src="png/40/bip-erreur.png" width="32" alt=""> | **Erreur** | Le tour s'est arrêté sur une erreur de l'API | `#8E96A3` + pastille `#FFC93D` | **Pastille « ! »**, corps tassé, petits yeux | Affaissement à l'entrée, puis figé |
| <img src="png/40/bip-inactif.png" width="32" alt=""> | **Au repos** | Session ouverte, rien demandé encore | `#4A4A4E` | Yeux fermés, **antenne pliée** | Aucun |

L'attente est le seul état rouge et le seul qui bouge sans arrêt : c'est ce qui le fait ressortir
en vision périphérique. L'erreur est volontairement grise pour ne pas se confondre avec elle.

**Bip neutre** (`#DAD6CF`, blanc cassé) : quand plus aucune session ne tourne, Bip se réveille
dans l'encoche vide, sourit et salue. Il flotte comme en cours, et réagit quand on clique dessus.

## Mouvements

Unités : Bip est dessiné sur une grille 32 × 32. **À 16 px, 2 unités = 1 px** ; les amplitudes
suivent donc la taille d'affichage. Les mouvements du corps pivotent en bas au centre (50 %, 87,5 %),
entre les pieds.

| État | Détail |
|---|---|
| Attente : respiration | Échelle X 1 → 1,04, Y 1 → 1,08, aller-retour, SineEase InOut. Cycle **1,6 s** (0–1 min), **1,1 s** (1–3 min), **0,8 s** (au-delà). Le rythme ne change qu'en fin de cycle. |
| Attente : bras | Rotation −10° → +12° autour de l'épaule (24, 16), même cycle. Apparaît en 200 ms, disparaît en 150 ms. |
| Attente : antenne | Boule de rayon 2,2 → 2,8 en 200 ms (BackEase Out, amplitude 0,3). |
| Attente : tremblement | −4° / +4° / −3° / 0 en 300 ms, toutes les 6 s (4 s après 3 min). |
| En cours, neutre | Translation Y 0 → −2 u et rotation −2° ↔ +2°, cycle 3,2 s, SineEase InOut. Départ décalé de 0 à 1 s au hasard : plusieurs Bip ne flottent pas en phase. |
| Prêt | Une seule fois : tassement à 90 ms (X 1,06 / Y 0,90), montée jusqu'à 270 ms (Y −6 u, X 0,96 / Y 1,05, QuadraticEase Out), réception à 390 ms (X 1,08 / Y 0,90, QuadraticEase In), repos à 510 ms. |
| Erreur | Affaissement X 1,04 / Y 0,94 en 250 ms (QuadraticEase Out), pastille 0 → 1 en 200 ms (BackEase Out), puis figé. |
| Au repos | L'antenne se plie (fondu 400 ms), puis plus rien. |
| Changement d'état | Couleur interpolée en 250 ms, visages en fondu croisé en 150 ms. |
| Clic sur Bip | Au hasard : le saut de « prêt », un clignement d'yeux (120 ms), ou un signe du bras (deux allers-retours de 180 ms). |
| Coucou (`bip-coucou.svg`) | Deux signes du bras (−16° ↔ +12°), une pause, un clignement ; cycle 3,2 s. |

**Images par seconde** : les mouvements en boucle tournent à **15 images/s**, et à **8** dans
l'encoche repliée, où le flottement ne fait qu'un pixel. L'éclat de l'encoche tourne à 30.
Les transitions courtes restent à 60.

**Autour de Bip, dans l'encoche :**
- **Éclat** quand au moins une session attend : trait de 2 px collé au bord bas, 40 % de la largeur,
  dégradé transparent → `#FF5D52` à 75 % → transparent, qui balaie de gauche à droite en 2,4 s.
- **Barre des tâches** : après 5 min d'attente, la fenêtre Claude clignote (désactivable).
- **Prêt** : Bip s'efface au bout de 4 min.

## L'encoche

Un seul bloc `#000000`, collé au bord haut de l'écran principal, centré, coins du bas arrondis.
Deux congés concaves de 8 px le raccordent au bord de l'écran (à gauche `M0,0 H8 V8 A8,8 0 0 0 0,0 Z`,
à droite le même en miroir ; 4 px pour la forme trait).

| Forme | Dimensions | Contenu |
|---|---|---|
| Trait | 120 × 4 px, rayon bas 2 | Rien : aucune session active. |
| Repliée | hauteur 22 px, marge latérale 10, rayon bas 11 | Bip à 16 px, écart 6 px, au plus 5 puis « +N », dans l'ordre de lancement des sessions. Un ☾ quand « ne pas déranger » est actif. Les clics traversent. |
| Entrouverte | 60 px de haut, 320 à 440 px de large selon le nom, rayon bas 20, marges 12 / 18 | Bip 40 px + « `Migrations staging` attend ta réponse » (titre du chat ; 13/18 semibold) + type de demande (12/16, `#A3A3A3`). Reste 4 s. |
| Dépliée | 440 px de large, marges 12 (20 en haut), rayon bas 24 | Bouton ☰ en haut à droite ; en haut à gauche, « ☾ jusqu'à 17:18 » (11,5, `#A3A3A3`) quand « ne pas déranger » est actif. Cartes de demande en tête (fond `#161616`, rayon 14, marge 14), puis lignes de session de 36 px (Bip 20 px, « titre · dossier/ » en Cascadia Mono 12,5, état et durée à droite) séparées par `#1C1C1C` ; 48 px avec le récap de fin de tour (11,5, `#3FD07F`). Sous-agents : sous-lignes de 24 px en retrait de 34 px, « ↳ description » (12) et durée, vertes une minute une fois finies ; 5 au plus, puis « +N autres ». Les réglages s'ajoutent en dessous, après un trait `#2A2A2A`. |

**Carte de permission** : Bip 40 px + nom du projet (Cascadia Mono 13) + « attend depuis 2 min »
(12, `#A3A3A3`) ; question 14/20 semibold ; commande en Cascadia Mono 12/18 sur fond `#000`,
bordure `#2A2A2A`, rayon 8 ; boutons hauteur 32, rayon 8 : **Autoriser** (`#F2F2F2`, texte noir),
**Refuser** (`#2A2A2A`, texte `#F2F2F2`), lien « Répondre dans Claude » (`#A3A3A3`, souligné) ;
« Toujours : … » en bouton contour `#333333`, hauteur 28, sur sa propre ligne.

**Réglages** : lignes de 12,5 px avec une précision en 11 px `#A3A3A3` ; interrupteurs 30 × 17,
`#3FD07F` activé, `#333333` sinon ; survol `#FFFFFF` à 7 %.

Ombre portée sous les formes entrouverte et dépliée : 0 12 28, noir à 40 %.

### Transformations

| Mouvement | Durée | Courbe WPF |
|---|---|---|
| Ouverture (largeur, hauteur et rayon ensemble) | 500 ms, après 200 ms de survol | `BackEase` EaseOut, amplitude 0,3 |
| Contenu à l'ouverture | dès 200 ms, chaque ligne : opacité 0 → 1 et Y +4 → 0 px en 180 ms, décalage de 40 ms | `CubicEase` EaseOut |
| Fermeture | 300 ms après la sortie de la souris : contenu effacé en 100 ms, bloc en 350 ms | `KeySpline="0.4,0 0.2,1"` |
| Entrouverte | ouverture 400 ms, tient 4 s, fermeture 350 ms | `BackEase` EaseOut, amplitude 0,2 |

## Palette et typographie

États : voir plus haut. Interface : fond `#000000`, carte `#161616`, bouton secondaire `#2A2A2A`,
contour « Toujours » `#333333`, séparateur `#1C1C1C`, texte `#F2F2F2` (18,8:1 sur noir), noms et
commandes `#E6E6E6`, secondaire `#A3A3A3` (7,2:1 sur carte).

Polices : **Segoe UI Variable Text** pour l'interface, **Cascadia Mono** pour les noms de projet et
les commandes. Tailles dans `tokens.json` → `typography.styles`.

## Réutiliser Bip

Bip est sous la même [licence MIT](../../LICENSE) que le reste du projet. Les SVG et PNG
s'utilisent tels quels. Pour une application WPF, le contrôle de `wpf/` se pose à 16, 20 ou
40 px et se pilote par sa propriété `State` (`Waiting`, `Running`, `Ready`, `Error`, `Idle`) ;
`BipResources.xaml` fournit les pinceaux et une image fixe par état.
