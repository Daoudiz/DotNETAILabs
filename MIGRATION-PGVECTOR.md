# Migration du lab d'ingestion de SQLite vers PostgreSQL/pgvector

## 1. Objectif

Ce document décrit la migration du lab `RunDataIngestionPipeline` depuis
`CommunityToolkit.VectorData.SqliteVec` vers PostgreSQL et l'extension
`pgvector`.

La migration doit conserver :

- la lecture des documents Markdown ;
- le découpage sémantique en chunks ;
- la génération des résumés ;
- la génération des embeddings par le fournisseur IA actif ;
- `VectorStoreWriter<string>` pour écrire les chunks ;
- `SearchAsync` pour effectuer la recherche vectorielle.

Seule l'implémentation du `VectorStore` change. Les abstractions
`Microsoft.Extensions.VectorData` restent utilisées, ce qui limite l'impact sur
le lab.

## 2. Situation actuelle

Le lab construit actuellement le stockage SQLite directement dans
`Presentation/Labs/RAGLabs.cs` :

```csharp
using SqliteVectorStore vectorStore = new(
    "Data Source=vectors.db;Pooling=false",
    new()
    {
        EmbeddingGenerator = embeddingGenerator
    });
```

Le projet utilise :

- .NET 10 ;
- `Microsoft.Extensions.AI` 10.11.0 ;
- `Microsoft.Extensions.DataIngestion` 10.11.0-preview ;
- `CommunityToolkit.VectorData.SqliteVec` 1.0.3-preview ;
- `nomic-embed-text-v2-moe` avec 768 dimensions lorsque le fournisseur actif
  est Ollama.

## 3. Architecture cible

La construction du stockage doit être déplacée dans `Infrastructure`, puis le
lab doit recevoir l'abstraction `VectorStore` par injection de dépendances.

```text
Program.cs
    |
    +-- Infrastructure/DependencyInjection.cs
            |-- IEmbeddingGenerator
            |-- PostgresVectorStore
            +-- RAGLabs
                    |
                    +-- VectorStoreWriter
                    +-- IngestionPipeline
                    +-- SearchAsync
```

Cette organisation respecte les règles du projet :

- `Program.cs` reste un composition root minimal ;
- la construction du client PostgreSQL reste dans `Infrastructure` ;
- `Presentation` dépend de `VectorStore`, pas du connecteur PostgreSQL ;
- aucun repository ou niveau d'abstraction supplémentaire n'est nécessaire.

## 4. Prérequis

Installer ou rendre disponibles :

1. PostgreSQL ;
2. l'extension `pgvector` sur le serveur PostgreSQL ;
3. Ollama, si Ollama reste le fournisseur actif ;
4. les modèles Ollama configurés dans `appsettings.json`.

Vérifier les modèles locaux :

```powershell
ollama list
```

La configuration actuelle attend notamment :

```text
phi4-mini:latest
nomic-embed-text-v2-moe
```

Le modèle d'embedding et sa dimension doivent rester cohérents. Avec la
configuration actuelle, la colonne vectorielle aura 768 dimensions.

## 5. Créer PostgreSQL avec pgvector

### 5.1 Option recommandée pour le lab : Docker

Créer un fichier `compose.pgvector.yml` à la racine du dépôt :

```yaml
services:
  postgres:
    image: pgvector/pgvector:pg17
    container_name: dotnet-ai-postgres
    restart: unless-stopped
    environment:
      POSTGRES_DB: dotnet_ai_labs
      POSTGRES_USER: dotnet_ai
      POSTGRES_PASSWORD: change-me-for-local-development
    ports:
      - "5432:5432"
    volumes:
      - dotnet-ai-postgres-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U dotnet_ai -d dotnet_ai_labs"]
      interval: 5s
      timeout: 5s
      retries: 10

volumes:
  dotnet-ai-postgres-data:
```

Le mot de passe ci-dessus est uniquement un exemple de développement local. Il
ne doit pas être réutilisé dans un environnement partagé.

Démarrer le service :

```powershell
docker compose -f compose.pgvector.yml up -d
docker compose -f compose.pgvector.yml ps
```

### 5.2 Option PostgreSQL déjà installé

Créer une base et un utilisateur dédiés avec les outils d'administration
habituels, puis exécuter la commande suivante dans la base du lab :

```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

L'extension s'appelle `vector` dans SQL, même si le projet est nommé
`pgvector`.

### 5.3 Vérifier l'extension

Depuis le conteneur proposé ci-dessus :

```powershell
docker exec -it dotnet-ai-postgres psql -U dotnet_ai -d dotnet_ai_labs -c "SELECT extname, extversion FROM pg_extension WHERE extname = 'vector';"
```

La requête doit retourner une ligne dont `extname` vaut `vector`.

## 6. Modifier les packages NuGet

Modifier `DotNetIALabs/DotNetIALabs.csproj`.

Supprimer :

```xml
<PackageReference Include="CommunityToolkit.VectorData.SqliteVec"
                  Version="1.0.3-preview" />
```

Ajouter :

```xml
<PackageReference Include="CommunityToolkit.VectorData.PgVector"
                  Version="1.0.1" />
```

La version 1.0.1 cible .NET 10 et dépend de
`Microsoft.Extensions.VectorData.Abstractions >= 10.8.2`. Elle est compatible
avec les versions actuellement utilisées par le projet.

Restaurer ensuite les packages :

```powershell
dotnet restore .\DotNetIALabs.slnx
```

Si SQLite doit rester disponible pour comparer les deux labs, conserver son
package et ajouter simplement le package PostgreSQL. Si la migration remplace
définitivement SQLite dans ce lab, retirer le package SQLite évite une
dépendance devenue inutile.

## 7. Configurer la chaîne de connexion

### 7.1 Ajouter la clé non secrète dans `appsettings.json`

Ajouter une section racine `ConnectionStrings` sans mot de passe :

```json
{
  "ConnectionStrings": {
    "VectorStore": ""
  },
  "AI": {
    "Provider": "Ollama"
  }
}
```

Il ne faut pas remplacer le contenu de la section `AI` existante par cet
extrait : seule la nouvelle section `ConnectionStrings` doit être ajoutée.

### 7.2 Stocker la valeur réelle dans les User Secrets

Depuis le dossier du projet :

```powershell
cd .\DotNetIALabs
dotnet user-secrets set "ConnectionStrings:VectorStore" "Host=localhost;Port=5432;Database=dotnet_ai_labs;Username=dotnet_ai;Password=change-me-for-local-development"
```

Vérifier uniquement la présence des clés :

```powershell
dotnet user-secrets list
```

Ne jamais copier une chaîne contenant le mot de passe dans :

- `appsettings.json` ;
- le code C# ;
- Git ;
- les logs de l'application.

Une variable d'environnement peut être utilisée à la place :

```powershell
$env:ConnectionStrings__VectorStore = "Host=localhost;Port=5432;Database=dotnet_ai_labs;Username=dotnet_ai;Password=change-me-for-local-development"
```

Les deux caractères de soulignement correspondent au séparateur `:` de la
configuration .NET.

## 8. Enregistrer PostgreSQL dans l'injection de dépendances

Modifier `DotNetIALabs/Infrastructure/DependencyInjection.cs`.

### 8.1 Ajouter le namespace du connecteur

```csharp
using CommunityToolkit.VectorData.PgVector;
```

### 8.2 Lire et valider la chaîne de connexion

Dans `AddInfrastructure`, après l'enregistrement de
`IEmbeddingGenerator<string, Embedding<float>>`, ajouter :

```csharp
string vectorStoreConnectionString =
    configuration.GetConnectionString("VectorStore")
    ?? throw new InvalidOperationException(
        "La chaîne de connexion 'ConnectionStrings:VectorStore' est absente.");

if (string.IsNullOrWhiteSpace(vectorStoreConnectionString))
{
    throw new InvalidOperationException(
        "La chaîne de connexion 'ConnectionStrings:VectorStore' est vide.");
}

services.AddPostgresVectorStore(vectorStoreConnectionString);
```

Le connecteur retrouve automatiquement le
`IEmbeddingGenerator<string, Embedding<float>>` déjà enregistré dans le
conteneur. Avec la surcharge prenant une chaîne de connexion, il configure
également le support pgvector de `Npgsql` ; il n'est pas nécessaire d'appeler
manuellement `UseVector()`.

L'extrait complet de la zone concernée devient :

```csharp
services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
    CreateEmbeddingGenerator);

string vectorStoreConnectionString =
    configuration.GetConnectionString("VectorStore")
    ?? throw new InvalidOperationException(
        "La chaîne de connexion 'ConnectionStrings:VectorStore' est absente.");

if (string.IsNullOrWhiteSpace(vectorStoreConnectionString))
{
    throw new InvalidOperationException(
        "La chaîne de connexion 'ConnectionStrings:VectorStore' est vide.");
}

services.AddPostgresVectorStore(vectorStoreConnectionString);

services.AddSingleton<Helpers>();
```

Cette validation ne réalise aucun appel réseau. La connexion réelle sera
ouverte au moment où le lab utilisera le store.

> Remarque : cette configuration rend PostgreSQL obligatoire au démarrage de
> toute l'application, même pour les labs précédents. Pour conserver chaque lab
> totalement indépendant, l'enregistrement peut être déplacé dans une méthode
> dédiée au lab ou la chaîne peut être validée lors de la résolution de
> `RAGLabs`. Pour la migration complète demandée ici, l'enregistrement central
> ci-dessus reste le changement le plus simple.

## 9. Injecter le VectorStore dans `RAGLabs`

Modifier `DotNetIALabs/Presentation/Labs/RAGLabs.cs`.

### 9.1 Supprimer le namespace SQLite

Supprimer :

```csharp
using CommunityToolkit.VectorData.SqliteVec;
```

`Microsoft.Extensions.VectorData` est déjà importé et fournit le type abstrait
`VectorStore`.

### 9.2 Modifier le constructeur primaire

Avant :

```csharp
public sealed class RAGLabs(
    IChatClient chatClient,
    IOptions<AiOptions> aiOptions,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
```

Après :

```csharp
public sealed class RAGLabs(
    IChatClient chatClient,
    IOptions<AiOptions> aiOptions,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    VectorStore vectorStore)
```

Le lab ne connaît ainsi ni `PostgresVectorStore`, ni `Npgsql`, ni la chaîne de
connexion.

### 9.3 Supprimer la construction SQLite

Supprimer entièrement :

```csharp
using SqliteVectorStore vectorStore = new(
    "Data Source=vectors.db;Pooling=false",
    new()
    {
        EmbeddingGenerator = embeddingGenerator
    });
```

Ne pas ajouter de `using` ou de `Dispose` autour du `vectorStore` injecté : son
cycle de vie appartient au conteneur d'injection de dépendances.

### 9.4 Configurer explicitement la métrique et l'index

Remplacer la création actuelle du writer par :

```csharp
using VectorStoreWriter<string> writer = new(
    vectorStore,
    dimensionCount: Helpers.GetActiveEmbeddingDimensions(aiOptions.Value),
    new VectorStoreWriterOptions
    {
        CollectionName = CollectionName,
        DistanceFunction = DistanceFunction.CosineDistance,
        IndexKind = IndexKind.Hnsw
    });
```

Cette configuration apporte deux garanties :

- le sens du score est explicite : avec `CosineDistance`, le score le plus bas
  est le plus pertinent ;
- PostgreSQL crée un index HNSW pour accélérer les recherches lorsque la
  collection est créée.

HNSW est limité à 2 000 dimensions par ce connecteur. Les 768 dimensions de
`nomic-embed-text-v2-moe` sont compatibles.

Pour réingérer régulièrement les mêmes documents, l'option suivante peut aussi
être ajoutée :

```csharp
IncrementalIngestion = true
```

Elle demande au writer de remplacer les chunks connus d'un document au lieu de
les accumuler. Il est préférable de l'activer si le lab est exécuté plusieurs
fois sur le même corpus.

### 9.5 Résultat attendu dans `RAGLabs.cs`

La partie stockage doit finalement ressembler à ceci :

```csharp
// Le VectorStore PostgreSQL est injecté dans le constructeur de RAGLabs.
using VectorStoreWriter<string> writer = new(
    vectorStore,
    dimensionCount: Helpers.GetActiveEmbeddingDimensions(aiOptions.Value),
    new VectorStoreWriterOptions
    {
        CollectionName = CollectionName,
        DistanceFunction = DistanceFunction.CosineDistance,
        IndexKind = IndexKind.Hnsw,
        IncrementalIngestion = true
    });

using IngestionPipeline<string> pipeline = new(
    reader,
    chunker,
    writer,
    loggerFactory: loggerFactory)
{
    DocumentProcessors = { imageAlternativeTextEnricher },
    ChunkProcessors = { summaryEnricher }
};
```

La boucle de recherche ne change pas :

```csharp
VectorStoreCollection<object, Dictionary<string, object?>> collection =
    writer.VectorStoreCollection;

await foreach (VectorSearchResult<Dictionary<string, object?>> result in
    collection.SearchAsync(
        searchValue,
        top: 3,
        cancellationToken: cancellationToken))
{
    Console.WriteLine(
        $"Distance: {result.Score}\n\tcontent: {result.Record["content"]}");
}
```

Le libellé `Distance` est recommandé à la place de `Score`, car une distance
faible est meilleure avec `CosineDistance`.

## 10. Mettre à jour le menu du lab

Dans `DotNetIALabs/Presentation/ConsoleChatRunner.cs`, remplacer :

```csharp
Console.WriteLine("25. Data Ingestion sample using SQLite vector store");
```

par :

```csharp
Console.WriteLine("25. Data Ingestion sample using PostgreSQL/pgvector");
```

## 11. Compiler et exécuter

Depuis la racine du dépôt :

```powershell
dotnet build .\DotNetIALabs.slnx
dotnet run --project .\DotNetIALabs\DotNetIALabs.csproj
```

Dans le menu, sélectionner le lab `25`.

Le premier passage doit :

1. lire les fichiers `Data/*.md` ;
2. créer les chunks ;
3. générer leurs résumés et embeddings ;
4. créer la table correspondant à `data-with-summary-v1` ;
5. insérer les enregistrements ;
6. ouvrir la boucle de recherche.

La création initiale peut être plus lente, car les embeddings et l'index HNSW
sont générés au premier passage.

## 12. Vérifier les données dans PostgreSQL

La collection conserve actuellement le nom :

```csharp
private const string CollectionName = "data-with-summary-v1";
```

À cause des tirets, le nom de table doit être placé entre guillemets doubles
dans les commandes SQL.

Lister les tables :

```sql
\dt
```

Afficher le schéma réellement généré par le writer :

```sql
\d "data-with-summary-v1"
```

Compter les chunks :

```sql
SELECT COUNT(*)
FROM "data-with-summary-v1";
```

Afficher quelques lignes :

```sql
SELECT *
FROM "data-with-summary-v1"
LIMIT 5;
```

Ces commandes peuvent être exécutées depuis le conteneur :

```powershell
docker exec -it dotnet-ai-postgres psql -U dotnet_ai -d dotnet_ai_labs
```

Il faut utiliser `\d` avant d'écrire des requêtes ciblant des colonnes, car les
noms exacts sont déterminés par le schéma par défaut de `VectorStoreWriter`.

## 13. Vérifier fonctionnellement la recherche

Poser une question liée à `Data/sample.md`, par exemple :

```text
what is data ingestion?
```

Le chunk contenant la définition de l'ingestion doit apparaître dans les
premiers résultats.

Avec `CosineDistance` :

- `0` représente une correspondance très proche ;
- une valeur plus faible est meilleure ;
- le classement attendu est croissant par distance.

Exemple d'affichage :

```text
Distance: 0.21
    content: Data ingestion is the process of collecting...

Distance: 0.43
    content: These building blocks make it easy...
```

Ne pas comparer directement ces valeurs avec un autre lab configuré en
`CosineSimilarity`, où la valeur la plus élevée est la meilleure.

## 14. Cas d'erreur et diagnostic

### `extension "vector" is not available`

Le serveur PostgreSQL ne contient pas pgvector. Utiliser une distribution ou
une image incluant l'extension, puis exécuter :

```sql
CREATE EXTENSION vector;
```

### `type "vector" does not exist`

L'extension n'a pas été activée dans la base indiquée par la chaîne de
connexion. Une extension PostgreSQL doit être activée séparément dans chaque
base.

### `password authentication failed`

Vérifier l'utilisateur, le mot de passe et la base dans le User Secret
`ConnectionStrings:VectorStore`.

### `Connection refused`

Vérifier :

```powershell
docker compose -f compose.pgvector.yml ps
docker logs dotnet-ai-postgres
```

Vérifier aussi le port `5432` et l'hôte de la chaîne de connexion.

### Erreur de dimension des vecteurs

La dimension définie lors de la création de la table ne correspond pas à la
sortie du modèle d'embedding. Pour Ollama, vérifier :

```json
"EmbeddingModel": "nomic-embed-text-v2-moe",
"EmbeddingDimensions": 768
```

Après un changement de modèle ou de dimension, utiliser une nouvelle collection
ou supprimer/recréer explicitement la table de test, puis réingérer tous les
documents. La suppression d'une table détruit les données qu'elle contient et
ne doit pas être automatisée dans le lab.

### La table existe avec un ancien schéma

Le nom de collection actuel contient une version : `data-with-summary-v1`. Si
le schéma change, créer par exemple `data-with-summary-v2` permet de réingérer
sans supprimer immédiatement la table précédente.

### Résultats incohérents après plusieurs exécutions

Activer :

```csharp
IncrementalIngestion = true
```

Puis vérifier le nombre de lignes par document. Cette option remplace les chunks
associés au document réingéré.

## 15. Stratégie de retour arrière

La migration n'a pas besoin de supprimer `vectors.db`. Pour revenir à SQLite :

1. remettre le package `CommunityToolkit.VectorData.SqliteVec` ;
2. retirer l'enregistrement `AddPostgresVectorStore` ;
3. retirer `VectorStore` du constructeur de `RAGLabs` ;
4. restaurer la construction de `SqliteVectorStore` ;
5. remettre le libellé SQLite dans le menu.

La base PostgreSQL et le fichier SQLite sont indépendants. Les données doivent
être réingérées lorsqu'on change de backend ; copier le fichier SQLite ne migre
pas ses vecteurs vers PostgreSQL.

## 16. Checklist de migration

- [ ] PostgreSQL est démarré.
- [ ] L'extension `vector` est activée dans `dotnet_ai_labs`.
- [ ] `CommunityToolkit.VectorData.PgVector` 1.0.1 est référencé.
- [ ] Le package SQLite est retiré si le lab ne l'utilise plus.
- [ ] `ConnectionStrings:VectorStore` est stockée dans les User Secrets ou dans
      une variable d'environnement.
- [ ] `AddPostgresVectorStore` est appelé dans `Infrastructure`.
- [ ] `RAGLabs` reçoit `VectorStore` par injection.
- [ ] La construction de `SqliteVectorStore` est supprimée du lab.
- [ ] La dimension reste cohérente avec le modèle d'embedding.
- [ ] `CosineDistance` et `Hnsw` sont configurés explicitement.
- [ ] Le menu mentionne PostgreSQL/pgvector.
- [ ] La solution compile.
- [ ] L'ingestion crée la table et écrit des chunks.
- [ ] La recherche retourne le chunk pertinent avec la distance la plus faible.

## 17. Références

- [Connecteur PostgreSQL pour Microsoft.Extensions.VectorData](https://learn.microsoft.com/en-us/semantic-kernel/concepts/vector-store-connectors/out-of-the-box-connectors/postgres-connector?pivots=programming-language-csharp)
- [Package CommunityToolkit.VectorData.PgVector 1.0.1](https://www.nuget.org/packages/CommunityToolkit.VectorData.PgVector/1.0.1)
- [Projet pgvector](https://github.com/pgvector/pgvector)
- [Pilote pgvector-dotnet](https://github.com/pgvector/pgvector-dotnet)


