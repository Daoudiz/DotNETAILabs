using DotNetIALabs.Configuration;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;
using Microsoft.ML.Tokenizers;
using Microsoft.SemanticKernel.Connectors.SqliteVec;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetIALabs.Presentation.Labs
{
    public sealed class RAGLabs (
            IChatClient chatClient,
            IOptions<AiOptions> aiOptions,
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {

        private readonly GenerationOptions _generation = aiOptions.Value.Generation;
        public async Task RunDataIngetionPipline(CancellationToken cancellationToken)
        {
            //configure document reader
            IngestionDocumentReader reader = new MarkdownReader();
           

            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole());


            EnricherOptions enricherOptions = new(chatClient)
            {
                LoggerFactory = loggerFactory

            };

            IngestionDocumentProcessor imageAlternativeTextEnricher = new ImageAlternativeTextEnricher(enricherOptions);

            //configurer le chunker qui fractionne les documents en blocs sémantiques (chunks)
            IngestionChunkerOptions chunkerOptions = new(TiktokenTokenizer.CreateForModel(Helpers.GetActiveModel(aiOptions.Value)))
            {
                MaxTokensPerChunk = 2000,
                OverlapTokens = 0
            };
            IngestionChunker<string> chunker = new SemanticSimilarityChunker(embeddingGenerator, chunkerOptions);

            //Configure chunk processor to generate summaries for each chunk
            IngestionChunkProcessor<string> summaryEnricher = new SummaryEnricher(enricherOptions);

            // configure SQLite Vector Store to stock embeddings vectors
            using SqliteVectorStore vectorStore = new (
                "Data Source=vectors.db;Pooling=false",
                new()
                { 
                    EmbeddingGenerator = embeddingGenerator 
                });

            // The writer requires the embeddding dimension count to be specified
            using VectorStoreWriter<string> writer = new(
                vectorStore,
                dimensionCount: Helpers.GetActiveEmbeddingDimensions(aiOptions.Value),
                new VectorStoreWriterOptions { CollectionName = "data"});

            //Compose data ingestion pipeline 
            using IngestionPipeline<string> pipeline = new(reader, chunker, writer, loggerFactory: loggerFactory)
            {
                DocumentProcessors = { imageAlternativeTextEnricher },
                ChunkProcessors = { summaryEnricher }
            };

            //Process all documents from repository
            string solutionRoot = Path.GetFullPath(
                    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

            string repositoryPath = Path.Combine(solutionRoot, "Data");
            await foreach (IngestionResult result in pipeline.ProcessAsync(
                new DirectoryInfo(repositoryPath),
                searchPattern: "*.md"))
            {
                Console.WriteLine($"Completed processing '{result.DocumentId}'." +
                    $"Succeeded: '\r\n{{result.Succeeded}}\r\n'.");
            }


            //Search the vector store collection and display results 
            VectorStoreCollection<object, Dictionary<string,object?>> collection = 
                writer.VectorStoreCollection;

            while(true)
            {
                Console.WriteLine("Enter your question (or 'exit' to quit :");
                string? searchValue = Console.ReadLine();

                if(string.IsNullOrEmpty(searchValue) || searchValue == "exit")
                {
                    break;
                }

                Console.WriteLine("Searching...\n");
                await foreach (VectorSearchResult<Dictionary<string, object?>> result in 
                    collection.SearchAsync(searchValue, top:3))
                {
                    Console.WriteLine($"Score: {result.Score}\n\tcontent: {result.Record["content"]}");
                }

            }
        }
    }
}
