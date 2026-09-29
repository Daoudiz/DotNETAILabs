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


        public async Task RunSimpleFunctionCallAsync(
            CancellationToken cancellationToken)
        {          


            List<ChatMessage> chatHistory = [new(ChatRole.System, """
                            You are a hiking enthusiast who helps people discover fun hikes in their area. You are upbeat and friendly.   
                            When the user asks about current weather, you MUST call the
                            get_current_weather tool before answering.
                            Never claim that you cannot access current weather.
                            """)];

            // Weather conversation relevant to the registered function.
            chatHistory.Add(new ChatMessage(ChatRole.User,
                "I live in Montreal and I'm looking for a moderate intensity hike. What's the current weather like?"));
            Console.WriteLine($"{chatHistory.Last().Role} >>> {chatHistory.Last()}");

            ChatResponse response = await chatClient.GetResponseAsync(chatHistory, CreateChatOptionsWithFunction(), cancellationToken: cancellationToken);
            Console.WriteLine($"Assistant >>> {response.Text}");

        }

        public async Task RunMultipleFunctionCallAsync(
            CancellationToken cancellationToken)
        {
            List<ChatMessage> chatHistory = [new(ChatRole.System, """                
                    You are a currency expert who help people find the exact value of a given currency in Moroccan Dirhams.
                    """)];

            // Weather conversation relevant to the registered function.
            chatHistory.Add(new ChatMessage(ChatRole.User,
                "I'm looking for the value of Euro in Moroccan Dirhams?"));
            Console.WriteLine($"{chatHistory.Last().Role} >>> {chatHistory.Last()}");

            ChatResponse response = await chatClient.GetResponseAsync(chatHistory, CreateChatOptionsWithMultiFunction(), cancellationToken: cancellationToken);
            Console.WriteLine($"Assistant >>> {response.Text}");

        }

        private ChatOptions CreateChatOptions(float? temperature = null) => new()
        {
            MaxOutputTokens = _generation.MaxOutputTokens,
            Temperature = temperature ?? _generation.Temperature
        };

        private ChatOptions CreateChatOptionsWithFunction() => new()
        {
            MaxOutputTokens = _generation.MaxOutputTokens,
            Temperature = _generation.Temperature,

            Tools = [AIFunctionFactory.Create((string location, string unit) =>
                 {
                    // Here you would call a weather API
                    // to get the weather for the location.
                     Console.WriteLine(
                    $"Outil météo appelé : location={location}, unit={unit}");
                    return "Periods of rain or drizzle, 15 C";
                },
                "get_current_weather",
                "Gets the current weather in a given location")]

        };

        private ChatOptions CreateChatOptionsWithMultiFunction() => new()
        {
            MaxOutputTokens = _generation.MaxOutputTokens,
            Temperature = _generation.Temperature,

            Tools = [
                
                
                AIFunctionFactory.Create((string location, string unit) =>
                 {
                    // Here you would call a weather API
                    // to get the weather for the location.
                     Console.WriteLine(
                    $"Outil météo appelé : location={location}, unit={unit}");
                    return "Periods of rain or drizzle, 15 C";
                },
                "get_current_weather",
                "Gets the current weather in a given location"),
                
                AIFunctionFactory.Create((string curruncy) =>
                 {
                    // Here you would call a currency API
                    // to get the value of the given currency in Moroccan Dirahms.
                     Console.WriteLine(
                    $"Outil devise appelé : currency={curruncy}");
                    return "The value of the currecny in Moroccan Dirhams is low";
                },
                "get_currency",
                "Gets the current value of the currency")

                ]

        };

    }
}
