using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetIALabs.Data
{
    public sealed record EquipmentRuleSeed(    
        int Key,
        string Name,
        string Rule
    );

    public sealed class EquipmentRulesRegister
    {
        public static IReadOnlyList<EquipmentRuleSeed> All { get; } =
        Array.AsReadOnly<EquipmentRuleSeed>(
        [
            new(
                1,
                "Périodicité étalonnage",
                "L'étalonnage des équipements critiques doit se faire au moins une fois chaque deux ans"),
            new(
                2,
                "Périodicité contrôle intermédiaire",
                "Le contrôle intermédiaire des équipements critiques doit se faire au moins mensuellement"),
            new(
                3,
                "Déclaration conformité",
                "Suite à un étalonnage, un équipement est déclaré conforme si l'erreur d'ajustage plus l'incertitude est inférieure à l'EMT"),
            new(
                4,
                "Déclassement",
                "un équipement est déclassé suite à la dégradation de certains de ces caractéristiques métrologiques.")
        ]);
    }
}
