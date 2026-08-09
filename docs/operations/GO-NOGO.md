# Verrou de transfert d'hôte

Le serveur, Docker et le client Probe peuvent être construits et déployés avant l'accès au deuxième PC. La fonction dangereuse reste bloquée par :

```text
GSH_ALLOW_HOST_TRANSFER=false
```

Le passage à `true` est interdit tant que les preuves suivantes ne sont pas réunies :

1. rapport Probe du PC distant analysé ;
2. monde jetable identifié sur chaque PC ;
3. snapshots cohérents avant chaque mutation ;
4. trois cycles complets A → B → A ;
5. identités, inventaires, équipements, positions et rôle hôte conservés ;
6. redémarrage Windows inclus ;
7. Xbox Cloud connecté et conflits simulés ;
8. aucun remplacement cloud indéterministe.

Tout échec de cette liste impose le no-go documenté. Le déploiement NAS ne constitue pas une preuve de portabilité WGS.


## État des preuves au 9 août 2026

Relevé vérifié sur le NAS et sur `PC-STEVEN`, pas repris du paragraphe suivant.

| # | Condition | État |
|---|---|---|
| 1 | Rapport Probe du PC distant analysé | ✅ |
| 2 | Monde jetable identifié sur chaque PC | ✅ |
| 3 | Snapshots cohérents avant chaque mutation | ✅ |
| 4 | Trois cycles complets A → B → A | ❌ 2 cycles aboutis, mais sur une seule machine |
| 5 | Identités, inventaires, équipements, positions, rôle hôte conservés | ✅ |
| 6 | Redémarrage Windows inclus | ❌ jamais testé |
| 7 | Xbox Cloud connecté et conflits simulés | ⚠️ connecté oui, conflit jamais provoqué |
| 8 | Aucun remplacement cloud indéterministe | ✅ |

S'y ajoute la reprise après interruption, exigée par la revue finale : la reprise après **échec de capture** est prouvée en conditions réelles (session `f85bcb7e`, reprise automatique au redémarrage du service, publication de la version `fe32692b`). La coupure **pendant l'import** reste à éprouver.

Le verrou serveur est actuellement **ouvert** pour la campagne pilote sur le seul monde `Shlags1`. Cette ouverture est temporaire et doit être refermée en fin de campagne : elle ne vaut pas levée du go/no-go, qui porte sur l'ouverture générale.

## État des preuves au 7 août 2026

Acquis :

- diagnostics des deux PC ;
- mondes jetables identifiés et protégés ;
- import ciblé dans un nouveau `Standard-X` ;
- règle joueur local = ID 0 ;
- conservation inventaires/équipements/positions sur le cycle testé ;
- cycle réel `Steven → Bob → Steven` réussi ;
- fonctionnement en ligne avec Xbox Cloud sans conflit visible sur les essais ;
- portabilité d'une sauvegarde réelle supplémentaire confirmée au niveau payload/slot ID 0.

Encore requis avant activation générale du feature gate :

- deux cycles A → B → A supplémentaires reproductibles ;
- scénario incluant redémarrage Windows dans la séquence validée ;
- stratégie documentée lorsqu'un vrai dialogue Local/Cloud apparaît ;
- intégration client/service testée de bout en bout avec rollback ;
- revue finale des logs et du comportement de reprise après interruption.

Le code pilote peut donc être consolidé, mais `GSH_ALLOW_HOST_TRANSFER` reste à `false` par défaut.
