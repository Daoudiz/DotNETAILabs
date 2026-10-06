using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetIALabs.Models
{
    public sealed record EquipmentRuleMatch(
            EquipmentRulesRecord Record,
            double Score);
    public enum EquipmentRulesSearchOutcome
    {
        Match,
        Ambiguous,
        NoMatch
    }

    public sealed record EquipmentRulesSearchDecision(
        EquipmentRulesSearchOutcome Outcome,
        IReadOnlyList<EquipmentRuleMatch> Candidates);

    public sealed class EquipmentRulesRecord
    {
        public required int Key { get; set; }
        public required string Name { get; set; }
        public required string Rule { get; set; }
        public required ReadOnlyMemory<float> Embedding { get; set; }
    }

   
}
