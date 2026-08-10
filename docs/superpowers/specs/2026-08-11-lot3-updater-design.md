# GameSave Hub V1 — Lot 3 : installateur unique et mises à jour à distance

**Statut :** conception validée avec l'utilisateur le 11 août 2026

**Périmètre :** packaging et diffusion du client Windows 11 x64 déjà livré au Lot 2 (`0.4.0-pilot`, slot local permanent `GSH-MONDE-PARTAGE`, verrou de version API). N'ajoute et ne modifie aucun comportement de synchronisation de sauvegarde.

## 1. Contexte

Le Lot 2 a livré un client fonctionnel mais dont l'installation et la mise à jour restent entièrement manuelles : zip à extraire, script `.cmd` à lancer, fichier `LISEZ-MOI` à lire, et toute nouvelle version doit être reconstruite puis renvoyée individuellement à chaque joueur (comme cela a été fait pour Bob). Ce parcours reste supportable à deux ou trois joueurs mais ne passe pas à l'échelle et repose sur la disponibilité de Steven pour chaque mise à jour.

Le Lot 2 a anticipé ce besoin : `managed-slot.json` est séparé de `client-state.json` avec un schéma versionné, `maintenance-status` expose déjà un signal de sûreté en lecture seule (jeu fermé, aucune session active, aucune transition en cours, checkpoints durables), et `ClientCompatibility.MinimumAcquireVersion` empêche un client obsolète de reprendre la main. Le Lot 3 construit sur ces fondations sans les modifier.

## 2. Décision produit

Un seul exécutable signé, `GameSaveHub-Setup.exe`, publié en single-file .NET self-contained, sert à la fois d'installateur et de moteur de mise à jour. Il n'y a pas de second outil séparé : la même vérification de signature, la même logique de bascule de dossier et le même respect de `maintenance-status` s'appliquent que ce soit un joueur qui double-clique la première fois ou une tâche planifiée qui l'invoque en silence plus tard.

« Signé » désigne ici une vérification d'intégrité interne (une paire de clés que Steven contrôle), pas un certificat Authenticode commercial : l'objectif n'est pas de faire disparaître l'avertissement SmartScreen au premier lancement, mais d'empêcher qu'un paquet corrompu ou falsifié soit appliqué automatiquement lors d'une mise à jour silencieuse.

## 3. Modes d'exécution

### 3.1 Mode interactif (premier lancement, double-clic)

Identique au parcours actuel côté résultat (élévation UAC, installation du service `GameSaveHubClient`, de l'application WPF et du raccourci, préservation de l'identité CNG et du pseudo si déjà présents), mais porté dans le binaire unique au lieu d'un script PowerShell externe : plus de zip à extraire, plus de fichier texte à lire séparément — les instructions nécessaires (pseudo, code d'invitation) restent affichées par l'application WPF elle-même après l'installation, comme aujourd'hui.

À la fin d'une installation réussie, il enregistre une tâche planifiée Windows nommée `GameSaveHubUpdater`, configurée pour s'exécuter avec les droits les plus élevés sans session utilisateur ouverte (« Run whether user is logged on or not »), toutes les 6 heures, invoquant ce même exécutable avec l'argument `--auto-update`.

### 3.2 Mode silencieux (`--auto-update`, tâche planifiée)

N'affiche aucune fenêtre. Séquence :

1. `GET /api/v1/client/latest` (voir §5) pour connaître la dernière version publiée.
2. Si la version publiée n'est pas strictement plus récente que celle installée, ne rien faire.
3. Télécharger le paquet dans un dossier de préparation (`%ProgramData%\GameSaveHub\update-staging`), jamais dans l'installation active.
4. Vérifier le SHA-256 du paquet téléchargé contre celui du manifeste, puis la signature du manifeste lui-même (§4). Si l'une des deux vérifications échoue, supprimer le dossier de préparation et s'arrêter — jamais de nouvelle tentative avec un contenu partiellement téléchargé.
5. Décompresser dans `update-staging\Client.new` et valider la présence des fichiers attendus (manifeste de paquet interne listant les fichiers, réutilisant le même principe que `SOURCE-SHA256SUMS.txt`).
6. Interroger `maintenance-status` par tube nommé. Si `SafeToUpdate=false`, s'arrêter sans rien modifier : la prochaine exécution planifiée réessaiera.
7. Si sûr : arrêter le service, effectuer la bascule (§6), redémarrer le service, vérifier qu'il répond au tube nommé sous 30 secondes.
8. Journaliser le résultat (succès, échec de vérification, report pour cause de jeu actif, échec de démarrage post-bascule) dans le même répertoire de diagnostics que le reste du client.

## 4. Signature et intégrité

Une paire de clés ECDSA P-256 — même famille cryptographique que l'identité machine CNG déjà utilisée par le client, pour ne pas introduire un second mécanisme dans le dépôt. La clé privée n'est jamais commitée ni déployée sur le NAS ; elle reste uniquement sur le poste de Steven, utilisée au moment de publier une nouvelle version. La clé publique est une constante compilée dans `GameSaveHub-Setup.exe`.

Le manifeste publié (§5) est signé dans son intégralité (tous ses champs, y compris le hash du paquet) ; le client vérifie la signature du manifeste avant de faire confiance au hash qu'il contient, puis vérifie le hash du paquet téléchargé contre ce hash signé. Un paquet dont le hash correspond mais dont le manifeste n'est pas signé par la clé publique attendue est rejeté.

## 5. Distribution des mises à jour

Nouvel endpoint sur `GameSaveHub.Server.Api`, **non authentifié** (un poste tout juste installé n'a pas encore d'identité enrôlée, et ces métadonnées ne sont pas sensibles) :

```
GET /api/v1/client/latest
→ 200 {
    "version": "0.5.0",
    "sha256": "<hash du paquet .zip>",
    "signature": "<signature ECDSA du manifeste>",
    "downloadUrl": "/api/v1/client/packages/0.5.0"
  }
```

Le paquet lui-même est stocké comme un objet immuable supplémentaire dans le même `ImmutableArtifactStore` déjà utilisé pour les sauvegardes (adressage par contenu, même répertoire `data/objects/`), servi par un second endpoint non authentifié en lecture seule. Publier une nouvelle version reste un acte administratif explicite : une nouvelle commande `admin` (`client-release publish <fichier.zip> <version>`) signe et enregistre le manifeste ; elle ne s'exécute jamais automatiquement.

## 6. Bascule et réconciliation

Toute la partie risquée (téléchargement, décompression, vérification) se déroule dans `update-staging`, jamais sur l'installation active (`%ProgramFiles%\GameSaveHub\Client`). La bascule elle-même, une fois tout validé, se limite à deux renommages de dossier, chacun atomique côté système de fichiers NTFS :

1. `Client` → `Client.old`
2. `Client.new` → `Client`

Il n'y a pas de rollback automatique au sens d'un moteur dédié : la garantie de sûreté vient du fait que rien n'est modifié tant que tout n'a pas été validé en préparation. Si une coupure survient entre les deux renommages, l'état est détectable sans ambiguïté au prochain démarrage (du service ou de la tâche planifiée) :

| État observé | Interprétation | Action |
|---|---|---|
| `Client` présent, pas de `Client.old` | Bascule non commencée ou déjà nettoyée | Rien à faire |
| `Client` et `Client.old` présents | Bascule terminée, nettoyage non fait | Supprimer `Client.old` |
| `Client.old` présent, pas de `Client` | Coupure entre les deux renommages | Renommer `Client.old` → `Client` |

Cette table suit le même principe que `ReconcileManagedSlotReplacementAsync` du Lot 2 : trois états, lecture seule pour décider, jamais de suppression avant d'avoir statué. Si le service ne redémarre pas correctement dans les 30 secondes suivant une bascule terminée, l'exécution silencieuse le journalise comme échec mais ne tente aucune action corrective automatique — l'ancien contenu n'existe alors plus (le nouveau a déjà remplacé l'ancien avec succès au niveau fichiers), donc il n'y a rien de sûr à restaurer automatiquement ; ce cas nécessite une intervention manuelle, journalisée clairement pour rester diagnosticable.

## 7. Compatibilité et déploiement

Rollout additif, comme pour le verrou de version au Lot 2 : la première version du client comportant la tâche planifiée doit être installée manuellement (Steven, puis Bob, puis chaque nouveau joueur lors de son onboarding) puisqu'un client antérieur n'a pas encore de tâche planifiée pour découvrir les mises à jour suivantes. Une fois ce socle en place sur un poste, toute version publiée ensuite s'y propage seule.

`managed-slot.json`, l'identité CNG et le pseudo enregistré restent dans `%ProgramData%\GameSaveHub`, jamais touchés par la bascule de dossier (qui ne porte que sur `%ProgramFiles%\GameSaveHub\Client`).

## 8. Hors périmètre

- Certificat Authenticode commercial et suppression de l'avertissement SmartScreen.
- Rollback automatique multi-versions ou reprise après un service qui démarre mais fonctionne mal (au-delà de l'échec de démarrage sous 30 s).
- Mise à jour poussée depuis le NAS vers un poste précis à la demande (déclenchement reste toujours côté client, à l'heure planifiée).
- Toute modification du protocole de transfert de sauvegarde, du slot permanent ou du verrou de version déjà livrés au Lot 2.
- Distribution au-delà du groupe de joueurs déjà enrôlés (pas de portail public de téléchargement).

## 9. Vérification

Tests unitaires : vérification de signature (paquet valide, hash altéré, signature invalide, manifeste non signé), résolution des trois états de réconciliation du §6, et la condition de report quand `maintenance-status` renvoie `SafeToUpdate=false`. Validation réelle sur le PC de Steven en premier (installation par le nouvel exécutable unique, puis déclenchement manuel du mode silencieux avec une version factice plus récente) avant toute diffusion à Bob ou à un nouveau joueur.
