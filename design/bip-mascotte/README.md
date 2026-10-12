# Bip — kit de design de la mascotte ClaudeNotch

Bip est la mascotte de ClaudeNotch : **un petit robot par session Claude Code**, debout sur le bord de l'encoche. Ce dossier contient tout ce qu'il faut pour remplacer le personnage provisoire (Clawd + pastille) dans l'appli C# / WPF existante.

Source des valeurs : `tokens.json` fait foi. Ce README les explique ; le code WPF les applique.

## Contenu

| Chemin | Rôle |
|---|---|
| `wpf/BipControl.xaml` + `.xaml.cs` | Contrôle WPF animé. Une propriété `State` (`Waiting`, `Running`, `Ready`, `Error`, `Idle`) gère couleur, visage, antenne, bras, pastille et mouvements. **C'est le livrable principal.** |
| `wpf/BipResources.xaml` | ResourceDictionary : couleurs et pinceaux (états + interface de l'encoche), polices, et une `DrawingImage` statique par état (`Bip.Waiting.Image`…). |
| `svg/bip-*.svg` | Les 5 états en vectoriel, fond transparent, grille 32 × 32. |
| `png/{16,20,40,256}/bip-*.png` | Les mêmes, rendus à chaque taille d'affichage, fond transparent. |
| `tokens.json` | Toutes les valeurs : géométrie, couleurs, typo, dimensions de l'encoche, durées et courbes. |
| `apercu-anime.html` | Ouvrir dans un navigateur pour voir les mouvements. |
| `apercu-planche.png` | Les 5 états à 128, 40, 20 et 16 px sur fond noir. |

## Intégration (à faire par Claude Code)

1. Copier `wpf/BipControl.xaml` et `wpf/BipControl.xaml.cs` dans le projet, puis adapter le namespace `ClaudeNotch.Controls` à celui du projet.
2. Fusionner `wpf/BipResources.xaml` dans `App.xaml` (`ResourceDictionary.MergedDictionaries`).
3. Remplacer chaque usage du personnage provisoire (image Clawd + pastille de couleur) par :
   ```xml
   <controls:BipControl State="{Binding BipState}" Width="16" Height="16"/>
   ```
   Tailles : **16 px** dans l'encoche repliée, **20 px** dans les lignes de session, **40 px** dans les cartes de demande et la forme entrouverte.
4. Mapper l'état de session existant vers `BipState` (voir tableau ci-dessous). Si la session attendait déjà avant que le contrôle soit créé (ex. réouverture de l'encoche), renseigner `WaitingSince` pour que le rythme d'impatience reparte du bon niveau.
5. Supprimer l'ancienne pastille de couleur : la couleur est maintenant portée par Bip lui-même.
6. Pour une image fixe sans animation (aperçu, tooltip), utiliser `{StaticResource Bip.Running.Image}` dans un `Image`.

Le contrôle gère seul : interpolation de couleur, fondu des visages, respiration qui accélère, tremblement périodique, saut de joie, affaissement d'erreur, antenne pliée. Ce qui reste côté appli : l'éclat sous l'encoche, le clignotement de la barre des tâches, l'effacement « prêt » à 10 min, et les transformations de l'encoche.

## Les 5 états

| `BipState` | Quand (brief) | Couleur | Lecture sans couleur | Mouvement |
|---|---|---|---|---|
| `Waiting` | Permission ou question en attente, ou arrêt depuis 1 min | `#FF5D52` | **Bras levé**, antenne gonflée, yeux ronds, bouche ouverte | Respiration + bras qui s'agite ; accélère avec le temps ; tremblement |
| `Running` | Claude réfléchit, lit, exécute | `#F5A524` | Yeux plissés en traits, antenne droite | Flottement doux |
| `Ready` | Claude a fini et rend la main | `#3FD07F` | Yeux rieurs, sourire | Un saut à l'arrivée, puis immobile |
| `Error` | Tour arrêté sur une erreur API | `#8E96A3` + pastille `#FFC93D` | **Pastille « ! »** jaune, corps tassé, petits yeux | Affaissement à l'entrée, puis figé |
| `Idle` | Session ouverte au repos | `#4A4A4E` | Yeux fermés, **antenne pliée** | Aucun |

`Waiting` est le seul état rouge et le seul qui bouge en continu : c'est ce qui le fait ressortir en vision périphérique. L'erreur est volontairement grise (et non rouge comme dans le personnage provisoire) pour ne pas se confondre avec l'attente.

## Mouvements

Unités : la mascotte est dessinée sur une grille 32 × 32. **À 16 px, 2 unités = 1 px** ; les amplitudes en unités suivent donc la taille d'affichage. Pivot des mouvements du corps : bas-centre (50 %, 87,5 %), entre les pieds.

| État | Détail |
|---|---|
| Attente — respiration | Échelle X 1 → 1,04, Y 1 → 1,08, aller-retour, SineEase InOut. Cycle **1,6 s** (0–1 min), **1,1 s** (1–3 min), **0,8 s** (au-delà). Le rythme change en fin de cycle seulement. |
| Attente — bras | Rotation −10° → +12° autour de l'épaule (24, 16), même cycle. Apparaît en 200 ms, disparaît en 150 ms. |
| Attente — antenne | Boule r 2,2 → 2,8 en 200 ms (BackEase Out, amplitude 0,3). |
| Attente — tremblement | −4° / +4° / −3° / 0 en 300 ms, toutes les 6 s (4 s après 3 min). |
| En cours | Translation Y 0 → −2 u, rotation −2° ↔ +2°, cycle 3,2 s, SineEase InOut. Départ décalé aléatoirement de 0 à 1 s pour que plusieurs Bip ne flottent pas en phase. |
| Prêt | Une seule fois : tassement 90 ms (X 1,06 / Y 0,90) → montée jusqu'à 270 ms (Y −6 u, X 0,96 / Y 1,05, QuadraticEase Out) → réception à 390 ms (X 1,08 / Y 0,90, QuadraticEase In) → repos à 510 ms. |
| Erreur | Affaissement X 1,04 / Y 0,94 en 250 ms (QuadraticEase Out), pastille 0 → 1 en 200 ms (BackEase Out). Puis figé. |
| Inactif | Antenne qui se plie (fondu 400 ms), puis plus rien. |
| Changement d'état | Couleur interpolée 250 ms, visages en fondu croisé 150 ms. |

À implémenter côté appli :
- **Éclat sous l'encoche** quand au moins une session attend : trait de 2 px collé au bord bas, 40 % de la largeur, dégradé transparent → `#FF5D52` à 75 % → transparent, balaie de gauche à droite en 2,4 s, linéaire, en boucle. Le bloc noir doit couper ce qui dépasse (`ClipToBounds`).
- **Barre des tâches** : au-delà de 5 min d'attente, faire clignoter la fenêtre Claude (`FlashWindowEx`).
- **Prêt** : effacer Bip au bout de 10 min (opacité 1 → 0 en 400 ms).

## L'encoche

Un seul bloc `#000000`, collé au bord haut de l'écran principal, centré, coins du bas arrondis. Deux petits congés concaves de 8 px le raccordent au bord de l'écran (à gauche : `M0,0 H8 V8 A8,8 0 0 0 0,0 Z`, à droite le même en miroir ; 4 px pour la forme trait).

| Forme | Dimensions | Contenu |
|---|---|---|
| Trait | 120 × 4 px, rayon bas 2 | Rien. Aucune session active. |
| Repliée | hauteur 22 px, marge latérale 10, rayon bas 11 | Bip à 16 px, écart 6 px, au plus 5 puis « +N ». Ordre de lancement des sessions, fixe. Les clics traversent. |
| Entrouverte | 320 × 60 px, rayon bas 20, marges 12 / 18 | Bip 40 px + « `nhl_predictor` attend ta réponse » (13/18 semibold) + type de demande (12/16, `#A3A3A3`). Reste 4 s. |
| Dépliée | 440 px de large, marge 12, rayon bas 24 | Cartes de demande en tête (fond `#161616`, rayon 14, marge 14), puis lignes de session de 36 px (Bip 20 px, nom en Cascadia Mono 12,5, état + durée à droite) séparées par `#1C1C1C`. |

Carte de permission : Bip 40 px + nom du projet (Cascadia Mono 13) + « attend depuis 2 min » (12, `#A3A3A3`) ; question 14/20 semibold ; commande en Cascadia Mono 12/18 sur fond `#000`, bordure `#2A2A2A`, rayon 8 ; boutons hauteur 32, rayon 8 : **Autoriser** (`#F2F2F2` / texte noir), **Refuser** (`#2A2A2A` / texte `#F2F2F2`), lien « Répondre dans Claude » (`#A3A3A3`, souligné) ; « Toujours : … » en bouton contour `#333333`, hauteur 28, sur sa propre ligne.

Ombre portée sous les formes entrouverte et dépliée : 0 12 28, noir 22 % sur fond clair, 45 % sur fond foncé.

### Transformations

| Mouvement | Durée | Courbe WPF |
|---|---|---|
| Ouverture (largeur, hauteur et rayon ensemble) | 500 ms, après 200 ms de survol | `BackEase` EaseOut, Amplitude 0,3 |
| Contenu à l'ouverture | dès 200 ms, chaque ligne : opacité 0 → 1 et Y +4 → 0 px en 180 ms, décalage 40 ms | `CubicEase` EaseOut |
| Fermeture | contenu effacé en 100 ms, bloc en 350 ms ; 300 ms après la sortie de la souris (proposé) | `KeySpline="0.4,0 0.2,1"` |
| Entrouverte | ouverture 400 ms, tient 4 s, fermeture 350 ms | `BackEase` EaseOut, Amplitude 0,2 |

## Palette et typographie

États : voir le tableau ci-dessus. Interface : fond `#000000`, carte `#161616`, bouton secondaire `#2A2A2A`, contour « Toujours » `#333333`, séparateur `#1C1C1C`, texte `#F2F2F2` (18,8:1 sur noir), noms et commandes `#E6E6E6`, secondaire `#A3A3A3` (7,2:1 sur carte).

Polices : **Segoe UI Variable Text** pour l'interface, **Cascadia Mono** pour les noms de projet et les commandes. Tailles dans `tokens.json` → `typography.styles`.

## Contrôles avant de valider

- [ ] À 16 px, sur un écran Windows réel, à 100 % **et** 125 % de mise à l'échelle : on distingue les 5 états sans la couleur (bras, pastille, antenne pliée).
- [ ] L'antenne pliée de l'état inactif reste visible à 16 px (elle ne fait qu'environ 1 px d'épaisseur).
- [ ] Deux sessions « en cours » côte à côte ne flottent pas en phase.
- [ ] L'attente qui dure 5 min reste supportable (pas de clignotement, mouvements de quelques pixels).
- [ ] Aucun reste de l'ancien personnage (image Clawd, pastille de couleur).
