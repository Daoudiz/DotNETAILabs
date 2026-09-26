# AGENTS.md — Labs .NET / IA

## Contexte

Application console .NET 10 destinée à apprendre `Microsoft.Extensions.AI` : chat, embeddings, recherche vectorielle, RAG, puis intégration Web API.

Rester monoprojet et éviter une Clean Architecture complète ou des abstractions supplémentaires avant qu'un besoin concret ne les justifie.

## Structure

```text
Program.cs                 # Composition root / Generic Host
Configuration/             # AiOptions et validation
Infrastructure/            # Clients techniques et enregistrements DI
Presentation/              # ConsoleChatRunner
appsettings.json
```

- `Program.cs` ne contient ni boucle de chat ni instanciation directe de client IA.
- La configuration et la construction des clients restent hors de `Presentation`.
- Conserver `IChatClient`, `ChatMessage`, `ChatOptions` et `ChatResponseUpdate` visibles dans les labs de chat.

## Configuration

Utiliser la section `AI` avec :

```text
AI:Provider
AI:Generation
AI:Providers:Ollama
AI:Providers:OpenAI
AI:Providers:AzureOpenAI
```

- `Provider` désigne le fournisseur actif.
- Les fournisseurs inactifs ne doivent pas bloquer la validation.
- Les clés API ne vont jamais dans `appsettings.json`, le code, Git ou les logs ; utiliser User Secrets ou des variables d'environnement.
- Valider au démarrage le fournisseur actif, le modèle ou déploiement, l'endpoint et les options de génération. Ne pas effectuer d'appel réseau dans ce validateur.

## Règles de code

- Injecter les interfaces ; centraliser les clients fournisseurs dans `Infrastructure`.
- Enregistrer les clients IA sans état en singleton.
- Utiliser les APIs asynchrones et propager `CancellationToken`.
- En streaming : `Console.Write(fragment)` ; reconstituer la réponse avec `StringBuilder` avant de l'ajouter à l'historique.
- L'historique en mémoire convient à une console mono-session ; une Web API devra isoler les conversations par utilisateur ou `conversationId`.
- Préférer des changements petits, ciblés et compatibles avec le lab en cours.

## Packages et vérification

- Conserver des versions explicites et compatibles des packages IA.
- N'ajouter un package fournisseur, embedding ou vector store qu'au début du lab qui l'utilise.
Vérifier également la disponibilité du runtime local requis avant de diagnostiquer le code.

## Progression des labs

```text
Chat -> embeddings -> vector search -> ingestion -> RAG -> PostgreSQL/pgvector -> Web API
```

Proposer l'impact architectural avant tout changement qui dépasse le lab actuel.
