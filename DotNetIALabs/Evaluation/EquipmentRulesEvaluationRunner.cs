using DotNetIALabs.Data;
using DotNetIALabs.Models;
using DotNetIALabs.Presentation.Labs;

namespace DotNetIALabs.Evaluation;

public sealed class EquipmentRulesEvaluationRunner(
    VectorSearchLab vectorSearchLab)
{
    private const double MinimumTop1Accuracy = 0.95;
    private const double MinimumRecallAt3 = 1.00;
    private const double MinimumOutOfDomainRejectionRate = 1.00;
    private const double MinimumAmbiguityDetectionRate = 0.80;
    private const double MaximumFalseAmbiguityRate = 0.10;

    public async Task<EquipmentRulesEvaluationReport> RunAsync(
        CancellationToken cancellationToken)
    {
        List<EquipmentRulesEvaluationResult> results = [];

        foreach (EquipmentRulesEvaluationCase testCase in
            EquipmentRulesEvaluationCatalog.All)
        {
            EquipmentRulesSearchDecision decision =
                await vectorSearchLab.SearchEquipmentRulesAsync(
                    testCase.Question,
                    cancellationToken);

            results.Add(Evaluate(testCase, decision));
        }

        EquipmentRulesEvaluationReport report = BuildReport(results);
        WriteReport(report);

        return report;
    }

    private static EquipmentRulesEvaluationResult Evaluate(
        EquipmentRulesEvaluationCase testCase,
        EquipmentRulesSearchDecision decision)
    {
        int? firstKey = decision.Candidates.Count > 0
            ? decision.Candidates[0].Record.Key
            : null;

        bool top1Correct = testCase.ExpectedRuleKeys.Count > 0
            && firstKey == testCase.ExpectedRuleKeys[0];

        bool expectedRulesInTop3 = testCase.ExpectedRuleKeys.All(
            expectedKey => decision.Candidates
                .Take(3)
                .Any(candidate =>
                    candidate.Record.Key == expectedKey));

        (bool passed, string reason) = testCase.Kind switch
        {
            EquipmentRulesEvaluationKind.Precise =>
                EvaluatePrecise(
                    decision,
                    top1Correct,
                    expectedRulesInTop3),

            EquipmentRulesEvaluationKind.Ambiguous =>
                EvaluateAmbiguous(
                    decision,
                    expectedRulesInTop3),

            EquipmentRulesEvaluationKind.OutOfDomain =>
                EvaluateOutOfDomain(decision),

            _ => (false, "Type de test inconnu.")
        };

        return new EquipmentRulesEvaluationResult(
            testCase,
            decision,
            top1Correct,
            expectedRulesInTop3,
            passed,
            reason);
    }

    private static (bool Passed, string Reason) EvaluatePrecise(
        EquipmentRulesSearchDecision decision,
        bool top1Correct,
        bool expectedRuleInTop3)
    {
        if (decision.Outcome == EquipmentRulesSearchOutcome.NoMatch)
        {
            return (false, "Question pertinente rejetée.");
        }

        if (!expectedRuleInTop3)
        {
            return (false, "Règle attendue absente du top 3.");
        }

        if (!top1Correct)
        {
            return (false, "Règle attendue absente de la première position.");
        }

        return (true, string.Empty);
    }

    private static (bool Passed, string Reason) EvaluateAmbiguous(
        EquipmentRulesSearchDecision decision,
        bool expectedRulesInTop3)
    {
        if (!expectedRulesInTop3)
        {
            return (false, "Règles attendues absentes du top 3.");
        }

        if (decision.Outcome != EquipmentRulesSearchOutcome.Ambiguous)
        {
            return (false, "Ambiguïté non détectée.");
        }

        return (true, string.Empty);
    }

    private static (bool Passed, string Reason) EvaluateOutOfDomain(
        EquipmentRulesSearchDecision decision) =>
        decision.Outcome == EquipmentRulesSearchOutcome.NoMatch
            ? (true, string.Empty)
            : (false, "Question hors domaine acceptée.");

    // BuildReport et WriteReport sont détaillées dans les sections suivantes.
    private static EquipmentRulesEvaluationReport BuildReport(
    IReadOnlyList<EquipmentRulesEvaluationResult> results)
    {
        EquipmentRulesEvaluationResult[] precise = results
            .Where(result =>
                result.TestCase.Kind == EquipmentRulesEvaluationKind.Precise)
            .ToArray();

        EquipmentRulesEvaluationResult[] ambiguous = results
            .Where(result =>
                result.TestCase.Kind == EquipmentRulesEvaluationKind.Ambiguous)
            .ToArray();

        EquipmentRulesEvaluationResult[] outOfDomain = results
            .Where(result =>
                result.TestCase.Kind == EquipmentRulesEvaluationKind.OutOfDomain)
            .ToArray();

        double top1Accuracy = Ratio(
            precise.Count(result => result.Top1Correct),
            precise.Length);

        double recallAt3 = Ratio(
            precise.Count(result => result.ExpectedRulesInTop3),
            precise.Length);

        double outOfDomainRejectionRate = Ratio(
            outOfDomain.Count(result => result.Passed),
            outOfDomain.Length);

        double ambiguityDetectionRate = Ratio(
            ambiguous.Count(result =>
                result.Decision.Outcome ==
                    EquipmentRulesSearchOutcome.Ambiguous),
            ambiguous.Length);

        double falseAmbiguityRate = Ratio(
            precise.Count(result =>
                result.Decision.Outcome ==
                    EquipmentRulesSearchOutcome.Ambiguous),
            precise.Length);

        double? minimumRelevantScore = precise
            .Where(result => result.FirstScore.HasValue)
            .Select(result => result.FirstScore!.Value)
            .DefaultIfEmpty()
            .Min();

        double? maximumOutOfDomainScore = outOfDomain
            .Where(result => result.FirstScore.HasValue)
            .Select(result => result.FirstScore!.Value)
            .DefaultIfEmpty()
            .Max();

        bool passed =
            top1Accuracy >= MinimumTop1Accuracy
            && recallAt3 >= MinimumRecallAt3
            && outOfDomainRejectionRate >=
                MinimumOutOfDomainRejectionRate
            && ambiguityDetectionRate >=
                MinimumAmbiguityDetectionRate
            && falseAmbiguityRate <= MaximumFalseAmbiguityRate;

        return new EquipmentRulesEvaluationReport(
            results,
            top1Accuracy,
            recallAt3,
            outOfDomainRejectionRate,
            ambiguityDetectionRate,
            falseAmbiguityRate,
            minimumRelevantScore,
            maximumOutOfDomainScore,
            passed);
    }

    private static double Ratio(int numerator, int denominator) =>
        denominator == 0
            ? 0
            : (double)numerator / denominator;

    private static void WriteReport(
    EquipmentRulesEvaluationReport report)
    {
        Console.WriteLine();
        Console.WriteLine("Évaluation de la recherche des règles");
        Console.WriteLine();

        foreach (EquipmentRulesEvaluationResult result in report.Results)
        {
            string status = result.Passed ? "RÉUSSI" : "ÉCHOUÉ";
            string candidates = string.Join(
                " | ",
                result.Decision.Candidates.Select(candidate =>
                    $"{candidate.Record.Name}:{candidate.Score:F4}"));

            Console.WriteLine(
                $"{result.TestCase.Id} [{status}] {candidates}");

            if (!result.Passed)
            {
                Console.WriteLine($"  {result.FailureReason}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(
            $"Précision top 1 : {report.Top1Accuracy:P1}");
        Console.WriteLine(
            $"Rappel top 3 : {report.RecallAt3:P1}");
        Console.WriteLine(
            "Rejet hors domaine : " +
            $"{report.OutOfDomainRejectionRate:P1}");
        Console.WriteLine(
            "Détection des ambiguïtés : " +
            $"{report.AmbiguityDetectionRate:P1}");
        Console.WriteLine(
            "Fausses ambiguïtés : " +
            $"{report.FalseAmbiguityRate:P1}");

        if (report.MinimumRelevantScore is double minimumRelevant)
        {
            Console.WriteLine(
                $"Score pertinent minimal : {minimumRelevant:F4}");
        }

        if (report.MaximumOutOfDomainScore is double maximumOutOfDomain)
        {
            Console.WriteLine(
                $"Score hors domaine maximal : {maximumOutOfDomain:F4}");
        }

        Console.WriteLine();
        Console.WriteLine(
            report.Passed
                ? "Résultat global : RÉUSSI"
                : "Résultat global : ÉCHOUÉ");
    }

}
