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
    public sealed class VectorSearchLab (
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<AiOptions> aiOptions)
    {
        public async Task RunSearchAzureServicesAsync(CancellationToken cancellationToken)
        {
            int dimensions = GetActiveEmbeddingDimensions(aiOptions.Value);
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
            int dimensions = GetActiveEmbeddingDimensions(aiOptions.Value);
            VectorStoreCollectionDefinition definition = CreateEquipmentRulesDefinition(dimensions);

            InMemoryVectorStore vectoreStore = new();

            VectorStoreCollection<int, EquipmentRulesRecord> rules =
               vectoreStore.GetCollection<int, EquipmentRulesRecord>(
                   "EquipmentRules",
                   definition
                   );

            await rules.EnsureCollectionExistsAsync(cancellationToken);

            foreach (EquipmentRuleSeed seed in EquipmentRulesRegister.All)
            {
                ReadOnlyMemory<float> vector = await GenerateVectorAsync(
                   seed.Rule,
                   dimensions,
                   cancellationToken);

                EquipmentRulesRecord record = new()
                {
                    Key = seed.Key,
                    Name = seed.Name,
                    Rule = seed.Rule,
                    Embedding = vector,
                };

                await rules.UpsertAsync(record, cancellationToken);
            }

            while (!cancellationToken.IsCancellationRequested)
            {

                Console.WriteLine("Veuillez entrer votre prompt :");

                string? prompt = await Console.In.ReadLineAsync(cancellationToken);

                if ((prompt is null))
                {
                    return;
                }

                if (prompt is null
                    || prompt.Trim().Equals(
                        "bye",
                        StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                ReadOnlyMemory<float> promptEmbedding = await GenerateVectorAsync(
                    prompt,
                    dimensions,
                    cancellationToken);

                Console.WriteLine($"Prompt : {prompt}");
                Console.WriteLine("Résultats de la recherche vectorielle :");

                await foreach (VectorSearchResult<EquipmentRulesRecord> result in
                    rules.SearchAsync(
                        promptEmbedding,
                        top: 1,
                        cancellationToken: cancellationToken))
                {
                    Console.WriteLine($"- {result.Record.Name}");
                    Console.WriteLine($"  Score : {result.Score:F4}");
                    Console.WriteLine($"  {result.Record.Rule}");
                }
            }

        }

        private async Task<ReadOnlyMemory<float>> GenerateVectorAsync(
        string text,
        int expectedDimensions,
        CancellationToken cancellationToken)
        {
            GeneratedEmbeddings<Embedding<float>> embeddings =
                await embeddingGenerator.GenerateAsync(
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

    }
}
