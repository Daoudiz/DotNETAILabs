using DotNetIALabs.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetIALabs.Evaluation
{
    public sealed record EquipmentRulesEvaluationResult(
    EquipmentRulesEvaluationCase TestCase,
    EquipmentRulesSearchDecision Decision,
    bool Top1Correct,
    bool ExpectedRulesInTop3,
    bool Passed,
    string FailureReason)
    {
        public double? FirstScore =>
            Decision.Candidates.Count > 0
                ? Decision.Candidates[0].Score
                : null;

        public double? SecondScore =>
            Decision.Candidates.Count > 1
                ? Decision.Candidates[1].Score
                : null;

        public double? ScoreDifference =>
            FirstScore.HasValue && SecondScore.HasValue
                ? FirstScore.Value - SecondScore.Value
                : null;
    }
}
