using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using DotNetIALabs.Configuration;
using DotNetIALabs.Models;
using System.Reflection.Metadata.Ecma335;


namespace DotNetIALabs.Presentation.Labs
{
    public sealed class ChatLabs (
        IChatClient chatClient,
        IOptions<AiOptions> aiOptions,
        IHostEnvironment environment)
    {
        private readonly GenerationOptions _generation = aiOptions.Value.Generation;

        public async Task RunSimpleCallAsync(CancellationToken cancellationToken)
        {
            string solutionRoot = Path.GetFullPath(
           Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

            string filePath = Path.Combine(solutionRoot, "Data", "Benefits.md");

            string text = await File.ReadAllTextAsync(filePath, cancellationToken);

            string prompt = $""" Summarize this text in 20 words or less :{text} """;

            ChatResponse response = await chatClient.GetResponseAsync(prompt, CreateChatOptions(), cancellationToken);

            Console.WriteLine(response.Text);
        }

        public async Task RunConversationAsync(CancellationToken cancellationToken)
        {
            List<ChatMessage> history = [];

            Console.WriteLine(
                "Entrez votre message, ou 'bye' pour terminer la conversation.");

            while (!cancellationToken.IsCancellationRequested)
            {
                Console.Write("Vous : ");
                string? userPrompt = await Console.In.ReadLineAsync(cancellationToken);

                if (userPrompt is null
                    || userPrompt.Trim().Equals(
                        "bye",
                        StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(userPrompt))
                {
                    continue;
                }

                history.Add(new ChatMessage(ChatRole.User, userPrompt));

                Console.Write("Assistant : ");
                StringBuilder response = new();

                await foreach (ChatResponseUpdate update in chatClient
                    .GetStreamingResponseAsync(
                        history,
                        CreateChatOptions(),
                        cancellationToken))
                {
                    Console.Write(update.Text);
                    response.Append(update.Text);
                }

                history.Add(new ChatMessage(
                    ChatRole.Assistant,
                    response.ToString()));

                Console.WriteLine();
            }

            Console.WriteLine("Conversation terminée.");
        }

        public async Task RunStructuredOutputAsync(
        CancellationToken cancellationToken)
        {
            string[] reviews =
            [
                "The product exceeded my expectations!",
            "This product is okay, but it didn't really meet my needs.",
            "The product is disappointing."
            ];

            foreach (string review in reviews)
            {
                List<ChatMessage> messages =
                [
                    new(
                    ChatRole.System,
                    "Classify product reviews. Return the requested structured result."),
                new(
                    ChatRole.User,
                    $"Classify this review and explain the classification: {review}")
                ];

                ChatResponse<SentimentClassification> response =
                    await chatClient.GetResponseAsync<SentimentClassification>(
                        messages,
                        options: CreateChatOptions(temperature: 0),
                        cancellationToken: cancellationToken);

                Console.WriteLine($"Avis : {review}");
                Console.WriteLine($"Sentiment : {response.Result.Sentiment}");
                Console.WriteLine($"Explication : {response.Result.Explanation}");
                Console.WriteLine();
            }
        }


       
        private ChatOptions CreateChatOptions(float? temperature = null) => new()
        {
            MaxOutputTokens = _generation.MaxOutputTokens,
            Temperature = temperature ?? _generation.Temperature
        };

       

    }
}
