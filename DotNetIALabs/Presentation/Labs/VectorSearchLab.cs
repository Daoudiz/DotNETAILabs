using CommunityToolkit.VectorData.InMemory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;
using DotNetIALabs.Configuration;
using DotNetIALabs.Data;
using DotNetIALabs.Models;
using System.Runtime.InteropServices;


namespace DotNetIALabs.Presentation.Labs
{
    public sealed class VectorSearchLab 
    {
        private const string EquipmentRulesCollectionName = "EquipmentRules";
        private const int EquipmentRulesCandidateCount = 3;
        private const double EquipmentRulesMinimumScore = 0.43;
        private const double EquipmentRulesAmbiguityDelta = 0.0085;

        private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
        private readonly AiOptions _aiOptions;
        private readonly int _embeddingDimensions;
        private readonly InMemoryVectorStore _vectorStore = new();
        private readonly VectorStoreCollection<int, EquipmentRulesRecord> _equipmentRules;
        private bool _equipmentRulesInitialized;

        

      
        public VectorSearchLab(
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
            IOptions<AiOptions> aiOptions)
        {
            _embeddingGenerator = embeddingGenerator;
            _aiOptions = aiOptions.Value;
            _embeddingDimensions = GetActiveEmbeddingDimensions(_aiOptions);

            _equipmentRules =
                _vectorStore.GetCollection<int, EquipmentRulesRecord>(
                    EquipmentRulesCollectionName,
                    CreateEquipmentRulesDefinition(_embeddingDimensions));
        }

        public async Task RunSearchAzureServicesAsync(CancellationToken cancellationToken)
        {
            int dimensions = GetActiveEmbeddingDimensions(_aiOptions);
            VectorStoreCollectionDefinition definition = CreateDefinition(dimensions);

            InMemoryVectorStore vectoreStore = new();

            VectorStoreCollection<int, CloudServiceRecord> services =
                vectoreStore.GetCollection<int, CloudServiceRecord>(
                    "CloudServices",
                    definition
                    );

            await services.EnsureCollectionExistsAsync(cancellationToken);

            foreach(CloudServiceSeed seed in CloudServicesCatalog.All)
            {
                ReadOnlyMemory<float> vector = await GenerateVectorAsync(
                    seed.Description,
                    dimensions,
                    cancellationToken);

                CloudServiceRecord record = new()
                {
                    Key = seed.Key,
                    Name = seed.Name,
                    Description = seed.Description,
                    Embedding = vector,
                };

                await services.UpsertAsync(record, cancellationToken);
            }

            const string prompt =
            "Quel service dois-je utiliser pour stocker des documents Word ?";

            ReadOnlyMemory<float> promptEmbedding = await GenerateVectorAsync(
                prompt,
                dimensions,
                cancellationToken);

            Console.WriteLine($"Prompt : {prompt}");
            Console.WriteLine("Résultats de la recherche vectorielle :");

            await foreach (VectorSearchResult<CloudServiceRecord> result in
                services.SearchAsync(
                    promptEmbedding,
                    top: 1,
                    cancellationToken: cancellationToken))
            {
                Console.WriteLine($"- {result.Record.Name}");
                Console.WriteLine($"  Score : {result.Score:F4}");
                Console.WriteLine($"  {result.Record.Description}");
            }

        }

        public async Task RunSearchEquipmentRulesAsync(CancellationToken cancellationToken)
        {
            //List<(string Prompt, List<EquipmentRuleMatch> Matches)> searchHistory = [];

            await EnsureEquipmentRulesIndexedAsync(cancellationToken);

            Console.WriteLine("Posez une question sur les règles d'équipement, " +
                    "ou saisissez 'bye' pour revenir au menu.");

            while (!cancellationToken.IsCancellationRequested)
            {

                Console.Write("Question : ");
                string? input = await Console.In.ReadLineAsync(cancellationToken);

                if (input is null)
                {
                    return;
                }

                string prompt = input.Trim();

                if (prompt.Equals("bye", StringComparison.OrdinalIgnoreCase))
                {
                    //Console.WriteLine("Historique des recherches :");

                    //for (int i = 0; i < searchHistory.Count; i++)
                    //{
                    //    (string searchedPrompt, List<EquipmentRuleMatch> searchedMatches) =
                    //        searchHistory[i];

                    //    Console.WriteLine($"{i + 1}. Question : {searchedPrompt}");

                    //    if (searchedMatches.Count == 0)
                    //    {
                    //        Console.WriteLine("   Aucun résultat suffisamment pertinent.");
                    //        continue;
                    //    }

                    //    foreach (EquipmentRuleMatch match in searchedMatches)
                    //    {
                    //        Console.WriteLine(
                    //            $"   - {match.Record.Name} | Score : {match.Score:F4}");
                    //    }
                    //}

                    break;
                }

                if(prompt.Length == 0)
                {
                    continue;
                }

                EquipmentRulesSearchDecision decision =  await SearchEquipmentRulesAsync(
                                                                    prompt,
                                                                    cancellationToken);

                switch (decision.Outcome)
                {
                    case EquipmentRulesSearchOutcome.NoMatch:
                        Console.WriteLine(
                            "Le registre ne contient pas de règle suffisamment pertinente " +
                            "pour cette question.");
                        break;

                    case EquipmentRulesSearchOutcome.Ambiguous:
                        Console.WriteLine(
                            "Plusieurs règles peuvent correspondre. " +
                            "Précisez votre demande :");

                        foreach (EquipmentRuleMatch candidate in
                            decision.Candidates.Take(2))
                        {
                            Console.WriteLine(
                                $"- {candidate.Record.Name} ({candidate.Score:F4})");
                        }
                        break;

                    case EquipmentRulesSearchOutcome.Match:
                        EquipmentRuleMatch bestMatch = decision.Candidates[0];

                        Console.WriteLine($"- {bestMatch.Record.Name}");
                        Console.WriteLine($"  Score : {bestMatch.Score:F4}");
                        Console.WriteLine($"  {bestMatch.Record.Rule}");
                        break;
                }

                //ReadOnlyMemory<float> promptEmbedding = await GenerateVectorAsync(
                //    prompt,
                //    _embeddingDimensions,
                //    cancellationToken);

                //List<EquipmentRuleMatch> matches = [];

                //await foreach (VectorSearchResult<EquipmentRulesRecord> result in
                //    _equipmentRules.SearchAsync(
                //        promptEmbedding,
                //        top: EquipmentRulesCandidateCount,
                //        cancellationToken: cancellationToken))
                //{




                //    if (result.Score >= EquipmentRulesMinimumScore)
                //    {
                //        matches.Add(new EquipmentRuleMatch(
                //            result.Record,
                //            result.Score));
                //    }                  
                //}

                //searchHistory.Add((prompt, matches));

                //if (matches.Count == 0)
                //{
                //    Console.WriteLine(
                //        "Le registre ne contient pas de règle suffisamment pertinente " +
                //        "pour cette question. Précisez votre demande.");
                //    continue;
                //}          

                //bool isAmbiguous = matches.Count >= 2
                //        && matches[0].Score - matches[1].Score
                //            <= EquipmentRulesAmbiguityDelta;

                //if (isAmbiguous)
                //{
                //    Console.WriteLine(
                //        "Plusieurs règles peuvent correspondre. " +
                //        "Précisez le sujet concerné :");

                //    foreach (EquipmentRuleMatch match in matches)
                //    {
                //        Console.WriteLine(
                //            $"- {match.Record.Name} (score : {match.Score:F4})");
                //    }

                //    continue;
                //}

                //Console.WriteLine("Règles pertinentes :");

                //foreach (EquipmentRuleMatch match in matches)
                //{
                //    Console.WriteLine($"- {match.Record.Name}");
                //    Console.WriteLine($"  Score : {match.Score:F4}");
                //    Console.WriteLine($"  {match.Record.Rule}");
                //}
            }

        }

        private async Task<ReadOnlyMemory<float>> GenerateVectorAsync(
        string text,
        int expectedDimensions,
        CancellationToken cancellationToken)
        {
            GeneratedEmbeddings<Embedding<float>> embeddings =
                await _embeddingGenerator.GenerateAsync(
                    [text],
                    cancellationToken: cancellationToken);

            if (embeddings.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Le fournisseur a retourné {embeddings.Count} embeddings au lieu de 1.");
            }

            ReadOnlyMemory<float> vector = embeddings[0].Vector;

            if (vector.Length != expectedDimensions)
            {
                throw new InvalidOperationException(
                    $"Le modèle d'embeddings a produit {vector.Length} dimensions, " +
                    $"mais la configuration en déclare {expectedDimensions}.");
            }

            return vector;
        }

        private static VectorStoreCollectionDefinition CreateDefinition(
                int dimensions) => new()
       {
            Properties =
            [
                new VectorStoreKeyProperty(
                    nameof(CloudServiceRecord.Key),
                    typeof(int)),
                new VectorStoreDataProperty(
                    nameof(CloudServiceRecord.Name),
                    typeof(string)),
                new VectorStoreDataProperty(
                    nameof(CloudServiceRecord.Description),
                    typeof(string)),
                new VectorStoreVectorProperty(
                    nameof(CloudServiceRecord.Embedding),
                    typeof(ReadOnlyMemory<float>),
                    dimensions)
                {
                    DistanceFunction = DistanceFunction.CosineSimilarity
                }
            ]
       };

        private static VectorStoreCollectionDefinition CreateEquipmentRulesDefinition(
            int dimensions) => new()
            {
                Properties =
            [
                new VectorStoreKeyProperty(
                    nameof(EquipmentRulesRecord.Key),
                    typeof(int)),
                new VectorStoreDataProperty(
                    nameof(EquipmentRulesRecord.Name),
                    typeof(string)),
                new VectorStoreDataProperty(
                    nameof(EquipmentRulesRecord.Rule),
                    typeof(string)),
                new VectorStoreVectorProperty(
                    nameof(EquipmentRulesRecord.Embedding),
                    typeof(ReadOnlyMemory<float>),
                    dimensions)
                {
                    DistanceFunction = DistanceFunction.CosineSimilarity
                }
            ]
            };

        private static int GetActiveEmbeddingDimensions(AiOptions options)
        {
            string provider = options.Provider.Trim();

            if (provider.Equals(
                AiOptions.OllamaProvider,
                StringComparison.OrdinalIgnoreCase))
            {
                return options.Providers.Ollama.EmbeddingDimensions;
            }

            if (provider.Equals(
                AiOptions.OpenAiProvider,
                StringComparison.OrdinalIgnoreCase))
            {
                return options.Providers.OpenAI.EmbeddingDimensions;
            }

            if (provider.Equals(
                AiOptions.AzureOpenAiProvider,
                StringComparison.OrdinalIgnoreCase))
            {
                return options.Providers.AzureOpenAI.EmbeddingDimensions;
            }

            throw new InvalidOperationException(
                $"Fournisseur AI inconnu : '{options.Provider}'.");
        }

        private static string BuildEquipmentRuleIndexText(
    EquipmentRuleSeed seed) =>
    $"Titre : {seed.Name}\nRègle : {seed.Rule}";

        private async Task EnsureEquipmentRulesIndexedAsync(
                                CancellationToken cancellationToken)
        {
            if (_equipmentRulesInitialized)
            {
                return;
            }

            await _equipmentRules.EnsureCollectionExistsAsync(cancellationToken);

            foreach (EquipmentRuleSeed seed in EquipmentRulesRegister.All)
            {
                ReadOnlyMemory<float> vector = await GenerateVectorAsync(
                    BuildEquipmentRuleIndexText(seed),
                    _embeddingDimensions,
                    cancellationToken);

                EquipmentRulesRecord record = new()
                {
                    Key = seed.Key,
                    Name = seed.Name,
                    Rule = seed.Rule,
                    Embedding = vector
                };

                await _equipmentRules.UpsertAsync(record, cancellationToken);
            }

            _equipmentRulesInitialized = true;
        }

        public async Task<EquipmentRulesSearchDecision>  SearchEquipmentRulesAsync(
                                                            string question,
                                                            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(question))
            {
                throw new ArgumentException(
                    "La question ne peut pas être null.",
                    nameof(question));
            }

            //Generate embedding for equipment rules
            await EnsureEquipmentRulesIndexedAsync(cancellationToken);

            //Generate embedding for user prompt
            ReadOnlyMemory<float> questionEmbedding = await GenerateVectorAsync(
                        question.Trim(),
                        _embeddingDimensions,
                        cancellationToken);

            List<EquipmentRuleMatch> candidates = [];

            await foreach (VectorSearchResult<EquipmentRulesRecord> result in
                _equipmentRules.SearchAsync(
                    questionEmbedding,
                    top: EquipmentRulesCandidateCount,
                    cancellationToken: cancellationToken))
            {
                if(result.Score is not double score)
                {
                    continue;
                }

                candidates.Add(new EquipmentRuleMatch(
                    result.Record,
                    score));
            }

            if (candidates.Count == 0 || candidates[0].Score < EquipmentRulesMinimumScore)
            {
                return new EquipmentRulesSearchDecision(
                    EquipmentRulesSearchOutcome.NoMatch,
                    candidates);
            }

            bool isAmbiguous = candidates.Count >= 2 && (candidates[0].Score - candidates[1].Score) <= EquipmentRulesAmbiguityDelta;            
           
            return new EquipmentRulesSearchDecision(
                isAmbiguous
                    ? EquipmentRulesSearchOutcome.Ambiguous
                    : EquipmentRulesSearchOutcome.Match,
                candidates);

        }

    }
}
