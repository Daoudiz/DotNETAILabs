using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetIALabs.Evaluation
{
    

    public enum EquipmentRulesEvaluationKind
    {
        Precise,
        Ambiguous,
        OutOfDomain
    }

    public sealed record EquipmentRulesEvaluationCase(
        string Id,
        string Question,
        EquipmentRulesEvaluationKind Kind,
        IReadOnlyList<int> ExpectedRuleKeys);
}
