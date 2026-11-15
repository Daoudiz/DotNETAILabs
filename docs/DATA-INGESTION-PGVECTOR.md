# Flux d'ingestion des données avec PostgreSQL/pgvector

## 1. Objectif du document

Ce document explique le fonctionnement du lab `RunDataIngestionPipeline` :

1. le flux complet d'ingestion des documents ;
2. le rôle de PostgreSQL et de l'extension pgvector ;
3. le fonctionnement de la recherche vectorielle ;
4. la nature de l'erreur `The collection has not been initialized yet` ;
5. les causes réelles de cette erreur ;
6. les corrections intégrées dans le projet.

Le lab utilise les abstractions suivantes :

- `Microsoft.Extensions.AI` pour le chat et les embeddings ;
- `Microsoft.Extensions.DataIngestion` pour construire le pipeline ;
- `Microsoft.Extensions.VectorData` pour manipuler un stockage vectoriel ;
- `CommunityToolkit.VectorData.PgVector` comme implémentation PostgreSQL ;
- Ollama pour le modèle de chat et le modèle d'embedding.

## 2. Vue d'ensemble du flux

Le flux général est le suivant :

```text
Fichiers Markdown
       |
       v
MarkdownReader
       |
       v
Document d'ingestion
       |
       +--> ImageAlternativeTextEnricher
       |
       v
SemanticSimilarityChunker
       |
       v
Chunks de texte
       |
       +--> SingleChunkSummaryEnricher
       |
       v
VectorStoreWriter
       |
       +--> IEmbeddingGenerator
       |         |
       |         v
       |    Vecteurs de 768 dimensions
       |
       v
PostgreSQL + pgvector
       |
       v
Recherche par distance cosinus
```

Chaque composant a une responsabilité précise. Le pipeline orchestre ces
composants dans l'ordre, mais ne remplace pas leur traitement individuel.

## 3. Étape 1 : localisation des documents

Les documents du lab se trouvent dans le dossier :

```text
Data/
```

Le projet contient notamment :

```text
Data/Benefits.md
Data/sample.md
```

Le code ne suppose plus que l'exécutable se trouve toujours dans
`bin/Debug/net10.0`. Il recherche le dossier `Data` en remontant l'arborescence
depuis :

- `AppContext.BaseDirectory` ;
- le répertoire de travail courant.

Extrait simplifié :

```csharp
private static DirectoryInfo FindDataDirectory()
{
    string[] startPaths =
    [
        AppContext.BaseDirectory,
        Directory.GetCurrentDirectory()
    ];

    foreach (string startPath in startPaths.Distinct(
        StringComparer.OrdinalIgnoreCase))
    {
        DirectoryInfo? currentDirectory = new(startPath);

        while (currentDirectory is not null)
        {
            DirectoryInfo dataDirectory = new(
                Path.Combine(currentDirectory.FullName, "Data"));

            if (dataDirectory.Exists &&
                dataDirectory.EnumerateFiles("*.md").Any())
            {
                return dataDirectory;
            }

            currentDirectory = currentDirectory.Parent;
        }
    }

    throw new DirectoryNotFoundException(
        "The Data directory containing Markdown documents could not be found.");
}
```

Cette recherche fonctionne avec une sortie Visual Studio, une sortie de build
personnalisée ou un lancement depuis la racine du dépôt.

## 4. Étape 2 : lecture des documents Markdown

Le reader utilisé est `MarkdownReader` :

```csharp
IngestionDocumentReader reader = new MarkdownReader();
```

Son rôle est de :

- ouvrir chaque fichier correspondant au filtre `*.md` ;
- lire sa structure Markdown ;
- produire un `IngestionDocument` ;
- conserver le contenu et les métadonnées nécessaires aux étapes suivantes.

Le pipeline traite tous les fichiers avec :

```csharp
pipeline.ProcessAsync(
    repository,
    searchPattern: "*.md",
    cancellationToken: cancellationToken)
```

L'utilisation de `await foreach` permet de traiter les résultats de manière
asynchrone et de propager le `CancellationToken`.

## 5. Étape 3 : enrichissement des documents

Le pipeline configure un `ImageAlternativeTextEnricher` :

```csharp
IngestionDocumentProcessor imageAlternativeTextEnricher =
    new ImageAlternativeTextEnricher(enricherOptions);
```

Ce processor intervient avant le découpage. Son objectif est de produire une
description textuelle des images lorsque le document en contient.

Une image ne peut pas être comparée directement avec une question textuelle
dans ce pipeline. Sa description devient donc du texte exploitable par le
chunker et le générateur d'embeddings.

## 6. Étape 4 : découpage en chunks

Un document complet peut être trop long et contenir plusieurs sujets. Il est
donc découpé en blocs plus petits appelés **chunks**.

Le lab utilise :

```csharp
Tokenizer tokenizer = TiktokenTokenizer.CreateForEncoding("o200k_base");

IngestionChunkerOptions chunkerOptions = new(tokenizer)
{
    MaxTokensPerChunk = 400,
    OverlapTokens = 0
};

IngestionChunker<string> chunker =
    new SemanticSimilarityChunker(embeddingGenerator, chunkerOptions);
```

### Pourquoi utiliser des chunks ?

Les chunks permettent :

- d'obtenir une représentation vectorielle plus précise ;
- de retrouver seulement le passage utile d'un document ;
- de limiter la quantité de texte retournée à un futur système RAG ;
- d'éviter qu'un thème dominant masque les autres thèmes du document.

### Paramètres actuels

`MaxTokensPerChunk = 400` limite chaque chunk à environ 400 tokens.

`OverlapTokens = 0` signifie que deux chunks consécutifs ne partagent pas de
tokens. Un chevauchement peut améliorer la continuité entre les passages, mais
il augmente aussi la quantité de données stockées.

Le `SemanticSimilarityChunker` utilise les embeddings pour rechercher des
frontières sémantiques, au lieu de découper uniquement après un nombre fixe de
caractères.

## 7. Étape 5 : génération d'un résumé par chunk

Chaque chunk est envoyé séparément au modèle de chat :

```csharp
ChatResponse response = await chatClient.GetResponseAsync(
    $"""
    Summarize the following text in one concise paragraph.
    Return only the summary text, without JSON, a list, or commentary.

    {chunk.Content}
    """,
    chatOptions,
    cancellationToken);
```

Le résumé est enregistré dans les métadonnées :

```csharp
chunk.Metadata["summary"] = response.Text.Trim();
```

Le processor personnalisé `SingleChunkSummaryEnricher` traite un seul chunk à
la fois. Cette stratégie évite d'attendre une sortie structurée contenant un
nombre exact de résumés pour un lot de chunks.

Si la génération du résumé échoue, l'exception est journalisée et le chunk est
quand même transmis au writer. Le résumé enrichit donc le contenu sans rendre
toute l'ingestion dépendante de sa réussite.

## 8. Étape 6 : génération des embeddings

Un embedding est un tableau de nombres représentant le sens d'un texte.

Dans la configuration Ollama actuelle :

```json
"EmbeddingModel": "nomic-embed-text-v2-moe",
"EmbeddingDimensions": 768
```

Le flux de conversion est :

```text
"Data ingestion is the process..."
                  |
                  v
       nomic-embed-text-v2-moe
                  |
                  v
[0.021, -0.118, 0.304, ..., 0.087]
             768 valeurs
```

Deux textes sémantiquement proches produisent généralement des vecteurs proches
dans cet espace de 768 dimensions.

Le générateur est enregistré comme singleton :

```csharp
services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
    CreateEmbeddingGenerator);
```

Il est ensuite fourni explicitement au connecteur PostgreSQL :

```csharp
services.AddPostgresVectorStore(
    _ => vectorStoreConnectionString,
    serviceProvider => new PostgresVectorStoreOptions
    {
        EmbeddingGenerator = serviceProvider.GetRequiredService<
            IEmbeddingGenerator<string, Embedding<float>>>()
    });
```

Cette configuration est indispensable lorsque le schéma dynamique du writer
déclare le champ vectoriel à partir du texte.

## 9. Étape 7 : écriture avec VectorStoreWriter

Le writer relie le pipeline d'ingestion au stockage vectoriel :

```csharp
using VectorStoreWriter<string> writer = new(
    vectorStore,
    dimensionCount: Helpers.GetActiveEmbeddingDimensions(aiOptions.Value),
    new VectorStoreWriterOptions
    {
        CollectionName = "data-with-summary-v1",
        DistanceFunction = DistanceFunction.CosineDistance,
        IndexKind = IndexKind.Hnsw,
        IncrementalIngestion = true
    });
```

Ses responsabilités sont les suivantes :

1. définir le schéma de la collection ;
2. demander la création de la collection si elle n'existe pas ;
3. générer les embeddings à partir du contenu ;
4. écrire les chunks et leurs métadonnées ;
5. remplacer les anciens chunks du document en ingestion incrémentale.

### Dimension

`dimensionCount` doit être identique à la sortie du modèle d'embedding. Une
table créée avec `VECTOR(768)` ne peut pas recevoir un vecteur de 1 536
dimensions.

### Ingestion incrémentale

Avec :

```csharp
IncrementalIngestion = true
```

une nouvelle ingestion d'un document remplace ses anciens chunks. Cela évite
d'accumuler des doublons lorsque le lab est exécuté plusieurs fois.

## 10. Rôle de PostgreSQL et de pgvector

PostgreSQL stocke les données relationnelles classiques :

- identifiant du chunk ;
- contenu ;
- identifiant du document ;
- métadonnées ;
- résumé.

L'extension pgvector ajoute un type de colonne spécialisé :

```sql
VECTOR(768)
```

Elle ajoute également les opérateurs et les index nécessaires à la recherche
de voisins vectoriels.

L'extension doit être installée sur le serveur puis activée dans chaque base :

```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

Le package .NET `CommunityToolkit.VectorData.PgVector` n'installe pas
l'extension PostgreSQL. Il fournit uniquement le connecteur entre
`Microsoft.Extensions.VectorData` et un serveur PostgreSQL déjà équipé de
pgvector.

## 11. Collection, table et index HNSW

Dans les abstractions .NET, le stockage est appelé **collection**. Dans le
connecteur PostgreSQL, cette collection correspond à une table.

Le lab utilise :

```text
data-with-summary-v1
```

Le writer demande également un index :

```csharp
IndexKind = IndexKind.Hnsw
```

HNSW est un index de recherche approximative des voisins les plus proches. Il
accélère la recherche lorsque le nombre de vecteurs augmente, avec un compromis
entre vitesse, mémoire et exactitude.

Pour ce connecteur, HNSW supporte jusqu'à 2 000 dimensions. Le modèle actuel à
768 dimensions respecte cette limite.

## 12. Étape 8 : recherche vectorielle

Après une ingestion réussie, le writer expose la collection :

```csharp
VectorStoreCollection<object, Dictionary<string, object?>> collection =
    writer.VectorStoreCollection;
```

Une question utilisateur est ensuite envoyée à :

```csharp
collection.SearchAsync(
    searchValue,
    top: 3,
    cancellationToken: cancellationToken)
```

Le connecteur effectue les opérations suivantes :

1. convertir la question en embedding avec le même modèle ;
2. comparer ce vecteur avec les embeddings stockés ;
3. classer les chunks par distance ;
4. retourner les trois meilleurs résultats.

## 13. Interprétation de la distance

Le lab configure :

```csharp
DistanceFunction = DistanceFunction.CosineDistance
```

Avec une distance cosinus :

- une valeur faible correspond à un résultat proche ;
- une valeur proche de `0` est généralement très pertinente ;
- la plus petite distance représente le meilleur résultat.

Le programme affiche donc :

```text
Distance: 0.21
    content: Data ingestion is the process...
```

Il ne faut pas confondre cette distance avec une similarité cosinus. Avec une
similarité, la valeur la plus élevée serait la meilleure.

## 14. Nature de l'erreur observée

L'erreur initiale était :

```text
The collection has not been initialized yet. Call WriteAsync first
```

Cette erreur était une **conséquence**, et non la première cause du problème.

`VectorStoreWriter.VectorStoreCollection` n'est initialisé qu'au premier appel
réussi de :

```csharp
writer.WriteAsync(...)
```

L'ancien flux faisait ceci :

```text
Tentative d'ingestion
       |
       v
Échec avant ou pendant WriteAsync
       |
       v
Aucune collection initialisée
       |
       v
Accès à writer.VectorStoreCollection
       |
       v
"The collection has not been initialized yet"
```

Le message final masquait donc l'erreur survenue pendant le traitement des
documents.

## 15. Première cause : aucun document trouvé

L'ancien code supposait une structure de sortie fixe :

```csharp
Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..");
```

Ce calcul pouvait pointer vers un mauvais dossier si l'application était
lancée depuis une sortie personnalisée ou après publication.

Conséquence :

```text
Aucun fichier trouvé
    -> aucun document lu
    -> aucun chunk créé
    -> WriteAsync jamais appelé
    -> collection non initialisée
```

### Correction

Le lab utilise maintenant `FindDataDirectory()` pour rechercher réellement le
dossier contenant des fichiers `*.md`.

Il affiche également le chemin utilisé :

```text
Reading documents from 'D:\Formations\DotNETAILabs\Data'.
```

## 16. Deuxième cause : générateur d'embeddings absent du connecteur

Après correction du chemin, l'erreur réelle suivante est apparue :

```text
Vector property 'embedding' has type 'string' which isn't supported by your
provider, and no embedding generator is configured.
```

Le writer fournissait une valeur textuelle pour la propriété `embedding`, mais
le connecteur PostgreSQL ne savait pas comment la convertir en vecteur.

Le générateur existait dans le conteneur d'injection de dépendances, mais la
configuration initiale :

```csharp
services.AddPostgresVectorStore(vectorStoreConnectionString);
```

ne le transmettait pas explicitement aux options utilisées dans cette version
du connecteur.

### Correction

Le générateur est maintenant résolu par la factory d'options :

```csharp
services.AddPostgresVectorStore(
    _ => vectorStoreConnectionString,
    serviceProvider => new PostgresVectorStoreOptions
    {
        EmbeddingGenerator = serviceProvider.GetRequiredService<
            IEmbeddingGenerator<string, Embedding<float>>>()
    });
```

Le flux devient alors :

```text
Chunk textuel
    -> IEmbeddingGenerator
    -> Embedding<float> de 768 dimensions
    -> pgvector
```

## 17. Troisième problème révélé : authentification PostgreSQL

Après les deux corrections précédentes, l'exécution atteint correctement
PostgreSQL. Le serveur retourne maintenant :

```text
28P01: authentification par mot de passe échouée
```

Cette erreur signifie que :

- le dossier `Data` est trouvé ;
- les documents sont lus ;
- le writer est exécuté ;
- le générateur d'embeddings est configuré ;
- le connecteur atteint le serveur PostgreSQL ;
- mais PostgreSQL refuse l'utilisateur ou son mot de passe.

Il s'agit d'un problème de configuration externe, pas d'un défaut du pipeline.

Pour ce lab local, la chaîne est configurée dans `appsettings.json` :

```json
"ConnectionStrings": {
  "VectorStore": "Host=localhost;Port=5432;Database=raglabdb;Username=raguser;Password=<mot-de-passe-correct>"
}
```

Cette configuration simplifie l'exécution pédagogique du lab, mais rend le mot
de passe versionnable. Elle ne doit pas être réutilisée pour un environnement
partagé ou de production. Dans ces environnements, utiliser une variable
d'environnement, un gestionnaire de secrets ou une identité managée.

## 18. Validation ajoutée après le pipeline

Le code compte maintenant les documents traités et réussis :

```csharp
int processedDocumentCount = 0;
int successfulDocumentCount = 0;
```

Après le pipeline :

```csharp
if (processedDocumentCount == 0)
{
    throw new InvalidOperationException(
        $"No Markdown document was found in '{repository.FullName}'.");
}

if (successfulDocumentCount == 0)
{
    throw new InvalidOperationException(
        "No document was ingested successfully. Check the preceding logs.");
}
```

La collection n'est consultée qu'après au moins une ingestion réussie.

Cette validation ne répare pas une erreur PostgreSQL. Elle empêche simplement
une erreur secondaire de masquer la cause réelle visible dans les logs.

## 19. Flux corrigé

Le flux corrigé est maintenant :

```text
Recherche robuste du dossier Data
              |
              v
Lecture des fichiers Markdown
              |
              v
Découpage en chunks
              |
              v
Résumé de chaque chunk
              |
              v
Générateur d'embeddings explicitement configuré
              |
              v
Connexion PostgreSQL
              |
              v
Création ou ouverture de la collection pgvector
              |
              v
Écriture des chunks
              |
              v
Vérification d'au moins une ingestion réussie
              |
              v
Recherche vectorielle
```

## 20. Vérifications à effectuer

### 20.1 Vérifier l'extension

```sql
SELECT name, default_version, installed_version
FROM pg_available_extensions
WHERE name = 'vector';
```

Puis :

```sql
SELECT extname, extversion
FROM pg_extension
WHERE extname = 'vector';
```

Si l'extension est disponible mais non activée :

```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

### 20.2 Vérifier les identifiants

Tester la connexion indépendamment du lab :

```powershell
psql -h localhost -p 5432 -U raguser -d raglabdb
```

### 20.3 Vérifier le modèle Ollama

```powershell
ollama list
```

Le modèle `nomic-embed-text-v2-moe` doit être disponible.

### 20.4 Vérifier la compilation

```powershell
dotnet build .\DotNetIALabs.slnx
```

### 20.5 Vérifier fonctionnellement le lab

1. lancer l'application ;
2. sélectionner le lab `25` ;
3. vérifier que les deux documents sont indiqués comme réussis ;
4. poser une question liée aux documents ;
5. vérifier que les résultats sont classés par distance croissante.

Exemple :

```text
what is data ingestion?
```

## 21. Résumé

Le message sur la collection non initialisée était une erreur secondaire. La
collection n'existait pas encore parce qu'aucune écriture n'avait réussi.

Les corrections principales sont :

1. rechercher le dossier `Data` sans dépendre du chemin du binaire ;
2. transmettre explicitement le générateur d'embeddings à pgvector ;
3. vérifier les résultats d'ingestion avant d'utiliser la collection ;
4. lire la chaîne PostgreSQL depuis `appsettings.json` pour ce lab local ;
5. afficher une distance, puisque le lab utilise `CosineDistance`.

Après ces corrections, le code atteint PostgreSQL correctement. Une erreur
`28P01` restante doit être corrigée en fournissant les identifiants réels de la
base, puis en vérifiant que l'extension `vector` est activée.

