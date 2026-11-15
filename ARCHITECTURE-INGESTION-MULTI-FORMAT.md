# Architecture cible d'ingestion documentaire multi-format

## 1. Objectif

Cette architecture doit permettre à plusieurs programmes .NET de :

- découvrir des documents ;
- ingérer des fichiers de formats différents (`.md`, `.pdf`, `.txt`, puis
  éventuellement `.docx`) ;
- éviter de réingérer les documents inchangés ;
- connaître et contrôler l'état de chaque ingestion ;
- stocker les chunks et leurs embeddings dans PostgreSQL/pgvector ;
- déclencher explicitement une réingestion après un changement de document, de
  modèle ou de pipeline.

Les programmes consommateurs peuvent être :

- l'application console actuelle ;
- une Web API ;
- un worker en arrière-plan ;
- un outil d'administration ;
- une tâche planifiée.

Le présent document décrit une **architecture cible**. Il ne demande pas de
transformer immédiatement le lab en Clean Architecture.

## 2. Impact architectural

Le besoin de réutilisation dans plusieurs programmes justifie une bibliothèque
réutilisable. La trajectoire recommandée comporte deux étapes.

### Étape 1 — Validation dans le monoprojet

Les contrats et l'implémentation peuvent d'abord être placés dans le projet
actuel :

```text
DotNetIALabs/
    DataIngestion/
        Contracts/
        Core/
        Readers/
        Persistence/
```

Cette étape permet de stabiliser les contrats avec le lab console.

### Étape 2 — Extraction après validation

Lorsqu'un deuxième programme doit consommer le service, extraire les dossiers
dans deux bibliothèques :

```text
DotNetAI.DocumentIngestion.Abstractions/
DotNetAI.DocumentIngestion/
```

La première bibliothèque contient uniquement les contrats publics. La seconde
contient l'orchestration et les adaptateurs techniques.

Ce découpage évite que tous les consommateurs référencent directement Npgsql,
pgvector, les readers Markdown ou la bibliothèque PDF choisie.

## 3. Vue globale

```text
Programme consommateur
        |
        +--> IDocumentIngestionService
        |          |
        |          +--> découverte des sources
        |          +--> calcul de hash
        |          +--> contrôle de l'état
        |          +--> résolution du reader
        |          +--> pipeline d'ingestion
        |          +--> embeddings
        |          +--> pgvector
        |
        +--> IIngestionControlService
                   |
                   +--> consulter les états
                   +--> demander une réingestion
                   +--> consulter les échecs

Formats
  .md  --> MarkdownReader
  .pdf --> PdfDocumentReader --> IPdfTextExtractor
  .txt --> TextDocumentReader

Traitement commun
  reader --> document --> chunks --> enrichissement --> embeddings --> pgvector
```

## 4. Principes de conception

1. Les contrats publics ne dépendent ni de PostgreSQL ni d'Ollama.
2. Chaque format possède un reader spécialisé.
3. Le chunking, l'enrichissement et l'écriture sont communs.
4. L'état d'ingestion est suivi document par document.
5. Une table existante ne prouve pas qu'une ingestion est complète.
6. Un hash de contenu permet d'ignorer les documents inchangés.
7. La version du pipeline fait partie de l'identité de l'ingestion.
8. Un verrou empêche deux programmes d'ingérer le même document simultanément.
9. Une demande de réingestion marque un document comme `Pending` ; elle ne
   supprime pas silencieusement les données.
10. Les opérations asynchrones propagent toujours le `CancellationToken`.

---

# Partie I — Contrats publics

## 5. DocumentSource

`DocumentSource` représente une source à ingérer. Le contrat reste indépendant
du reader et du stockage vectoriel.

```csharp
namespace DotNetAI.DocumentIngestion.Abstractions;

public sealed record DocumentSource
{
    public required string DocumentId { get; init; }

    public required string FilePath { get; init; }

    public required string MediaType { get; init; }

    public string? DisplayName { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();
}
```

### Contraintes

- `DocumentId` doit être stable entre deux exécutions ;
- `FilePath` est la source physique de la première version ;
- `MediaType` évite de dépendre uniquement de l'extension ;
- `Metadata` peut contenir le tenant, la catégorie ou le niveau de sécurité.

Pour une Web API, un fichier reçu peut être matérialisé dans un dossier
temporaire avant de créer le `DocumentSource`. Une extension future pourra
introduire une source basée sur un stream ou un stockage objet.

## 6. IngestionRequest

```csharp
namespace DotNetAI.DocumentIngestion.Abstractions;

public sealed record IngestionRequest
{
    public required IReadOnlyCollection<DocumentSource> Sources { get; init; }

    public required string CollectionName { get; init; }

    public bool Force { get; init; }
}
```

`Force = false` applique le contrôle par hash et version.

`Force = true` demande une nouvelle ingestion, même si le document est déjà à
jour. Cette option doit être réservée aux commandes administratives.

## 7. États d'ingestion

```csharp
namespace DotNetAI.DocumentIngestion.Abstractions;

public enum DocumentIngestionStatus
{
    Pending,
    InProgress,
    Completed,
    Failed
}
```

Cycle normal :

```text
Pending --> InProgress --> Completed
                 |
                 +------> Failed

Completed --demande de réingestion--> Pending
Failed    --nouvelle tentative-------> InProgress
```

## 8. État public d'un document

```csharp
namespace DotNetAI.DocumentIngestion.Abstractions;

public sealed record DocumentIngestionState
{
    public required string DocumentId { get; init; }

    public required string SourcePath { get; init; }

    public required string MediaType { get; init; }

    public required string CollectionName { get; init; }

    public required DocumentIngestionStatus Status { get; init; }

    public required string ContentHash { get; init; }

    public required string PipelineVersion { get; init; }

    public required string EmbeddingModel { get; init; }

    public required int EmbeddingDimensions { get; init; }

    public int? ChunkCount { get; init; }

    public string? ErrorMessage { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
}
```

## 9. Résultat d'un document

```csharp
namespace DotNetAI.DocumentIngestion.Abstractions;

public enum DocumentIngestionAction
{
    Ingested,
    Skipped,
    Failed
}

public sealed record DocumentIngestionResult
{
    public required string DocumentId { get; init; }

    public required DocumentIngestionAction Action { get; init; }

    public int? ChunkCount { get; init; }

    public string? ErrorMessage { get; init; }
}
```

## 10. Rapport de lot

```csharp
namespace DotNetAI.DocumentIngestion.Abstractions;

public sealed record IngestionBatchResult
{
    public required string CollectionName { get; init; }

    public required IReadOnlyCollection<DocumentIngestionResult> Documents
        { get; init; }

    public int IngestedCount => Documents.Count(
        item => item.Action == DocumentIngestionAction.Ingested);

    public int SkippedCount => Documents.Count(
        item => item.Action == DocumentIngestionAction.Skipped);

    public int FailedCount => Documents.Count(
        item => item.Action == DocumentIngestionAction.Failed);

    public bool Succeeded => FailedCount == 0;
}
```

## 11. Contrat principal d'ingestion

```csharp
namespace DotNetAI.DocumentIngestion.Abstractions;

public interface IDocumentIngestionService
{
    Task<IngestionBatchResult> EnsureIngestedAsync(
        IngestionRequest request,
        CancellationToken cancellationToken = default);
}
```

Le verbe `Ensure` indique que l'appel est idempotent : un document déjà à jour
est ignoré.

Le contrat ne retourne pas de `PostgresVectorStore`, de `NpgsqlConnection` ou
de type spécifique à Ollama.

## 12. Contrat de contrôle

```csharp
namespace DotNetAI.DocumentIngestion.Abstractions;

public interface IIngestionControlService
{
    Task<DocumentIngestionState?> GetStateAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<DocumentIngestionState> ListStatesAsync(
        DocumentIngestionStatus? status = null,
        CancellationToken cancellationToken = default);

    Task RequestReingestionAsync(
        string documentId,
        CancellationToken cancellationToken = default);
}
```

Ce contrat permet de construire un écran d'administration sans lui donner un
accès direct aux tables internes.

Il ne propose volontairement pas `DeleteCollectionAsync`. Une suppression de
collection est destructive et doit être traitée dans un outil d'administration
distinct avec confirmation explicite.

---

# Partie II — Configuration

## 13. DocumentIngestionOptions

```csharp
namespace DotNetAI.DocumentIngestion;

public sealed class DocumentIngestionOptions
{
    public const string SectionName = "DataIngestion";

    public string CollectionName { get; init; } = "documents-v1";

    public string PipelineVersion { get; init; } = "1";

    public int MaxTokensPerChunk { get; init; } = 400;

    public int OverlapTokens { get; init; }

    public bool GenerateSummaries { get; init; } = true;

    public string[] SupportedExtensions { get; init; } =
        [".md", ".pdf", ".txt"];
}
```

Exemple de configuration :

```json
{
  "DataIngestion": {
    "CollectionName": "documents-v1",
    "PipelineVersion": "1",
    "MaxTokensPerChunk": 400,
    "OverlapTokens": 0,
    "GenerateSummaries": true,
    "SupportedExtensions": [".md", ".pdf", ".txt"]
  }
}
```

La version doit changer lorsque le résultat stocké devient incompatible :

- nouvelle dimension ;
- nouvelle stratégie de chunking ;
- changement important des métadonnées ;
- nouvelle logique d'enrichissement.

## 14. Validation des options

```csharp
using Microsoft.Extensions.Options;

namespace DotNetAI.DocumentIngestion;

public sealed class DocumentIngestionOptionsValidator
    : IValidateOptions<DocumentIngestionOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        DocumentIngestionOptions options)
    {
        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(options.CollectionName))
        {
            errors.Add("DataIngestion:CollectionName is required.");
        }

        if (string.IsNullOrWhiteSpace(options.PipelineVersion))
        {
            errors.Add("DataIngestion:PipelineVersion is required.");
        }

        if (options.MaxTokensPerChunk <= 0)
        {
            errors.Add("DataIngestion:MaxTokensPerChunk must be positive.");
        }

        if (options.OverlapTokens < 0 ||
            options.OverlapTokens >= options.MaxTokensPerChunk)
        {
            errors.Add(
                "DataIngestion:OverlapTokens must be between 0 and " +
                "MaxTokensPerChunk - 1.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
```

Ce validateur n'effectue aucun appel réseau.

---

# Partie III — Découverte et identification des documents

## 15. Source provider

Un programme peut fournir explicitement ses sources ou utiliser un provider.

```csharp
namespace DotNetAI.DocumentIngestion;

public interface IDocumentSourceProvider
{
    IAsyncEnumerable<DocumentSource> GetSourcesAsync(
        CancellationToken cancellationToken = default);
}
```

## 16. FileSystemDocumentSourceProvider

```csharp
using System.Runtime.CompilerServices;

namespace DotNetAI.DocumentIngestion;

public sealed class FileSystemDocumentSourceProvider(
    DirectoryInfo root,
    IReadOnlySet<string> supportedExtensions)
    : IDocumentSourceProvider
{
    public async IAsyncEnumerable<DocumentSource> GetSourcesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (FileInfo file in root.EnumerateFiles(
            "*",
            SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!supportedExtensions.Contains(file.Extension))
            {
                continue;
            }

            yield return new DocumentSource
            {
                DocumentId = file.FullName,
                FilePath = file.FullName,
                DisplayName = file.Name,
                MediaType = MediaTypeMap.FromExtension(file.Extension)
            };

            await Task.Yield();
        }
    }
}
```

## 17. Mapping des types MIME

```csharp
namespace DotNetAI.DocumentIngestion;

public static class MediaTypeMap
{
    public static string FromExtension(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".md" => "text/markdown",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
}
```

Dans une Web API, le type MIME fourni par le client doit être validé avec le
contenu réel. L'extension seule n'est pas une frontière de sécurité.

## 18. Calcul du hash

```csharp
namespace DotNetAI.DocumentIngestion;

public interface IDocumentHashProvider
{
    Task<string> ComputeAsync(
        DocumentSource source,
        CancellationToken cancellationToken = default);
}
```

Implémentation SHA-256 :

```csharp
using System.Security.Cryptography;

namespace DotNetAI.DocumentIngestion;

public sealed class Sha256DocumentHashProvider : IDocumentHashProvider
{
    public async Task<string> ComputeAsync(
        DocumentSource source,
        CancellationToken cancellationToken = default)
    {
        await using FileStream stream = new(
            source.FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        byte[] hash = await SHA256.HashDataAsync(
            stream,
            cancellationToken);

        return Convert.ToHexString(hash);
    }
}
```

---

# Partie IV — Readers multi-format

## 19. Contrat interne d'un reader enregistré

Ce contrat appartient à la bibliothèque d'implémentation, car il dépend de
`Microsoft.Extensions.DataIngestion`.

```csharp
using Microsoft.Extensions.DataIngestion;

namespace DotNetAI.DocumentIngestion.Readers;

public interface IDocumentReaderRegistration
{
    IReadOnlySet<string> SupportedExtensions { get; }

    IReadOnlySet<string> SupportedMediaTypes { get; }

    IngestionDocumentReader Reader { get; }
}
```

## 20. Resolver de readers

```csharp
namespace DotNetAI.DocumentIngestion.Readers;

public interface IDocumentReaderResolver
{
    IDocumentReaderRegistration Resolve(DocumentSource source);
}

public sealed class DocumentReaderResolver(
    IEnumerable<IDocumentReaderRegistration> registrations)
    : IDocumentReaderResolver
{
    private readonly IReadOnlyCollection<IDocumentReaderRegistration>
        _registrations = registrations.ToArray();

    public IDocumentReaderRegistration Resolve(DocumentSource source)
    {
        string extension = Path.GetExtension(source.FilePath);

        IDocumentReaderRegistration? registration = _registrations
            .FirstOrDefault(candidate =>
                candidate.SupportedMediaTypes.Contains(source.MediaType) ||
                candidate.SupportedExtensions.Contains(extension));

        return registration ?? throw new NotSupportedException(
            $"No document reader is registered for '{source.MediaType}' " +
            $"or extension '{extension}'.");
    }
}
```

## 21. Enregistrement Markdown

```csharp
using Microsoft.Extensions.DataIngestion;

namespace DotNetAI.DocumentIngestion.Readers;

public sealed class MarkdownReaderRegistration : IDocumentReaderRegistration
{
    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".md" };

    public IReadOnlySet<string> SupportedMediaTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "text/markdown"
        };

    public IngestionDocumentReader Reader { get; } = new MarkdownReader();
}
```

## 22. Reader texte

```csharp
using Microsoft.Extensions.DataIngestion;

namespace DotNetAI.DocumentIngestion.Readers;

public sealed class TextDocumentReader : IngestionDocumentReader
{
    public override async Task<IngestionDocument> ReadAsync(
        Stream source,
        string identifier,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        using StreamReader reader = new(
            source,
            leaveOpen: true);

        string content = await reader.ReadToEndAsync(cancellationToken);

        IngestionDocument document = new(identifier);
        document.Sections.Add(new IngestionDocumentSection(content));
        return document;
    }
}
```

Son enregistrement :

```csharp
public sealed class TextReaderRegistration : IDocumentReaderRegistration
{
    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt" };

    public IReadOnlySet<string> SupportedMediaTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text/plain" };

    public IngestionDocumentReader Reader { get; } =
        new TextDocumentReader();
}
```

## 23. Abstraction d'extraction PDF

La bibliothèque d'ingestion ne doit pas imposer une bibliothèque PDF précise.

```csharp
namespace DotNetAI.DocumentIngestion.Readers.Pdf;

public sealed record PdfPageText(int PageNumber, string Text);

public interface IPdfTextExtractor
{
    Task<IReadOnlyCollection<PdfPageText>> ExtractAsync(
        Stream source,
        CancellationToken cancellationToken = default);
}
```

Une implémentation pourra utiliser la bibliothèque PDF choisie par le projet.
Une autre pourra appeler un service OCR pour les PDF scannés.

## 24. PdfDocumentReader

```csharp
using Microsoft.Extensions.DataIngestion;

namespace DotNetAI.DocumentIngestion.Readers.Pdf;

public sealed class PdfDocumentReader(IPdfTextExtractor extractor)
    : IngestionDocumentReader
{
    public override async Task<IngestionDocument> ReadAsync(
        Stream source,
        string identifier,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<PdfPageText> pages =
            await extractor.ExtractAsync(source, cancellationToken);

        IngestionDocument document = new(identifier);

        foreach (PdfPageText page in pages.OrderBy(item => item.PageNumber))
        {
            if (string.IsNullOrWhiteSpace(page.Text))
            {
                continue;
            }

            string markdown =
                $"# Page {page.PageNumber}{Environment.NewLine}" +
                $"{Environment.NewLine}{page.Text}";

            document.Sections.Add(
                new IngestionDocumentSection(markdown));
        }

        return document;
    }
}
```

## 25. Enregistrement PDF

```csharp
using Microsoft.Extensions.DataIngestion;

namespace DotNetAI.DocumentIngestion.Readers.Pdf;

public sealed class PdfReaderRegistration(IPdfTextExtractor extractor)
    : IDocumentReaderRegistration
{
    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf" };

    public IReadOnlySet<string> SupportedMediaTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf"
        };

    public IngestionDocumentReader Reader { get; } =
        new PdfDocumentReader(extractor);
}
```

### PDF scannés

Si aucune page ne contient de texte, l'extracteur doit :

- signaler que l'OCR est nécessaire ;
- ou appeler explicitement un composant OCR configuré.

Le système ne doit pas marquer un PDF vide comme correctement ingéré.

---

# Partie V — Persistance de l'état

## 26. Table PostgreSQL de contrôle

```sql
CREATE TABLE IF NOT EXISTS ingestion_documents (
    document_id          text PRIMARY KEY,
    source_path          text NOT NULL,
    media_type           text NOT NULL,
    collection_name      text NOT NULL,
    content_hash         text NOT NULL,
    pipeline_version     text NOT NULL,
    embedding_model      text NOT NULL,
    embedding_dimensions integer NOT NULL,
    status               text NOT NULL CHECK (
        status IN ('Pending', 'InProgress', 'Completed', 'Failed')
    ),
    chunk_count          integer NULL,
    error_message        text NULL,
    started_at           timestamptz NULL,
    completed_at         timestamptz NULL,
    updated_at           timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_ingestion_documents_status
    ON ingestion_documents(status);
```

Cette table est distincte de la collection vectorielle.

## 27. Contrat interne du state store

```csharp
namespace DotNetAI.DocumentIngestion.Persistence;

internal interface IIngestionStateStore
{
    Task<DocumentIngestionState?> GetAsync(
        string documentId,
        CancellationToken cancellationToken);

    IAsyncEnumerable<DocumentIngestionState> ListAsync(
        DocumentIngestionStatus? status,
        CancellationToken cancellationToken);

    Task BeginAsync(
        DocumentIngestionState state,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        string documentId,
        int? chunkCount,
        CancellationToken cancellationToken);

    Task FailAsync(
        string documentId,
        string errorMessage,
        CancellationToken cancellationToken);

    Task MarkPendingAsync(
        string documentId,
        CancellationToken cancellationToken);
}
```

## 28. PostgreSqlIngestionStateStore

L'implémentation utilise des commandes paramétrées. Aucun identifiant ou
contenu utilisateur n'est concaténé dans le SQL.

```csharp
using Npgsql;

namespace DotNetAI.DocumentIngestion.Persistence;

internal sealed class PostgreSqlIngestionStateStore(
    NpgsqlDataSource dataSource)
    : IIngestionStateStore
{
    public async Task<DocumentIngestionState?> GetAsync(
        string documentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT document_id, source_path, media_type, collection_name,
                   content_hash, pipeline_version, embedding_model,
                   embedding_dimensions, status, chunk_count, error_message,
                   started_at, completed_at
            FROM ingestion_documents
            WHERE document_id = @documentId;
            """;

        await using NpgsqlCommand command =
            dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("documentId", documentId);

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? Map(reader)
            : null;
    }

    public async Task BeginAsync(
        DocumentIngestionState state,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO ingestion_documents (
                document_id, source_path, media_type, collection_name,
                content_hash, pipeline_version, embedding_model,
                embedding_dimensions, status, started_at, updated_at)
            VALUES (
                @documentId, @sourcePath, @mediaType, @collectionName,
                @contentHash, @pipelineVersion, @embeddingModel,
                @embeddingDimensions, 'InProgress', now(), now())
            ON CONFLICT (document_id) DO UPDATE SET
                source_path = EXCLUDED.source_path,
                media_type = EXCLUDED.media_type,
                collection_name = EXCLUDED.collection_name,
                content_hash = EXCLUDED.content_hash,
                pipeline_version = EXCLUDED.pipeline_version,
                embedding_model = EXCLUDED.embedding_model,
                embedding_dimensions = EXCLUDED.embedding_dimensions,
                status = 'InProgress',
                error_message = NULL,
                started_at = now(),
                completed_at = NULL,
                updated_at = now();
            """;

        await using NpgsqlCommand command = dataSource.CreateCommand(sql);
        AddStateParameters(command, state);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CompleteAsync(
        string documentId,
        int? chunkCount,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE ingestion_documents
            SET status = 'Completed',
                chunk_count = @chunkCount,
                error_message = NULL,
                completed_at = now(),
                updated_at = now()
            WHERE document_id = @documentId;
            """;

        await using NpgsqlCommand command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("documentId", documentId);
        command.Parameters.AddWithValue(
            "chunkCount",
            (object?)chunkCount ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task FailAsync(
        string documentId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE ingestion_documents
            SET status = 'Failed',
                error_message = @errorMessage,
                updated_at = now()
            WHERE document_id = @documentId;
            """;

        await using NpgsqlCommand command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("documentId", documentId);
        command.Parameters.AddWithValue("errorMessage", errorMessage);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkPendingAsync(
        string documentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE ingestion_documents
            SET status = 'Pending',
                error_message = NULL,
                updated_at = now()
            WHERE document_id = @documentId;
            """;

        await using NpgsqlCommand command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("documentId", documentId);

        int affectedRows =
            await command.ExecuteNonQueryAsync(cancellationToken);

        if (affectedRows == 0)
        {
            throw new KeyNotFoundException(
                $"Document '{documentId}' has no ingestion state.");
        }
    }

    public async IAsyncEnumerable<DocumentIngestionState> ListAsync(
        DocumentIngestionStatus? status,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT document_id, source_path, media_type, collection_name,
                   content_hash, pipeline_version, embedding_model,
                   embedding_dimensions, status, chunk_count, error_message,
                   started_at, completed_at
            FROM ingestion_documents
            WHERE @status IS NULL OR status = @status
            ORDER BY updated_at DESC;
            """;

        await using NpgsqlCommand command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(
            "status",
            status is null ? DBNull.Value : status.ToString());

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            yield return Map(reader);
        }
    }

    private static DocumentIngestionState Map(NpgsqlDataReader reader) =>
        new()
        {
            DocumentId = reader.GetString(0),
            SourcePath = reader.GetString(1),
            MediaType = reader.GetString(2),
            CollectionName = reader.GetString(3),
            ContentHash = reader.GetString(4),
            PipelineVersion = reader.GetString(5),
            EmbeddingModel = reader.GetString(6),
            EmbeddingDimensions = reader.GetInt32(7),
            Status = Enum.Parse<DocumentIngestionStatus>(reader.GetString(8)),
            ChunkCount = reader.IsDBNull(9) ? null : reader.GetInt32(9),
            ErrorMessage = reader.IsDBNull(10) ? null : reader.GetString(10),
            StartedAt = reader.IsDBNull(11)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(11),
            CompletedAt = reader.IsDBNull(12)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(12)
        };

    private static void AddStateParameters(
        NpgsqlCommand command,
        DocumentIngestionState state)
    {
        command.Parameters.AddWithValue("documentId", state.DocumentId);
        command.Parameters.AddWithValue("sourcePath", state.SourcePath);
        command.Parameters.AddWithValue("mediaType", state.MediaType);
        command.Parameters.AddWithValue("collectionName", state.CollectionName);
        command.Parameters.AddWithValue("contentHash", state.ContentHash);
        command.Parameters.AddWithValue("pipelineVersion", state.PipelineVersion);
        command.Parameters.AddWithValue("embeddingModel", state.EmbeddingModel);
        command.Parameters.AddWithValue(
            "embeddingDimensions",
            state.EmbeddingDimensions);
    }
}
```

## 29. Verrou d'ingestion

```csharp
namespace DotNetAI.DocumentIngestion.Persistence;

internal interface IIngestionLockProvider
{
    ValueTask<IAsyncDisposable> AcquireAsync(
        string documentId,
        CancellationToken cancellationToken);
}
```

L'implémentation PostgreSQL peut utiliser un advisory lock. La connexion doit
rester ouverte pendant toute la durée du lease, puis exécuter
`pg_advisory_unlock` lors du `DisposeAsync`.

Le verrou est nécessaire pour une Web API ou plusieurs workers. Pour la console
mono-instance, un `SemaphoreSlim` interne constitue une première étape
acceptable.

---

# Partie VI — Pipeline et orchestration

## 30. Pipeline factory

```csharp
using Microsoft.Extensions.DataIngestion;

namespace DotNetAI.DocumentIngestion.Core;

internal interface IIngestionPipelineFactory
{
    IngestionPipelineSession Create(
        IngestionDocumentReader reader,
        string collectionName);
}

internal sealed class IngestionPipelineSession(
    IngestionPipeline<string> pipeline,
    VectorStoreWriter<string> writer) : IDisposable
{
    public IngestionPipeline<string> Pipeline { get; } = pipeline;

    public void Dispose()
    {
        Pipeline.Dispose();
        writer.Dispose();
    }
}
```

La session rend explicite la propriété des deux objets jetables. Elle évite que
la factory retourne uniquement le pipeline en perdant la référence vers le
`VectorStoreWriter` qu'elle a créé.

Implémentation conceptuelle :

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;
using Microsoft.ML.Tokenizers;

namespace DotNetAI.DocumentIngestion.Core;

internal sealed class IngestionPipelineFactory(
    IChatClient chatClient,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    VectorStore vectorStore,
    IOptions<DocumentIngestionOptions> options,
    IEmbeddingModelDescriptor embeddingModel,
    ILoggerFactory loggerFactory)
    : IIngestionPipelineFactory
{
    public IngestionPipelineSession Create(
        IngestionDocumentReader reader,
        string collectionName)
    {
        Tokenizer tokenizer =
            TiktokenTokenizer.CreateForEncoding("o200k_base");

        IngestionChunkerOptions chunkerOptions = new(tokenizer)
        {
            MaxTokensPerChunk = options.Value.MaxTokensPerChunk,
            OverlapTokens = options.Value.OverlapTokens
        };

        IngestionChunker<string> chunker =
            new SemanticSimilarityChunker(
                embeddingGenerator,
                chunkerOptions);

        VectorStoreWriter<string> writer = new(
            vectorStore,
            embeddingModel.Dimensions,
            new VectorStoreWriterOptions
            {
                CollectionName = collectionName,
                DistanceFunction = DistanceFunction.CosineDistance,
                IndexKind = IndexKind.Hnsw,
                IncrementalIngestion = true
            });

        IngestionPipeline<string> pipeline = new(
            reader,
            chunker,
            writer,
            loggerFactory: loggerFactory);

        if (options.Value.GenerateSummaries)
        {
            pipeline.ChunkProcessors.Add(
                new SingleChunkSummaryEnricher(
                    chatClient,
                    loggerFactory.CreateLogger<
                        SingleChunkSummaryEnricher>()));
        }

        return new IngestionPipelineSession(pipeline, writer);
    }
}
```

`IEmbeddingModelDescriptor` évite de relire les options propres à chaque
provider dans le service générique :

```csharp
namespace DotNetAI.DocumentIngestion.Core;

public interface IEmbeddingModelDescriptor
{
    string ModelName { get; }

    int Dimensions { get; }
}
```

## 31. Décision de réingestion

```csharp
namespace DotNetAI.DocumentIngestion.Core;

internal static class IngestionDecision
{
    public static bool IsCurrent(
        DocumentIngestionState? state,
        string contentHash,
        string pipelineVersion,
        IEmbeddingModelDescriptor embeddingModel,
        string collectionName)
    {
        return state is not null &&
               state.Status == DocumentIngestionStatus.Completed &&
               state.ContentHash == contentHash &&
               state.PipelineVersion == pipelineVersion &&
               state.EmbeddingModel == embeddingModel.ModelName &&
               state.EmbeddingDimensions == embeddingModel.Dimensions &&
               state.CollectionName == collectionName;
    }
}
```

## 32. DocumentIngestionService

Le service groupe les sources par reader. Chaque groupe utilise un pipeline
adapté au format, mais partage le même chunker, le même modèle et la même
collection.

```csharp
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Options;

namespace DotNetAI.DocumentIngestion.Core;

public sealed class DocumentIngestionService(
    IDocumentHashProvider hashProvider,
    IDocumentReaderResolver readerResolver,
    IIngestionPipelineFactory pipelineFactory,
    IIngestionStateStore stateStore,
    IIngestionLockProvider lockProvider,
    IEmbeddingModelDescriptor embeddingModel,
    IOptions<DocumentIngestionOptions> options)
    : IDocumentIngestionService
{
    public async Task<IngestionBatchResult> EnsureIngestedAsync(
        IngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<DocumentIngestionResult> results = [];

        foreach (DocumentSource source in request.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DocumentIngestionResult result = await ProcessSourceAsync(
                source,
                request.CollectionName,
                request.Force,
                cancellationToken);

            results.Add(result);
        }

        return new IngestionBatchResult
        {
            CollectionName = request.CollectionName,
            Documents = results
        };
    }

    private async Task<DocumentIngestionResult> ProcessSourceAsync(
        DocumentSource source,
        string collectionName,
        bool force,
        CancellationToken cancellationToken)
    {
        await using IAsyncDisposable ingestionLock =
            await lockProvider.AcquireAsync(
                source.DocumentId,
                cancellationToken);

        string contentHash = await hashProvider.ComputeAsync(
            source,
            cancellationToken);

        DocumentIngestionState? currentState =
            await stateStore.GetAsync(
                source.DocumentId,
                cancellationToken);

        if (!force && IngestionDecision.IsCurrent(
            currentState,
            contentHash,
            options.Value.PipelineVersion,
            embeddingModel,
            collectionName))
        {
            return new DocumentIngestionResult
            {
                DocumentId = source.DocumentId,
                Action = DocumentIngestionAction.Skipped,
                ChunkCount = currentState!.ChunkCount
            };
        }

        DocumentIngestionState startedState = new()
        {
            DocumentId = source.DocumentId,
            SourcePath = source.FilePath,
            MediaType = source.MediaType,
            CollectionName = collectionName,
            ContentHash = contentHash,
            PipelineVersion = options.Value.PipelineVersion,
            EmbeddingModel = embeddingModel.ModelName,
            EmbeddingDimensions = embeddingModel.Dimensions,
            Status = DocumentIngestionStatus.InProgress,
            StartedAt = DateTimeOffset.UtcNow
        };

        await stateStore.BeginAsync(startedState, cancellationToken);

        try
        {
            IDocumentReaderRegistration registration =
                readerResolver.Resolve(source);

            using IngestionPipelineSession pipelineSession =
                pipelineFactory.Create(
                    registration.Reader,
                    collectionName);

            IngestionResult? pipelineResult = null;

            await foreach (IngestionResult result in
                pipelineSession.Pipeline.ProcessAsync(
                    [new FileInfo(source.FilePath)],
                    cancellationToken))
            {
                pipelineResult = result;
            }

            if (pipelineResult is null || !pipelineResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Ingestion failed for '{source.DocumentId}'.");
            }

            // Le chunk count peut être obtenu par instrumentation du writer ou
            // par une requête filtrée sur document_id après l'écriture.
            int? chunkCount = null;

            await stateStore.CompleteAsync(
                source.DocumentId,
                chunkCount,
                cancellationToken);

            return new DocumentIngestionResult
            {
                DocumentId = source.DocumentId,
                Action = DocumentIngestionAction.Ingested,
                ChunkCount = chunkCount
            };
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            await stateStore.FailAsync(
                source.DocumentId,
                exception.Message,
                cancellationToken);

            return new DocumentIngestionResult
            {
                DocumentId = source.DocumentId,
                Action = DocumentIngestionAction.Failed,
                ErrorMessage = exception.Message
            };
        }
    }
}
```

### Traitement par groupes

L'exemple précédent traite les sources une par une, ce qui facilite le suivi
d'état. Une optimisation ultérieure peut regrouper les sources par
`IDocumentReaderRegistration` et appeler `ProcessAsync(IEnumerable<FileInfo>)`.

Cette optimisation ne doit pas supprimer le suivi individuel des résultats.

## 33. IngestionControlService

```csharp
namespace DotNetAI.DocumentIngestion.Core;

public sealed class IngestionControlService(
    IIngestionStateStore stateStore)
    : IIngestionControlService
{
    public Task<DocumentIngestionState?> GetStateAsync(
        string documentId,
        CancellationToken cancellationToken = default) =>
        stateStore.GetAsync(documentId, cancellationToken);

    public IAsyncEnumerable<DocumentIngestionState> ListStatesAsync(
        DocumentIngestionStatus? status = null,
        CancellationToken cancellationToken = default) =>
        stateStore.ListAsync(status, cancellationToken);

    public Task RequestReingestionAsync(
        string documentId,
        CancellationToken cancellationToken = default) =>
        stateStore.MarkPendingAsync(documentId, cancellationToken);
}
```

---

# Partie VII — Injection de dépendances

## 34. Extension d'enregistrement

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector.Npgsql;

namespace DotNetAI.DocumentIngestion;

public static class DocumentIngestionServiceCollectionExtensions
{
    public static IServiceCollection AddDocumentIngestion(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<DocumentIngestionOptions>()
            .Bind(configuration.GetSection(
                DocumentIngestionOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<
            IValidateOptions<DocumentIngestionOptions>,
            DocumentIngestionOptionsValidator>();

        string connectionString = configuration
            .GetConnectionString("VectorStore")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:VectorStore is required.");

        services.AddSingleton(_ =>
        {
            NpgsqlDataSourceBuilder builder = new(connectionString);
            builder.UseVector();
            return builder.Build();
        });

        services.AddSingleton<IDocumentHashProvider,
            Sha256DocumentHashProvider>();

        services.AddSingleton<IDocumentReaderRegistration,
            MarkdownReaderRegistration>();
        services.AddSingleton<IDocumentReaderRegistration,
            TextReaderRegistration>();
        services.AddSingleton<IDocumentReaderRegistration,
            PdfReaderRegistration>();

        services.AddSingleton<IDocumentReaderResolver,
            DocumentReaderResolver>();

        services.AddSingleton<IIngestionStateStore,
            PostgreSqlIngestionStateStore>();
        services.AddSingleton<IIngestionLockProvider,
            PostgreSqlIngestionLockProvider>();

        services.AddSingleton<IIngestionPipelineFactory,
            IngestionPipelineFactory>();

        services.AddSingleton<IDocumentIngestionService,
            DocumentIngestionService>();
        services.AddSingleton<IIngestionControlService,
            IngestionControlService>();

        return services;
    }
}
```

`IPdfTextExtractor` doit être enregistré par le programme hôte, car il dépend
du choix technique du consommateur :

```csharp
services.AddSingleton<IPdfTextExtractor, MyPdfTextExtractor>();
```

Les clients IA et le `VectorStore` restent enregistrés dans l'infrastructure
du programme hôte, ou dans une extension dédiée au provider actif.

---

# Partie VIII — Utilisation par plusieurs programmes

## 35. Console

```csharp
public sealed class IngestionConsoleRunner(
    IDocumentSourceProvider sourceProvider,
    IDocumentIngestionService ingestionService,
    IOptions<DocumentIngestionOptions> options)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        List<DocumentSource> sources = [];

        await foreach (DocumentSource source in
            sourceProvider.GetSourcesAsync(cancellationToken))
        {
            sources.Add(source);
        }

        IngestionBatchResult result =
            await ingestionService.EnsureIngestedAsync(
                new IngestionRequest
                {
                    Sources = sources,
                    CollectionName = options.Value.CollectionName
                },
                cancellationToken);

        Console.WriteLine(
            $"Ingested: {result.IngestedCount}; " +
            $"Skipped: {result.SkippedCount}; " +
            $"Failed: {result.FailedCount}.");
    }
}
```

## 36. Web API

```csharp
app.MapPost("/ingestion", async (
    IngestionRequest request,
    IDocumentIngestionService service,
    CancellationToken cancellationToken) =>
{
    IngestionBatchResult result =
        await service.EnsureIngestedAsync(
            request,
            cancellationToken);

    return result.Succeeded
        ? Results.Ok(result)
        : Results.Json(result, statusCode: 207);
});

app.MapGet("/ingestion/{documentId}", async (
    string documentId,
    IIngestionControlService control,
    CancellationToken cancellationToken) =>
{
    DocumentIngestionState? state =
        await control.GetStateAsync(documentId, cancellationToken);

    return state is null
        ? Results.NotFound()
        : Results.Ok(state);
});

app.MapPost("/ingestion/{documentId}/reingest", async (
    string documentId,
    IIngestionControlService control,
    CancellationToken cancellationToken) =>
{
    await control.RequestReingestionAsync(
        documentId,
        cancellationToken);

    return Results.Accepted();
});
```

Une Web API ne doit pas accepter directement un chemin arbitraire fourni par un
client. Elle doit d'abord enregistrer le fichier dans une zone contrôlée, puis
construire le `DocumentSource` côté serveur.

## 37. Worker

```csharp
public sealed class IngestionWorker(
    IDocumentSourceProvider sourceProvider,
    IDocumentIngestionService ingestionService,
    IOptions<DocumentIngestionOptions> options,
    ILogger<IngestionWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            List<DocumentSource> sources = [];

            await foreach (DocumentSource source in
                sourceProvider.GetSourcesAsync(stoppingToken))
            {
                sources.Add(source);
            }

            IngestionBatchResult result =
                await ingestionService.EnsureIngestedAsync(
                    new IngestionRequest
                    {
                        Sources = sources,
                        CollectionName = options.Value.CollectionName
                    },
                    stoppingToken);

            logger.LogInformation(
                "Ingestion completed. Ingested: {Ingested}; " +
                "Skipped: {Skipped}; Failed: {Failed}.",
                result.IngestedCount,
                result.SkippedCount,
                result.FailedCount);

            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }
    }
}
```

---

# Partie IX — Comportement fonctionnel

## 38. Première ingestion

```text
Document inconnu
    -> calcul du hash
    -> état InProgress
    -> choix du reader
    -> extraction
    -> chunking
    -> enrichissement
    -> embeddings
    -> écriture pgvector
    -> état Completed
```

## 39. Document inchangé

```text
Hash identique
+ pipeline identique
+ modèle identique
+ dimensions identiques
+ état Completed
    -> résultat Skipped
    -> aucun appel au modèle
```

## 40. Document modifié

```text
Hash différent
    -> état InProgress
    -> nouvelle ingestion
    -> IncrementalIngestion remplace les anciens chunks
    -> état Completed
```

## 41. Changement de modèle

Si le modèle ou la dimension change, le document n'est plus considéré comme à
jour.

Une dimension incompatible doit utiliser une nouvelle collection :

```text
documents-v1 : ancien modèle, 768 dimensions
documents-v2 : nouveau modèle, 1 536 dimensions
```

## 42. Échec partiel

Chaque document possède son propre état :

```text
sample.md    Completed
manual.pdf   Failed
notes.txt    Completed
```

Un PDF défectueux ne doit pas invalider les documents correctement ingérés.

---

# Partie X — Sécurité et exploitation

## 43. Validations des fichiers

Avant ingestion :

- limiter la taille maximale ;
- vérifier l'extension et le type MIME ;
- refuser les chemins en dehors des racines autorisées ;
- limiter le nombre de pages PDF ;
- appliquer un délai maximal à l'OCR ;
- journaliser le document responsable d'un échec ;
- ne jamais journaliser les chaînes de connexion ou clés API.

## 44. PostgreSQL

L'extension doit être créée une seule fois par un administrateur :

```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

L'utilisateur applicatif doit avoir les droits nécessaires sur le schéma et
les tables, mais ne doit pas être super-utilisateur.

## 45. Observabilité

Journaliser au minimum :

- document découvert ;
- reader sélectionné ;
- document ignoré et raison ;
- début et fin d'ingestion ;
- nombre de chunks ;
- durée d'extraction, de chunking et d'écriture ;
- état final ;
- exception sans secret.

Des métriques utiles :

```text
ingestion_documents_total
ingestion_documents_failed_total
ingestion_documents_skipped_total
ingestion_duration_seconds
ingestion_chunks_total
```

---

# Partie XI — Tests

## 46. Tests unitaires

Tester séparément :

- résolution `.md`, `.pdf` et `.txt` ;
- rejet d'un format inconnu ;
- stabilité du hash ;
- décision `Skipped` lorsque tout correspond ;
- réingestion après changement de hash ;
- réingestion après changement de modèle ;
- passage à `Failed` après exception ;
- `RequestReingestionAsync` vers `Pending`.

## 47. Tests d'intégration

Avec un PostgreSQL de test contenant pgvector :

1. ingérer un Markdown ;
2. vérifier l'état `Completed` ;
3. relancer et vérifier `Skipped` ;
4. modifier le fichier et vérifier `Ingested` ;
5. ingérer un PDF textuel ;
6. simuler un PDF illisible et vérifier `Failed` ;
7. lancer deux ingestions concurrentes du même document ;
8. vérifier qu'une seule détient le verrou.

---

# Partie XII — Plan d'adoption

## 48. Phase 1

- extraire `IDocumentIngestionService` ;
- conserver uniquement Markdown ;
- créer la table `ingestion_documents` ;
- implémenter le hash et le comportement `Skipped` ;
- utiliser le service depuis le lab console.

## 49. Phase 2

- ajouter `IIngestionControlService` ;
- ajouter les états `Pending`, `InProgress`, `Completed`, `Failed` ;
- ajouter le verrou mono-instance ;
- ajouter les commandes de consultation et de réingestion.

## 50. Phase 3

- ajouter le resolver de readers ;
- ajouter `.txt` ;
- intégrer un extracteur PDF textuel ;
- conserver le numéro de page dans le contexte du chunk.

## 51. Phase 4

- ajouter l'OCR pour les PDF scannés ;
- extraire la bibliothèque réutilisable ;
- intégrer une Web API ou un worker ;
- remplacer le verrou local par un advisory lock PostgreSQL.

## 52. Décisions recommandées

Pour éviter une architecture trop lourde :

- conserver une seule implémentation de `IDocumentIngestionService` ;
- ne pas créer de repository générique ;
- ne pas abstraire `VectorStore`, qui est déjà une abstraction ;
- ne pas créer un chunker par format avant d'avoir un besoin mesuré ;
- isoler la bibliothèque PDF derrière `IPdfTextExtractor` ;
- n'extraire les projets qu'au moment où un deuxième programme les consomme.

## 53. Résultat cible

L'ajout d'un nouveau format doit nécessiter principalement :

1. un nouveau `IngestionDocumentReader` ;
2. un nouvel `IDocumentReaderRegistration` ;
3. l'enregistrement DI correspondant.

Le service d'ingestion, le contrôle d'état, le hash, le stockage pgvector et
les programmes consommateurs ne doivent pas être réécrits.

