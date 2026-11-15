# Extraction du pipeline dans `IDataIngestionService`

## 1. Objectif

Les méthodes `RunDataIngestionPipeline` et `RunPdfDataIngestionPipeline` de
`RAGLabs` répètent aujourd'hui les mêmes opérations :

1. création des options du modèle de chat ;
2. création du tokenizer et du chunker sémantique ;
3. création de l'enrichisseur de résumés ;
4. création du `VectorStoreWriter` pour PostgreSQL/pgvector ;
5. composition du pipeline ;
6. exécution du pipeline et contrôle des résultats.

L'objectif est de déplacer cette configuration et cette exécution dans un
service réutilisable nommé `IDataIngestionService`. Les labs ne conserveront
que leurs responsabilités pédagogiques :

- choisir le lecteur adapté au format du document ;
- désigner le répertoire et le motif des fichiers ;
- appeler le service d'ingestion ;
- afficher le bilan ;
- exécuter la boucle de recherche et afficher ses résultats.

Cette proposition reste volontairement simple et monoprojet. Elle n'introduit
ni couche métier artificielle, ni repository générique, ni Clean Architecture.

## 2. Limite de responsabilité

`IDataIngestionService` est responsable de :

- configurer le chunker et les enrichisseurs ;
- configurer `VectorStoreWriter` ;
- composer et exécuter `IngestionPipeline` ;
- propager le `CancellationToken` ;
- produire un bilan d'exécution ;
- gérer la durée de vie des objets techniques créés pour l'ingestion.

Il n'est pas responsable de :

- lire les questions dans la console ;
- exécuter une recherche vectorielle ;
- générer une réponse RAG ;
- connaître le menu ou l'interface utilisateur ;
- rechercher lui-même le dossier `Data` sur le disque.

La recherche reste dans le lab pendant cette première refactorisation. Elle
pourra ensuite être déplacée vers un service séparé, par exemple
`IVectorSearchService`, lorsqu'un deuxième consommateur en aura réellement
besoin.

## 3. Architecture cible

```text
DotNetIALabs/
|-- Configuration/
|   |-- AiOptions.cs
|   |-- DataIngestionOptions.cs                 # nouveau
|   `-- DataIngestionOptionsValidator.cs        # nouveau
|-- Infrastructure/
|   |-- DataIngestion/
|   |   |-- IDataIngestionService.cs            # nouveau contrat
|   |   |-- DataIngestionRequest.cs             # nouvelle entrée
|   |   |-- DataIngestionReport.cs              # nouveau bilan
|   |   |-- DataIngestionExecution.cs           # résultat + durée de vie
|   |   |-- DataIngestionService.cs             # implémentation
|   |   `-- SingleChunkSummaryEnricher.cs       # déplacé depuis RAGLabs
|   |-- DependencyInjection.cs                  # enregistrements DI
|   `-- PdfDocumentReader.cs
|-- Presentation/
|   `-- Labs/
|       `-- RAGLabs.cs                          # simplifié
`-- appsettings.json                            # section DataIngestion
```

Flux d'appel :

```text
RAGLabs
  | choisit MarkdownReader ou PdfDocumentReader
  | construit DataIngestionRequest
  v
IDataIngestionService
  | configure enrichisseurs + chunker + writer
  | exécute IngestionPipeline
  v
PostgreSQL / pgvector
  |
  `-> DataIngestionExecution
        |-- Report : bilan des documents
        `-- Collection : collection utilisée ensuite par le lab pour rechercher
```

`DataIngestionExecution` est volontairement `IDisposable`. Dans le lab actuel,
la collection créée par le writer est utilisée immédiatement après l'ingestion
pour la recherche. Cet objet garde donc le pipeline et son writer en vie jusqu'à
la fin de la boucle de recherche. Il permet d'éviter :

- de retourner un writer qui serait déjà détruit ;
- de donner la recherche comme responsabilité au service d'ingestion ;
- de reconstruire ailleurs la définition dynamique de la collection.

Dans une future Web API, l'ingestion et la recherche devront être deux flux
indépendants. La recherche rouvrira alors une collection explicitement typée à
partir de `VectorStore`, sans dépendre d'une `DataIngestionExecution`.

## 4. Configuration

### 4.1 `DataIngestionOptions`

Créer `Configuration/DataIngestionOptions.cs` :

```csharp
namespace DotNetIALabs.Configuration;

public sealed class DataIngestionOptions
{
    public const string SectionName = "DataIngestion";

    public string CollectionName { get; init; } = "data-with-summary-v1";
    public string TokenizerEncoding { get; init; } = "o200k_base";
    public int MaxTokensPerChunk { get; init; } = 400;
    public int OverlapTokens { get; init; }
    public bool EnableSummaries { get; init; } = true;
    public bool IncrementalIngestion { get; init; } = true;
}
```

Cette classe ne contient aucune clé API ni chaîne de connexion. Les paramètres
du fournisseur IA restent dans la section `AI`, et la chaîne de connexion reste
dans `ConnectionStrings:VectorStore`.

### 4.2 `appsettings.json`

Ajouter la section suivante sans modifier la section `AI` :

```json
{
  "DataIngestion": {
    "CollectionName": "data-with-summary-v1",
    "TokenizerEncoding": "o200k_base",
    "MaxTokensPerChunk": 400,
    "OverlapTokens": 0,
    "EnableSummaries": true,
    "IncrementalIngestion": true
  }
}
```

La chaîne PostgreSQL peut rester dans `appsettings.json` pour le lab local si
c'est le choix pédagogique retenu, mais un mot de passe réel ne doit pas être
commité. Pour un dépôt partagé, utiliser User Secrets ou une variable telle que
`ConnectionStrings__VectorStore`.

### 4.3 Validation des options

Créer `Configuration/DataIngestionOptionsValidator.cs` :

```csharp
using Microsoft.Extensions.Options;

namespace DotNetIALabs.Configuration;

public sealed class DataIngestionOptionsValidator
    : IValidateOptions<DataIngestionOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        DataIngestionOptions options)
    {
        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(options.CollectionName))
        {
            errors.Add("DataIngestion:CollectionName est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(options.TokenizerEncoding))
        {
            errors.Add("DataIngestion:TokenizerEncoding est obligatoire.");
        }

        if (options.MaxTokensPerChunk <= 0)
        {
            errors.Add("DataIngestion:MaxTokensPerChunk doit être supérieur à 0.");
        }

        if (options.OverlapTokens < 0)
        {
            errors.Add("DataIngestion:OverlapTokens ne peut pas être négatif.");
        }

        if (options.OverlapTokens >= options.MaxTokensPerChunk)
        {
            errors.Add(
                "DataIngestion:OverlapTokens doit être inférieur à " +
                "MaxTokensPerChunk.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
```

Ce validateur contrôle uniquement la cohérence locale de la configuration. Il
ne doit appeler ni Ollama, ni PostgreSQL, ni un autre service réseau.

## 5. Contrats du service

### 5.1 Requête d'ingestion

Créer `Infrastructure/DataIngestion/DataIngestionRequest.cs` :

```csharp
using Microsoft.Extensions.DataIngestion;

namespace DotNetIALabs.Infrastructure.DataIngestion;

public sealed record DataIngestionRequest(
    IngestionDocumentReader DocumentReader,
    DirectoryInfo Repository,
    string SearchPattern,
    string DocumentType,
    bool EnableImageAlternativeText = false,
    bool StopOnFirstError = true);
```

La requête contient ce qui change d'un lab à l'autre :

- `DocumentReader` : `MarkdownReader`, `PdfDocumentReader`, ou un futur lecteur ;
- `Repository` : répertoire à parcourir ;
- `SearchPattern` : `*.md`, `*.pdf`, etc. ;
- `DocumentType` : libellé utilisé dans les messages ;
- `EnableImageAlternativeText` : active l'enrichisseur d'images lorsque le
  lecteur produit réellement des éléments image ;
- `StopOnFirstError` : permet au lab d'échouer immédiatement ou de produire un
  bilan contenant plusieurs échecs.

Le service ne contient donc aucun `if` sur l'extension du fichier. L'ajout d'un
nouveau format consiste principalement à fournir un nouveau
`IngestionDocumentReader`.

### 5.2 Bilan par document et bilan global

Créer `Infrastructure/DataIngestion/DataIngestionReport.cs` :

```csharp
namespace DotNetIALabs.Infrastructure.DataIngestion;

public sealed record DocumentIngestionStatus(
    string DocumentId,
    bool Succeeded);

public sealed class DataIngestionReport
{
    public required string CollectionName { get; init; }
    public required IReadOnlyList<DocumentIngestionStatus> Documents { get; init; }

    public int ProcessedCount => Documents.Count;
    public int SucceededCount => Documents.Count(document => document.Succeeded);
    public int FailedCount => Documents.Count(document => !document.Succeeded);
    public bool Succeeded => ProcessedCount > 0 && FailedCount == 0;
}
```

Le rapport ne transporte pas les exceptions techniques : celles-ci sont déjà
écrites par le pipeline dans les logs. Il expose seulement les informations
nécessaires au lab pour afficher un bilan et choisir son code de sortie.

### 5.3 Contrat `IDataIngestionService`

Créer `Infrastructure/DataIngestion/IDataIngestionService.cs` :

```csharp
namespace DotNetIALabs.Infrastructure.DataIngestion;

public interface IDataIngestionService
{
    Task<DataIngestionExecution> IngestAsync(
        DataIngestionRequest request,
        CancellationToken cancellationToken = default);
}
```

Le contrat est asynchrone et propage le `CancellationToken`. Il ne contient pas
de méthode `SearchAsync`, car la recherche est une autre responsabilité.

### 5.4 Résultat possédant les ressources techniques

Créer `Infrastructure/DataIngestion/DataIngestionExecution.cs` :

```csharp
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.VectorData;

namespace DotNetIALabs.Infrastructure.DataIngestion;

public sealed class DataIngestionExecution : IDisposable
{
    private readonly IngestionPipeline<string> _pipeline;
    private readonly VectorStoreWriter<string> _writer;
    private bool _disposed;

    internal DataIngestionExecution(
        DataIngestionReport report,
        IngestionPipeline<string> pipeline,
        VectorStoreWriter<string> writer)
    {
        Report = report;
        _pipeline = pipeline;
        _writer = writer;
    }

    public DataIngestionReport Report { get; }

    public VectorStoreCollection<object, Dictionary<string, object?>> Collection
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _writer.VectorStoreCollection;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Même ordre que deux déclarations using : le dernier créé est libéré
        // en premier. Les implémentations concernées supportent Dispose répété.
        _pipeline.Dispose();
        _writer.Dispose();
        _disposed = true;
    }
}
```

Le consommateur doit toujours employer une déclaration `using` :

```csharp
using DataIngestionExecution execution =
    await dataIngestionService.IngestAsync(request, cancellationToken);
```

La collection n'est utilisable que tant que `execution` n'est pas détruit.

> Remarque : ce type est un compromis adapté au lab actuel, qui enchaîne
> ingestion et recherche dans une seule méthode. Quand ces deux flux seront
> séparés, `IDataIngestionService` pourra retourner uniquement
> `DataIngestionReport`, et un futur `IVectorSearchService` ouvrira lui-même la
> collection persistée.

## 6. Enrichisseur de résumés

La classe imbriquée `SingleChunkSummaryEnricher` doit sortir de `RAGLabs`, car
elle appartient désormais à la configuration interne du pipeline.

Créer
`Infrastructure/DataIngestion/SingleChunkSummaryEnricher.cs` :

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace DotNetIALabs.Infrastructure.DataIngestion;

internal sealed class SingleChunkSummaryEnricher(
    IChatClient chatClient,
    ChatOptions chatOptions,
    ILogger<SingleChunkSummaryEnricher> logger)
    : IngestionChunkProcessor<string>
{
    public override async IAsyncEnumerable<IngestionChunk<string>> ProcessAsync(
        IAsyncEnumerable<IngestionChunk<string>> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (IngestionChunk<string> chunk in
            chunks.WithCancellation(cancellationToken))
        {
            try
            {
                ChatResponse response = await chatClient.GetResponseAsync(
                    $"""
                    Summarize the following text in one concise paragraph.
                    Return only the summary text, without JSON, a list, or commentary.

                    {chunk.Content}
                    """,
                    chatOptions,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(response.Text))
                {
                    chunk.Metadata["summary"] = response.Text.Trim();
                }
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException)
            {
                logger.LogWarning(
                    exception,
                    "The summary could not be generated for one chunk.");
            }

            yield return chunk;
        }
    }
}
```

Cette implémentation produit exactement un résumé par chunk et évite l'erreur
rencontrée avec un enrichisseur qui attendait un nombre différent de réponses.

## 7. Implémentation de `DataIngestionService`

Créer `Infrastructure/DataIngestion/DataIngestionService.cs` :

```csharp
using DotNetIALabs.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;
using Microsoft.ML.Tokenizers;

namespace DotNetIALabs.Infrastructure.DataIngestion;

internal sealed class DataIngestionService(
    IChatClient chatClient,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    VectorStore vectorStore,
    IOptions<AiOptions> aiOptions,
    IOptions<DataIngestionOptions> ingestionOptions,
    ILoggerFactory loggerFactory,
    ILogger<DataIngestionService> logger)
    : IDataIngestionService
{
    public async Task<DataIngestionExecution> IngestAsync(
        DataIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();

        DataIngestionOptions options = ingestionOptions.Value;

        ChatOptions enrichmentChatOptions = new()
        {
            Temperature = 0,
            Seed = 42,
            MaxOutputTokens = aiOptions.Value.Generation.MaxOutputTokens
        };

        Tokenizer tokenizer = TiktokenTokenizer.CreateForEncoding(
            options.TokenizerEncoding);

        IngestionChunkerOptions chunkerOptions = new(tokenizer)
        {
            MaxTokensPerChunk = options.MaxTokensPerChunk,
            OverlapTokens = options.OverlapTokens
        };

        IngestionChunker<string> chunker = new SemanticSimilarityChunker(
            embeddingGenerator,
            chunkerOptions);

        VectorStoreWriter<string> writer = new(
            vectorStore,
            dimensionCount: Helpers.GetActiveEmbeddingDimensions(aiOptions.Value),
            new VectorStoreWriterOptions
            {
                CollectionName = options.CollectionName,
                DistanceFunction = DistanceFunction.CosineDistance,
                IndexKind = IndexKind.Hnsw,
                IncrementalIngestion = options.IncrementalIngestion
            });

        IngestionPipeline<string>? pipeline = null;

        try
        {
            pipeline = new IngestionPipeline<string>(
                request.DocumentReader,
                chunker,
                writer,
                loggerFactory: loggerFactory);

            ConfigureDocumentProcessors(
                pipeline,
                request,
                enrichmentChatOptions);

            ConfigureChunkProcessors(
                pipeline,
                options,
                enrichmentChatOptions);

            List<DocumentIngestionStatus> documents = [];

            logger.LogInformation(
                "Reading {DocumentType} documents from {Repository}.",
                request.DocumentType,
                request.Repository.FullName);

            await foreach (IngestionResult result in pipeline.ProcessAsync(
                request.Repository,
                request.SearchPattern,
                cancellationToken: cancellationToken))
            {
                documents.Add(new DocumentIngestionStatus(
                    result.DocumentId,
                    result.Succeeded));

                logger.LogInformation(
                    "Completed processing {DocumentId}. Succeeded: {Succeeded}.",
                    result.DocumentId,
                    result.Succeeded);

                if (!result.Succeeded && request.StopOnFirstError)
                {
                    throw new InvalidOperationException(
                        $"Ingestion failed for '{result.DocumentId}'. " +
                        "Check the preceding error in the logs.");
                }
            }

            if (documents.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No {request.DocumentType} document matching " +
                    $"'{request.SearchPattern}' was found in " +
                    $"'{request.Repository.FullName}'.");
            }

            DataIngestionReport report = new()
            {
                CollectionName = options.CollectionName,
                Documents = documents
            };

            // La propriété de pipeline et writer est transférée à execution.
            // Il ne faut plus les libérer dans cette branche.
            return new DataIngestionExecution(report, pipeline, writer);
        }
        catch
        {
            pipeline?.Dispose();
            writer.Dispose();
            throw;
        }
    }

    private void ConfigureDocumentProcessors(
        IngestionPipeline<string> pipeline,
        DataIngestionRequest request,
        ChatOptions chatOptions)
    {
        if (!request.EnableImageAlternativeText)
        {
            return;
        }

        EnricherOptions enricherOptions = new(chatClient)
        {
            ChatOptions = chatOptions,
            LoggerFactory = loggerFactory
        };

        pipeline.DocumentProcessors.Add(
            new ImageAlternativeTextEnricher(enricherOptions));
    }

    private void ConfigureChunkProcessors(
        IngestionPipeline<string> pipeline,
        DataIngestionOptions options,
        ChatOptions chatOptions)
    {
        if (!options.EnableSummaries)
        {
            return;
        }

        pipeline.ChunkProcessors.Add(
            new SingleChunkSummaryEnricher(
                chatClient,
                chatOptions,
                loggerFactory.CreateLogger<SingleChunkSummaryEnricher>()));
    }

    private static void ValidateRequest(DataIngestionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.DocumentReader);
        ArgumentNullException.ThrowIfNull(request.Repository);

        if (!request.Repository.Exists)
        {
            throw new DirectoryNotFoundException(
                $"The repository '{request.Repository.FullName}' does not exist.");
        }

        if (string.IsNullOrWhiteSpace(request.SearchPattern))
        {
            throw new ArgumentException(
                "A search pattern is required.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.DocumentType))
        {
            throw new ArgumentException(
                "A document type is required.",
                nameof(request));
        }
    }
}
```

### Points importants de cette implémentation

- `ILoggerFactory` vient du Generic Host. Le service ne crée plus un second
  `LoggerFactory` avec `LoggerFactory.Create(...)`.
- Les clients `IChatClient`, `IEmbeddingGenerator` et `VectorStore` sont injectés.
- Le service est sans état partagé : chaque appel crée son chunker, ses
  enrichisseurs, son writer et son pipeline.
- Si la construction ou l'exécution échoue, le bloc `catch` libère les objets
  déjà créés.
- En cas de succès, leur propriété est transférée à
  `DataIngestionExecution`, que le lab doit libérer.
- L'annulation n'est jamais absorbée. `OperationCanceledException` remonte au
  runner.
- `IncrementalIngestion = true` permet au writer de mettre à jour les documents
  modifiés au lieu de créer aveuglément de nouveaux enregistrements. Cela ne
  remplace pas une séparation future entre une commande d'ingestion et une
  commande de question/réponse.

## 8. Configuration de l'injection de dépendances

Modifier `Infrastructure/DependencyInjection.cs`.

Ajouter les namespaces :

```csharp
using DotNetIALabs.Infrastructure.DataIngestion;
```

Ajouter la liaison et la validation des options, à proximité de `AiOptions` :

```csharp
services
    .AddOptions<DataIngestionOptions>()
    .Bind(configuration.GetSection(DataIngestionOptions.SectionName))
    .ValidateOnStart();

services.AddSingleton<
    IValidateOptions<DataIngestionOptions>,
    DataIngestionOptionsValidator>();
```

Puis enregistrer le service :

```csharp
services.AddSingleton<IDataIngestionService, DataIngestionService>();
```

L'enregistrement peut être placé après ceux de `IChatClient`, de
`IEmbeddingGenerator` et de `VectorStore`. L'ordre des lignes d'enregistrement
n'affecte pas la résolution, mais ce regroupement rend les dépendances plus
lisibles.

Le bloc concerné devient conceptuellement :

```csharp
services.AddSingleton<IValidateOptions<AiOptions>, AiOptionsValidator>();
services.AddSingleton<
    IValidateOptions<DataIngestionOptions>,
    DataIngestionOptionsValidator>();

services.AddSingleton<IChatClient>(CreateChatClient);
services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
    CreateEmbeddingGenerator);

services.AddPostgresVectorStore(
    _ => vectorStoreConnectionString,
    serviceProvider => new PostgresVectorStoreOptions
    {
        EmbeddingGenerator = serviceProvider.GetRequiredService<
            IEmbeddingGenerator<string, Embedding<float>>>()
    });

services.AddSingleton<IDataIngestionService, DataIngestionService>();
services.AddSingleton<PdfDocumentReader>();
services.AddSingleton<RAGLabs>();
```

Le service peut être singleton parce que ses dépendances techniques sont déjà
singleton et qu'il ne conserve aucun état propre à une exécution. Les objets
mutables du pipeline sont créés dans `IngestAsync` et ne sont pas partagés entre
deux appels.

## 9. Modification de `RAGLabs`

### 9.1 Constructeur simplifié

Après la refactorisation, `RAGLabs` n'a plus besoin de construire lui-même les
clients et le pipeline. Son constructeur peut devenir :

```csharp
using DotNetIALabs.Infrastructure;
using DotNetIALabs.Infrastructure.DataIngestion;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.VectorData;

namespace DotNetIALabs.Presentation.Labs;

public sealed class RAGLabs(
    IDataIngestionService dataIngestionService,
    PdfDocumentReader pdfDocumentReader)
{
    // Méthodes présentées ci-dessous.
}
```

Retirer du constructeur les dépendances qui ne sont plus utilisées directement :

- `IChatClient` ;
- `IOptions<AiOptions>` ;
- `IEmbeddingGenerator<string, Embedding<float>>` ;
- `VectorStore`.

Retirer également de `RAGLabs` :

- la constante `CollectionName` ;
- la classe imbriquée `SingleChunkSummaryEnricher` ;
- les blocs de configuration du logger, du chat, du chunker, du writer et du
  pipeline.

### 9.2 Lab Markdown

La méthode Markdown devient :

```csharp
public async Task RunDataIngestionPipeline(
    CancellationToken cancellationToken)
{
    DirectoryInfo repository = FindDataDirectory(
        searchPattern: "*.md",
        documentType: "Markdown");

    DataIngestionRequest request = new(
        DocumentReader: new MarkdownReader(),
        Repository: repository,
        SearchPattern: "*.md",
        DocumentType: "Markdown",
        EnableImageAlternativeText: true,
        StopOnFirstError: true);

    using DataIngestionExecution execution =
        await dataIngestionService.IngestAsync(request, cancellationToken);

    DisplayReport(execution.Report);
    await RunSearchLoopAsync(execution.Collection, cancellationToken);
}
```

`EnableImageAlternativeText` reste activé pour Markdown, car `MarkdownReader`
peut produire des éléments image. L'enrichisseur ne sera utile que si de tels
éléments sont effectivement présents.

### 9.3 Lab PDF

La méthode PDF devient :

```csharp
public async Task RunPdfDataIngestionPipeline(
    CancellationToken cancellationToken)
{
    DirectoryInfo repository = FindDataDirectory(
        searchPattern: "*.pdf",
        documentType: "PDF");

    DataIngestionRequest request = new(
        DocumentReader: pdfDocumentReader,
        Repository: repository,
        SearchPattern: "*.pdf",
        DocumentType: "PDF",
        EnableImageAlternativeText: false,
        StopOnFirstError: true);

    using DataIngestionExecution execution =
        await dataIngestionService.IngestAsync(request, cancellationToken);

    DisplayReport(execution.Report);
    await RunSearchLoopAsync(execution.Collection, cancellationToken);
}
```

L'enrichissement d'images reste désactivé dans cette version : le
`PdfDocumentReader` actuel basé sur PdfPig produit uniquement des paragraphes,
pas des `IngestionDocumentImage`. Activer l'enrichisseur sans produire
d'éléments image n'aurait aucun effet.

### 9.4 Mutualisation de l'affichage et de la recherche

Déplacer la boucle de recherche dupliquée dans une méthode privée de `RAGLabs` :

```csharp
private static void DisplayReport(DataIngestionReport report)
{
    Console.WriteLine(
        $"Ingestion completed in collection '{report.CollectionName}'. " +
        $"Processed: {report.ProcessedCount}; " +
        $"Succeeded: {report.SucceededCount}; " +
        $"Failed: {report.FailedCount}.");
}

private static async Task RunSearchLoopAsync(
    VectorStoreCollection<object, Dictionary<string, object?>> collection,
    CancellationToken cancellationToken)
{
    while (true)
    {
        Console.WriteLine("Enter your question (or 'exit' to quit):");

        string? searchValue = await Console.In.ReadLineAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(searchValue) ||
            searchValue.Equals("exit", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Console.WriteLine("Searching...\n");

        await foreach (
            VectorSearchResult<Dictionary<string, object?>> result in
            collection.SearchAsync(
                searchValue.Trim(),
                top: 3,
                cancellationToken: cancellationToken))
        {
            Console.WriteLine(
                $"Distance: {result.Score}\n" +
                $"\tcontent: {result.Record[\"content\"]}");
        }
    }
}
```

Le nom `Distance` est important : la collection est configurée avec
`CosineDistance`, donc la plus petite valeur correspond au résultat le plus
proche. Il ne faut pas présenter cette valeur comme un score de similarité où
la plus grande valeur serait nécessairement la meilleure.

La recherche reste ici dans `Presentation` pour limiter la portée de cette
refactorisation. Dès qu'elle sera utilisée par plusieurs labs ou par une API,
elle devra être déplacée dans un service distinct.

## 10. Ajout ultérieur d'un nouveau format

Le service ne doit pas être modifié pour chaque format. Il faut :

1. créer ou intégrer un `IngestionDocumentReader` pour le format ;
2. enregistrer ce lecteur dans l'injection de dépendances s'il a des dépendances ;
3. construire une `DataIngestionRequest` avec le lecteur et le motif adéquats ;
4. appeler le même `IDataIngestionService`.

Exemple conceptuel pour un futur lecteur DOCX :

```csharp
DataIngestionRequest request = new(
    DocumentReader: docxDocumentReader,
    Repository: repository,
    SearchPattern: "*.docx",
    DocumentType: "DOCX",
    EnableImageAlternativeText: true);

using DataIngestionExecution execution =
    await dataIngestionService.IngestAsync(request, cancellationToken);
```

Le lecteur est responsable de convertir le format source en
`IngestionDocument`, avec ses sections et ses éléments. Le service reste
responsable du traitement commun qui suit cette lecture.

## 11. Ordre d'implémentation recommandé

### Étape 1 — Ajouter la configuration

1. créer `DataIngestionOptions` ;
2. ajouter la section `DataIngestion` dans `appsettings.json` ;
3. créer `DataIngestionOptionsValidator` ;
4. lier et valider les options dans `DependencyInjection` ;
5. lancer l'application et vérifier qu'une valeur invalide provoque une erreur
   explicite au démarrage.

Cette étape ne modifie encore aucun lab.

### Étape 2 — Créer les objets d'entrée et de sortie

1. créer `DataIngestionRequest` ;
2. créer `DocumentIngestionStatus` et `DataIngestionReport` ;
3. créer `IDataIngestionService` ;
4. créer `DataIngestionExecution`.

Compiler avant de continuer afin d'isoler les erreurs de type ou de namespace.

### Étape 3 — Déplacer l'enrichisseur

1. copier `SingleChunkSummaryEnricher` dans son propre fichier ;
2. le rendre `internal` ;
3. ne supprimer la classe imbriquée de `RAGLabs` qu'après branchement du
   nouveau service.

### Étape 4 — Implémenter le service

1. injecter les clients, options, logs et `VectorStore` ;
2. déplacer la configuration commune depuis le lab ;
3. ajouter la validation de `DataIngestionRequest` ;
4. gérer le transfert de propriété vers `DataIngestionExecution` ;
5. enregistrer `IDataIngestionService` dans le conteneur DI ;
6. compiler.

### Étape 5 — Migrer uniquement le lab Markdown

1. injecter `IDataIngestionService` dans `RAGLabs` ;
2. remplacer la configuration du pipeline Markdown par la requête montrée plus
   haut ;
3. conserver temporairement le lab PDF tel quel ;
4. exécuter le lab Markdown ;
5. contrôler les lignes de la table pgvector et effectuer une recherche.

Cette migration incrémentale permet de comparer l'ancien et le nouveau flux.

### Étape 6 — Migrer le lab PDF

1. remplacer son pipeline par une `DataIngestionRequest` utilisant
   `PdfDocumentReader` ;
2. laisser l'enrichissement d'images désactivé tant que le lecteur ne produit
   pas d'images ;
3. exécuter avec un PDF textuel ;
4. vérifier que le message explicite concernant l'OCR apparaît encore pour un
   PDF scanné sans couche texte.

### Étape 7 — Supprimer la duplication restante

1. extraire `RunSearchLoopAsync` ;
2. extraire `DisplayReport` ;
3. supprimer l'ancien code commun et la classe imbriquée ;
4. nettoyer les `using` et les paramètres de constructeur inutilisés ;
5. compiler avec les avertissements activés.

### Étape 8 — Vérification complète

Exécuter au minimum :

```powershell
dotnet restore .\DotNetIALabs\DotNetIALabs.csproj
dotnet build .\DotNetIALabs\DotNetIALabs.csproj --no-restore
dotnet run --project .\DotNetIALabs\DotNetIALabs.csproj
```

Puis vérifier les scénarios fonctionnels listés ci-dessous.

## 12. Scénarios de vérification

### Démarrage et configuration

- la section `DataIngestion` absente ou invalide arrête l'application avec un
  message compréhensible ;
- seul le fournisseur IA actif est validé ;
- aucune clé API ni mot de passe n'est affiché dans les logs.

### Markdown

- un fichier `.md` est lu, chunké, résumé et écrit dans PostgreSQL ;
- un Markdown contenant une image déclenche l'enrichisseur d'alternative
  textuelle si le reader a produit un élément image ;
- une question retourne les chunks pertinents ;
- le résultat ayant la distance cosinus la plus faible est affiché en premier.

### PDF

- un PDF textuel est ingéré ;
- les numéros de page sont conservés par `PdfDocumentReader` ;
- un PDF scanné sans texte échoue avec le message indiquant que l'OCR est
  nécessaire ;
- aucune description d'image n'est prétendument générée tant que le lecteur ne
  produit pas d'éléments image.

### Gestion des erreurs

- un répertoire inexistant est rejeté avant la création du pipeline ;
- zéro fichier correspondant au motif produit une erreur claire ;
- avec `StopOnFirstError = true`, le premier échec interrompt le lab ;
- l'annulation par `CancellationToken` remonte sans être convertie en erreur
  fonctionnelle ;
- après une exception, le pipeline et le writer sont libérés.

### Ingestion incrémentale

- deux exécutions avec les mêmes documents ne doivent pas multiplier
  indéfiniment les chunks ;
- la modification d'un document doit actualiser les données correspondantes ;
- une modification du modèle d'embedding ou de sa dimension exige une nouvelle
  collection, par exemple `data-with-summary-v2`, puis une réingestion complète.

## 13. Points d'attention

### Cohérence de la dimension des embeddings

`Helpers.GetActiveEmbeddingDimensions(aiOptions.Value)` doit correspondre à la
dimension réellement retournée par le modèle d'embedding actif. Pour Ollama et
`nomic-embed-text-v2-moe` dans la configuration actuelle, la valeur configurée
est `768`. Une collection existante créée avec une autre dimension ne peut pas
être réutilisée telle quelle.

### Évolution du schéma de collection

Un changement de métadonnées, de dimensions ou de stratégie de stockage peut
nécessiter une nouvelle collection. Utiliser un suffixe versionné dans
`CollectionName`, puis réingérer les documents. Cela évite les erreurs du type
colonne absente ou relation incompatible.

### Exécution simultanée

Le singleton `DataIngestionService` ne conserve pas d'état d'exécution, mais
deux appels visant la même collection peuvent tout de même se concurrencer dans
PostgreSQL. Pour les labs console, lancer une seule ingestion à la fois. Une
future application serveur devra ajouter une coordination par collection ou une
file de travaux.

### Ingestion unique et interrogation répétée

`IncrementalIngestion` rend les réexécutions sûres, mais le flux professionnel
doit finalement séparer :

```text
Commande d'ingestion -> IDataIngestionService -> pgvector
Commande de chat      -> IVectorSearchService -> pgvector -> réponse RAG
```

Ainsi, démarrer le chatbot ne relancera pas automatiquement l'ingestion. Le
service décrit ici prépare cette séparation sans imposer dès maintenant une
architecture plus complexe.

### Packages

Cette refactorisation n'exige aucun package supplémentaire. Elle réutilise les
versions déjà référencées de :

- `Microsoft.Extensions.DataIngestion` ;
- `Microsoft.Extensions.DataIngestion.Markdig` ;
- `Microsoft.Extensions.VectorData` et le provider pgvector ;
- `Microsoft.Extensions.AI` ;
- `Microsoft.ML.Tokenizers`.

Conserver des versions explicites et compatibles. Après toute mise à jour,
tester ensemble ingestion, création de collection et recherche afin d'éviter
les incompatibilités binaires entre packages `Microsoft.Extensions.VectorData`.

## 14. Résultat attendu

À la fin de la migration :

- les deux labs appellent le même `IDataIngestionService` ;
- la configuration du chunking, des résumés et de pgvector n'est définie qu'une
  fois ;
- l'ajout d'un nouveau format ne duplique pas le pipeline ;
- la recherche ne devient pas une responsabilité du service d'ingestion ;
- les ressources du pipeline restent valides pendant la recherche puis sont
  libérées de façon déterministe ;
- l'architecture reste assez simple pour conserver la valeur pédagogique des
  labs et peut évoluer ensuite vers une ingestion et une recherche totalement
  indépendantes.
