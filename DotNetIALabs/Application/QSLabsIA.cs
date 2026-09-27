using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace DotNetIALabs.Application
{
    public class QSLabsIA : IQSLAbsIA
    {
        private enum Sentiment
        {
            Positive,
            Neutral,
            Negative
        }  

        public async Task Lab1SimpleIACall(IChatClient chatClient)
        {

            //construct the prompt 
            string solutionRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

            string filePath = Path.Combine(solutionRoot, "Data", "Benefits.md");
            string text = File.ReadAllText(filePath);
            string prompt = $"""
                        Summarize this text in 20 words or less : 
                        {text}
                        """;

            try
            {
                //Submit the prompt and print ou the respose
                ChatResponse response = await chatClient.GetResponseAsync(prompt, new ChatOptions { MaxOutputTokens = 400 });
                Console.WriteLine(response.Text);
            }
            catch (Exception e)
            {
                Console.Write(e.Message);
            }


        }

        public async Task Lab2SimpleIAChat(
            IChatClient chatClient,
            CancellationToken cancellationToken = default)
        {
            List<ChatMessage> chatHistory = [];

            Console.WriteLine("Entrez votre message, ou 'bye' pour terminer la conversation.");

            while (!cancellationToken.IsCancellationRequested)
            {
                Console.Write("Vous : ");
                string? userPrompt = Console.ReadLine();

                if (userPrompt is null
                    || userPrompt.Trim().Equals("bye", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(userPrompt))
                {
                    continue;
                }

                chatHistory.Add(new ChatMessage(ChatRole.User, userPrompt));

                Console.Write("Assistant : ");
                StringBuilder response = new();

                await foreach (ChatResponseUpdate item in chatClient
                    .GetStreamingResponseAsync(
                        chatHistory,
                        cancellationToken: cancellationToken))
                {
                    Console.Write(item.Text);
                    response.Append(item.Text);
                }

                chatHistory.Add(new ChatMessage(
                    ChatRole.Assistant,
                    response.ToString()));

                Console.WriteLine();
            }

            Console.WriteLine("Conversation terminée.");
        }

        public async Task Lab3StructedOutput(IChatClient chatClient)
        {
            string[] reviews = [
                "The product exceeded my expectations !",
                "This product is okay, but it didn't really meet my needs.",
                "The product is disappointing."
            ];

            var options = new ChatOptions
            {
                Temperature = 0,
                 Seed = 42
            };

            foreach (var review in reviews)
            {
              
                try
                {
                    List<ChatMessage> prompt =
                    [
                        new(ChatRole.System,
                             """
                              You are a sentiment classifier.

                             
                             """
                        ), 

                    new (ChatRole.User, 
                        $"""
                        Classify the sentiment of this product review and give explanation of why you classified it as such: "{review}"
                        """                       
                        )                      

                     ];

                    //var response2 = await chatClient.GetResponseAsync<Sentiment>($"What's the sentiment of this review? {review}");
                    var response2 = await chatClient.GetResponseAsync<Sentiment>(prompt);
                    Console.WriteLine($"Review: {review} | Sentiment: {response2.Result}");
                }
                catch (Exception e)
                {
                    Console.Write(e.Message);
                }
            }   

        }
    }
}
