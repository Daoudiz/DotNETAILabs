using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetIALabs.Evaluation
{
    public sealed record EquipmentRulesEvaluationReport(
      IReadOnlyList<EquipmentRulesEvaluationResult> Results,
      double Top1Accuracy,
      double RecallAt3,
      double OutOfDomainRejectionRate,
      double AmbiguityDetectionRate,
      double FalseAmbiguityRate,
      double? MinimumRelevantScore,
      double? MaximumOutOfDomainScore,
      bool Passed);
}
