using DotNetIALabs.Configuration;
using DotNetIALabs.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;
using Microsoft.ML.Tokenizers;
using System.Runtime.CompilerServices;

namespace DotNetIALabs.Presentation.Labs
{
    public sealed class RAGLabs (
            IChatClient chatClient,
            IOptions<AiOptions> aiOptions,
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
            PdfDocumentReader pdfreader,
            VectorStore vectorStore)
    {
        private const string CollectionName = "data-with-summary-v1";

        
        public async Task RunDataIngestionPipeline(CancellationToken cancellationToken)
        {
            //configure document reader
            IngestionDocumentReader reader = new MarkdownReader();
           

            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole());


            ChatOptions enrichmentChatOptions = new()
            {
                Temperature = 0,
                Seed = 42,
                MaxOutputTokens = aiOptions.Value.Generation.MaxOutputTokens
            };

            EnricherOptions enricherOptions = new(chatClient)
            {
                ChatOptions = enrichmentChatOptions,
                LoggerFactory = loggerFactory
            };

            // Configuration de l'enrechisseur du document pour produire une description textuelle des images
            IngestionDocumentProcessor imageAlternativeTextEnricher = new ImageAlternativeTextEnricher(enricherOptions);

            //configurer le chunker qui fractionne les documents en blocs sémantiques (chunks)
            Tokenizer tokenizer = TiktokenTokenizer.CreateForEncoding("o200k_base");
            IngestionChunkerOptions chunkerOptions = new(tokenizer)
            {
                MaxTokensPerChunk = 400,
                OverlapTokens = 0
            };
            IngestionChunker<string> chunker = 
                new SemanticSimilarityChunker(embeddingGenerator, chunkerOptions);

            //Configure chunk processor to generate summaries for each chunk
            IngestionChunkProcessor<string> summaryEnricher =
                new SingleChunkSummaryEnricher(
                    chatClient,
                    enrichmentChatOptions,
                    loggerFactory.CreateLogger<SingleChunkSummaryEnricher>());

            // Configure the writer with PostgreSQL/pgvector.
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


            //Compose data ingestion pipeline 
            using IngestionPipeline<string> pipeline = new(reader, chunker, writer, loggerFactory: loggerFactory)
            {
                DocumentProcessors = { imageAlternativeTextEnricher },
                ChunkProcessors = { summaryEnricher }
            };

            // Process all documents from the repository.
            DirectoryInfo repository = FindDataDirectory();
            int processedDocumentCount = 0;

            Console.WriteLine($"Reading documents from '{repository.FullName}'.");

            await foreach (IngestionResult result in pipeline.ProcessAsync(
                repository,
                searchPattern: "*.md",
                cancellationToken: cancellationToken))
            {
                processedDocumentCount++;

                Console.WriteLine(
                    $"Completed processing '{result.DocumentId}'. " +
                    $"Succeeded: {result.Succeeded}.");

                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Ingestion failed for '{result.DocumentId}'. " +
                        "Check the preceding error in the logs.");
                }
            }

            if (processedDocumentCount == 0)
            {
                throw new InvalidOperationException(
                    $"No Markdown document was found in '{repository.FullName}'.");
            }

            //Search the vector store collection and display results 
            VectorStoreCollection<object, Dictionary<string,object?>> collection = 
                writer.VectorStoreCollection;

            while(true)
            {
                Console.WriteLine("Enter your question (or 'exit' to quit :");
                string? searchValue = await Console.In.ReadLineAsync(
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(searchValue) ||
                    searchValue.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                searchValue = searchValue.Trim();

                Console.WriteLine("Searching...\n");
                await foreach (VectorSearchResult<Dictionary<string, object?>> result in 
                    collection.SearchAsync(
                        searchValue,
                        top: 3,
                        cancellationToken: cancellationToken))
                {
                    Console.WriteLine(
                        $"Distance: {result.Score}\n\tcontent: {result.Record["content"]}");
                }

            }
        }


        public async Task RunPdfDataIngestionPipeline(CancellationToken cancellationToken)
        {         


            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole());


            ChatOptions enrichmentChatOptions = new()
            {
                Temperature = 0,
                Seed = 42,
                MaxOutputTokens = aiOptions.Value.Generation.MaxOutputTokens
            };

            EnricherOptions enricherOptions = new(chatClient)
            {
                ChatOptions = enrichmentChatOptions,
                LoggerFactory = loggerFactory
            };

            // Configuration de l'enrechisseur du document pour produire une description textuelle des images
            IngestionDocumentProcessor imageAlternativeTextEnricher = new ImageAlternativeTextEnricher(enricherOptions);

            //configurer le chunker qui fractionne les documents en blocs sémantiques (chunks)
            Tokenizer tokenizer = TiktokenTokenizer.CreateForEncoding("o200k_base");
            IngestionChunkerOptions chunkerOptions = new(tokenizer)
            {
                MaxTokensPerChunk = 400,
                OverlapTokens = 0
            };
            IngestionChunker<string> chunker =
                new SemanticSimilarityChunker(embeddingGenerator, chunkerOptions);

            //Configure chunk processor to generate summaries for each chunk
            IngestionChunkProcessor<string> summaryEnricher =
                new SingleChunkSummaryEnricher(
                    chatClient,
                    enrichmentChatOptions,
                    loggerFactory.CreateLogger<SingleChunkSummaryEnricher>());

            // Configure the writer with PostgreSQL/pgvector.
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


            //Compose data ingestion pipeline 
            using IngestionPipeline<string> pipeline = new(pdfreader, chunker, writer, loggerFactory: loggerFactory)
            {
                DocumentProcessors = { imageAlternativeTextEnricher },
                ChunkProcessors = { summaryEnricher }
            };

            // Process all documents from the repository.
            DirectoryInfo repository = FindDataDirectory();
            int processedDocumentCount = 0;

            Console.WriteLine($"Reading documents from '{repository.FullName}'.");

            await foreach (IngestionResult result in pipeline.ProcessAsync(
                repository,
                searchPattern: "*.pdf",
                cancellationToken: cancellationToken))
            {
                processedDocumentCount++;

                Console.WriteLine(
                    $"Completed processing '{result.DocumentId}'. " +
                    $"Succeeded: {result.Succeeded}.");

                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Ingestion failed for '{result.DocumentId}'. " +
                        "Check the preceding error in the logs.");
                }
            }

            if (processedDocumentCount == 0)
            {
                throw new InvalidOperationException(
                    $"No Pdf document was found in '{repository.FullName}'.");
            }

            //Search the vector store collection and display results 
            VectorStoreCollection<object, Dictionary<string, object?>> collection =
                writer.VectorStoreCollection;

            while (true)
            {
                Console.WriteLine("Enter your question (or 'exit' to quit :");
                string? searchValue = await Console.In.ReadLineAsync(
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(searchValue) ||
                    searchValue.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                searchValue = searchValue.Trim();

                Console.WriteLine("Searching...\n");
                await foreach (VectorSearchResult<Dictionary<string, object?>> result in
                    collection.SearchAsync(
                        searchValue,
                        top: 3,
                        cancellationToken: cancellationToken))
                {
                    Console.WriteLine(
                        $"Distance: {result.Score}\n\tcontent: {result.Record["content"]}");
                }

            }
        }

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

        private sealed class SingleChunkSummaryEnricher(
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
    }
}
