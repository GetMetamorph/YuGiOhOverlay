# Validation manuelle sur Windows

À réaliser avant de considérer la reconnaissance prête pour un duel. Les tests unitaires ne mesurent pas sa précision réelle.

1. **Démo** : les deux cartes fictives et le combo apparaissent après trois observations ; changer d'étape, suspendre et relancer. Aucune fenêtre de jeu n'est nécessaire et aucun clic n'est émis.
2. **Import** : synchroniser le lien Blue-Eyes, vérifier les exclusions, ouvrir une source et comparer l'ordre des étapes, leurs annotations, les notes et les conditions avec le site. Débrancher le réseau après synchronisation : le catalogue reste chargeable. Une URL invalide ou un JSON de version inconnue doit afficher une erreur sans remplacer le catalogue précédent.
3. **Calibration** : tester 1920×1080/2560×1440 et DPI 100/150 %, fenêtres déplacées et écran secondaire. Seule la main doit apparaître dans le diagnostic. Changer de ratio/DPI pendant l'observation doit demander une recalibration.
4. **Reconnaissance** : vérifier une main stable de 5 puis 6 cartes ; deux exemplaires identiques, cartes alternatives, main inclinée/recouverte et animation de pioche. Relever vrais positifs, faux positifs et inconnues. Ne pas considérer une absence de faux positifs sur une seule image comme une mesure de précision générale.
5. **Consolidation** : retirer/changer une carte fait disparaître la suggestion automatique ; les nouvelles cartes doivent rester visibles plusieurs images. Un guide explicitement conservé reste un guide manuel distinct de la suggestion courante.
6. **Suspension** : Alt+Tab, minimiser, fermer la fenêtre cible, Ctrl+F2 et fermeture de l'application. Aucune nouvelle capture après arrêt ; aucun état ancien ne doit redevenir une suggestion après reprise sans stabilisation.
7. **Overlay** : vérifier le contraste et le défilement à 360 px de largeur, le déplacement, le redimensionnement, les raccourcis et leur conflit éventuel, les clics traversants et la réactivité pendant import/observation.
8. **Budget CPU** : relever les temps capture/vision/décision pour le catalogue choisi. En cas de dépassement de l'âge maximal de l'état, aucune recommandation ne doit être émise. Ajuster la ROI, le nombre de caractéristiques ou les seuils après mesures ; ne pas abaisser les seuils pour forcer une identification.

Aucune capture utilisateur ni illustration de carte téléchargée n'est commitée. Les fixtures de vision du dépôt sont générées par `YuGiOhOverlay.Vision.Tests/SiftRecognitionTests.cs` avec une graine constante et des tolérances explicites.
