# YuGiOhOverlay — Duel Companion

Assistant d'observation pour **Windows x64, .NET 8 et WPF** : capture de la zone de main calibrée, identification des illustrations, correspondance avec les cartes de départ des combos Master Duel Meta, puis guide pas à pas dans un overlay.

**Version expérimentale.** La reconnaissance doit être vérifiée sur votre affichage. Une suggestion indique une main de départ compatible ; elle ne garantit pas la légalité ou la pertinence du combo sur le terrain courant. Aucun clic ni entrée de jeu n'est généré.

## Démarrer

```powershell
git switch feature/automatic-hand-combo-overlay
dotnet run --project YuGiOhOverlay.UI
```

Dans l'application :

1. Essayer **la démo** pour découvrir l'interface sans capture, réseau ou jeu.
2. Coller le lien **Combos & Counters** avec le deck sélectionné. Le lien Blue-Eyes est prérempli. **Synchroniser** importe les combos et met leurs illustrations en cache local.
3. Sélectionner la fenêtre **Master Duel**. En mode fenêtré ou sans bordure, afficher une main puis **Calibrer** en traçant un rectangle contenant toutes ses illustrations visibles. Échap annule. Aucune capture n'est enregistrée pendant cette sélection.
4. **Lancer l'observation**, puis remettre le jeu au premier plan. Le programme attend plusieurs observations cohérentes avant de suggérer un combo. Une fenêtre masquée, minimisée ou inactive suspend l'observation.
5. Choisir une suggestion, lire les prérequis et **Suivre ce combo**. La liste des cartes de départ disparaît et la recherche de nouveaux combos s’arrête réellement. **Quitter le combo** reprend la recherche après une nouvelle stabilisation ; Suspendre conserve le guide et son étape.
6. Pour suivre les invocations, utiliser aussi **Calibrer mon terrain** avant de lancer l’observation : sélectionner uniquement le terrain allié, sans la main ni le terrain adverse. Les ajouts à la main utilisent la zone de main déjà calibrée.
7. **Avancer automatiquement sur indice visuel** observe uniquement les cartes attendues par l’étape actuelle. Attendre l’indication **Prêt** avant de réaliser chaque étape pour établir son état initial. Désactiver cette case pour un suivi entièrement manuel. Les flèches restent disponibles pour corriger ou confirmer une étape.

| Raccourci | Action |
| --- | --- |
| Ctrl+F1 | Passer l'overlay de passif à interactif, puis inversement |
| Ctrl+F2 | Suspendre l'observation |
| Ctrl+→ / Ctrl+← | Avancer/reculer dans le guide sélectionné |

Déplacer l'overlay par son en-tête en mode interactif. Il reste au-dessus des fenêtres et laisse passer les clics en mode passif. Éviter de le poser sur la main. Si Ctrl+F1 est déjà réservé, l'overlay reste interactif. Le bouton **Réglages** ramène à la configuration.

## Reconnaissance et limites

- Les illustrations sont comparées par caractéristiques **SIFT**, avec contrôle géométrique RANSAC et consolidation sur plusieurs images. Aucun OCR, modèle IA ou GPU n'est nécessaire.
- La recherche porte sur les cartes référencées dans le catalogue du deck synchronisé. Une carte hors catalogue, trop recouverte, floue, animée ou avec une illustration alternative peut rester inconnue. Le nombre de cartes identifiées n'est pas une estimation du nombre total de cartes de la main.
- Les scores sont des indicateurs géométriques, pas des probabilités calibrées. Les paramètres initiaux sont des valeurs de départ à mesurer sur de vrais duels.
- La capture GDI vise uniquement la ROI de la fenêtre sélectionnée. Le plein écran exclusif peut donner une image noire ; utiliser le mode fenêtré sans bordure. La fenêtre doit rester visible et non recouverte par une autre application.
- Les coordonnées sont relatives au client de la fenêtre et suivent ses déplacements. Un changement de ratio ou de DPI demande une recalibration ; une variation de taille remet à zéro la stabilisation.
- Le diagnostic affiche uniquement la ROI, ses boîtes, les noms, scores et temps capture/vision/décision. Il reste en mémoire. Aucune capture ne quitte le PC.
- Pendant un combo, seules les illustrations attendues par l’étape sont recherchées, dans la main ou la zone de terrain allié. Il n’y a aucune recherche de starters ni classement de combos en arrière-plan. Pour une étape manuelle, aucune capture n’est effectuée.
- L’avancement automatique est une **aide expérimentale fondée sur un résultat visuel**, pas une vérification des règles ou de la résolution d’un effet. Il vise les étapes dont le dernier résultat explicite est un ajout à la main ou une invocation. Une base stable est d’abord observée, puis une augmentation du nombre d’exemplaires attendus doit être confirmée sur plusieurs images. Une carte déjà visible ne valide rien ; chaque étape repart de sa propre base. Un changement de fenêtre, une analyse trop lente ou une interruption annule la confirmation en cours.
- Une carte jouée avant l’acquisition de la base, une animation manquée, une illustration masquée ou un retour au même nombre d’exemplaires demande une validation manuelle. Une pioche indépendante ou une autre action qui produit la même carte peut ressembler au résultat attendu : garder les flèches de correction à portée de main et désactiver l’option si nécessaire.
- Les activations seules, poses face cachée, choix conditionnels et résultats non déterminables restent manuels. Le terrain complet, le tour, les effets déjà utilisés, le deck restant et les interruptions ne sont pas interprétés. Les prérequis et notes de l’auteur restent visibles.
- Les suggestions sont triées par votes nets du site, puis identifiant stable. Ce classement n'est pas une évaluation stratégique du meilleur coup.

## Source Master Duel Meta

L'adaptateur utilise la réponse publique effectivement observée sur le site le 9 novembre 2026, avec pagination bornée : `GET /api/v1/combos/paginated?deckType=...&sort=netVotes&limit=20&page=...`. Ce n'est pas une API publique documentée avec garantie de stabilité. Un changement de format ou une erreur réseau est signalé et conserve le catalogue précédent.

Les liens de chaque combo, auteurs, notes, prérequis et étapes sont conservés. Les contenus restent dans leur langue d'origine. Les illustrations, y compris les versions alternatives trouvées pour ces cartes, sont téléchargées à la synchronisation depuis le CDN utilisé par le site, puis réutilisées hors ligne. Les variantes partagent l’identité de la carte dans le moteur. Elles ne sont pas redistribuées dans ce dépôt.

Les conditions prises en charge sont des cartes précises liées uniquement par AND ou uniquement par OR, avec quantités. Les expressions génériques, parenthésées, mixtes ou comportant une condition de zone non vérifiable sont exclues et détaillées après import. Ne jamais retirer silencieusement un prérequis. La réponse Blue-Eyes utilisée pendant le développement comportait 18 combos : 12 importables et 6 exclus explicitement ; ces nombres peuvent évoluer.

## Données, ancien format et configuration

Les fichiers résident dans `%LOCALAPPDATA%\YuGiOhOverlay` : `assistant.json`, `combos.v3.json` et `artwork/`. Les anciennes données `combos.v2.json` sont lues si aucun v3 n’existe, puis migrées en mémoire sans modifier l’original. Les réglages de reconnaissance sont typés dans `YuGiOhOverlay.Application/ObservationContracts.cs`, chargés depuis `assistant.json` et validés avant observation. Fermer l'application avant de les modifier.

Le catalogue v3 est documenté dans [docs/combos.schema.json](docs/combos.schema.json) et illustré par [examples/combos.v3.example.json](examples/combos.v3.example.json). Il ajoute `guideSteps` : instructions séparées (carte/action/précision/connecteur), branches et éventuel `cue` décrivant l’apparition attendue. Les actions textuelles restent conservées. Le contrat v2 reste disponible dans [docs/combos.v2.schema.json](docs/combos.v2.schema.json).

**Après mise à jour, resynchroniser le deck** pour récupérer la présentation structurée et les indices visuels. Un ancien catalogue v2 conserve ses textes et reste utilisable en manuel : la migration n’invente pas d’événement visuel. Le bouton **Importer un catalogue JSON** accepte v2 et v3. Les exemples contiennent uniquement des données fictives ; ce ne sont pas des decks réels à synchroniser.

Les `data.json` v1 restent accessibles via **Mes anciennes fiches**, sans modification automatique. Ils ne contiennent pas les prérequis nécessaires à la détection : ils ne sont donc pas transformés en règles automatiques. Resynchroniser un deck depuis Master Duel Meta pour passer au nouveau parcours.

## Architecture

| Projet / chemin | Responsabilité |
| --- | --- |
| `YuGiOhOverlay.Domain/` | Cartes, état observé, règles et classement déterministe ; aucune dépendance technique |
| `YuGiOhOverlay.Application/` | Capture → reconnaissance → consolidation → recommandation, contrats, annulation et démo |
| `YuGiOhOverlay.Infrastructure/` | Adaptateur Master Duel Meta, validation JSON, cache atomique et anciens fichiers |
| `YuGiOhOverlay.Vision/` | Adaptateur OpenCV ; comparaison d'illustrations |
| `YuGiOhOverlay.Windows/` | Inventaire des fenêtres et capture GDI limitée à la ROI |
| `YuGiOhOverlay.UI/` | Configuration, calibration, overlay, diagnostics et view-model |
| `YuGiOhOverlay.UI/CompositionRoot.cs` | Injection par constructeur et durée de vie des dépendances |

`IActionExecutor` n'est volontairement pas implémenté : aucun mode d'exécution n'est disponible. L'architecture n'ajoute pas d'interface inutilisée pour une future fonction. Aucun accès mémoire du jeu, injection, interception réseau ou contournement d'anti-cheat. Utiliser dans les contextes privés/hors ligne ou autorisés.

## Construire et vérifier

Ouvrir la solution **`YuGiOhOverlay.sln` à la racine** dans Visual Studio avec la charge **Développement Desktop .NET**. SDK .NET 8 requis.

```powershell
dotnet restore YuGiOhOverlay.sln
dotnet build YuGiOhOverlay.sln -c Release -warnaserror
dotnet test YuGiOhOverlay.Domain.Tests -c Release
dotnet test YuGiOhOverlay.Vision.Tests -c Release
dotnet test YuGiOhOverlay.UI.Tests -c Release
.\build-release.bat
```

Les tests couvrent conditions AND/OR, quantités, priorités, états inconnus/périmés, conflits et doublons, stabilisation, annulation, erreurs de capture simulées, JSON invalide/versionné, migration v2/v3 et import. Le suivi est testé avec des adaptateurs simulés : suspension réelle de la recherche, apparition stable, ancien résultat rejeté, changement de géométrie, progression unique et reprise de recherche après sortie. Les tests WPF couvrent le verrouillage du guide et la lisibilité des instructions. Les tests natifs de vision utilisent des illustrations synthétiques déterministes (rotation, échelle, double exemplaire, image vide), jamais une vraie capture ou un clic. Ils nécessitent Windows pour le runtime OpenCV inclus dans le projet de tests.

La CI Windows compile, exécute les tests et publie un artefact `YuGiOhOverlay-windows-x64` contenant l'exécutable autonome. La validation en jeu et du rendu WPF reste à faire sur Windows ; voir [docs/validation.md](docs/validation.md).

## Dépendance ajoutée

**OpenCvSharp4 + OpenCvSharp4.runtime.win 4.11.0.20250507** (licence Apache-2.0) apportent SIFT, RANSAC et le décodage WebP. Cette version est compatible avec le compilateur du SDK .NET 8 utilisé. Alternative plus légère : comparaison de modèles en C# pur, moins adaptée aux cartes inclinées, redimensionnées et partiellement recouvertes. Pas de dépendance de production à un framework d'injection ou à un service IA.
