using DotNetIALabs.Evaluation;
using System;
using System.Collections.Generic;
using System.Text;


public static class EquipmentRulesEvaluationCatalog
{
    public static IReadOnlyList<EquipmentRulesEvaluationCase> All { get; } =
    [
        new(
            "EQ-01",
            "Quelle est la périodicité d'étalonnage " +
            "d'un équipement critique ?",
            EquipmentRulesEvaluationKind.Precise,
            [1]),

        new(
            "EQ-02",
            "À quelle fréquence faut-il calibrer un appareil critique ?",
            EquipmentRulesEvaluationKind.Precise,
            [1]),

        new(
            "EQ-03",
            "Tous les combien de temps doit-on étalonner " +
            "un équipement critique ?",
            EquipmentRulesEvaluationKind.Precise,
            [1]),

        new(
            "EQ-04",
            "Quand faut-il effectuer un contrôle intermédiaire ?",
            EquipmentRulesEvaluationKind.Precise,
            [2]),

        new(
            "EQ-05",
            "À quelle fréquence contrôler un équipement critique " +
            "entre deux étalonnages ?",
            EquipmentRulesEvaluationKind.Precise,
            [2]),

        new(
            "EQ-06",
            "Quand un équipement est-il déclaré conforme " +
            "après étalonnage ?",
            EquipmentRulesEvaluationKind.Precise,
            [3]),

        new(
            "EQ-07",
            "Comment déterminer la conformité avec l'erreur, " +
            "l'incertitude et l'EMT ?",
            EquipmentRulesEvaluationKind.Precise,
            [3]),

        new(
            "EQ-08",
            "Dans quel cas un équipement est-il déclassé ?",
            EquipmentRulesEvaluationKind.Precise,
            [4]),

        new(
            "EQ-09",
            "frequence etalonage equipement critique",
            EquipmentRulesEvaluationKind.Precise,
            [1]),

        new(
            "EQ-10",
            "controle intermediaire tous les combien",
            EquipmentRulesEvaluationKind.Precise,
            [2]),

        new(
            "EQ-11",
            "conformite apres etalonage erreur incertitude EMT",
            EquipmentRulesEvaluationKind.Precise,
            [3]),

        new(
            "EQ-12",
            "equipement declasse degradation caracteristiques",
            EquipmentRulesEvaluationKind.Precise,
            [4]),

        new(
            "EQ-13",
            "Quels contrôles faut-il réaliser sur un équipement critique ?",
            EquipmentRulesEvaluationKind.Ambiguous,
            [1, 2]),

        new(
            "EQ-14",
            "Quelles sont les périodicités applicables " +
            "aux équipements critiques ?",
            EquipmentRulesEvaluationKind.Ambiguous,
            [1, 2]),

        new(
            "EQ-15",
            "Que faut-il faire après un étalonnage ?",
            EquipmentRulesEvaluationKind.Ambiguous,
            [1, 3]),

        new(
            "EQ-16",
            "Parlez-moi du contrôle et de la conformité d'un équipement",
            EquipmentRulesEvaluationKind.Ambiguous,
            [2, 3]),

         new(
            "EQ-16-bis",
            "Quelles sont les périodicités des équipements ?",
            EquipmentRulesEvaluationKind.Ambiguous,
            [1, 2]),

        new(
            "EQ-17",
            "Quelle est la durée de garantie de l'équipement ?",
            EquipmentRulesEvaluationKind.OutOfDomain,
            []),

        new(
            "EQ-18",
            "Où acheter un nouvel équipement de mesure ?",
            EquipmentRulesEvaluationKind.OutOfDomain,
            []),

        new(
            "EQ-19",
            "Quel temps fera-t-il demain ?",
            EquipmentRulesEvaluationKind.OutOfDomain,
            []),

        new(
            "EQ-20",
            "Comment réserver une salle de réunion ?",
            EquipmentRulesEvaluationKind.OutOfDomain,
            []),

         new(
            "EQ-21",
            "Quelle est la fréquence de maintenance préventive des équipements  ?",
            EquipmentRulesEvaluationKind.OutOfDomain,
            [])
    ];
}
