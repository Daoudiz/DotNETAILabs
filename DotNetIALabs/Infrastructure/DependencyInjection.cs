using Azure;
using Azure.AI.OpenAI;
using DotNetIALabs.Configuration;
using DotNetIALabs.Evaluation;
using DotNetIALabs.Presentation;
using DotNetIALabs.Presentation.Labs;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OpenAI;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetIALabs.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AiOptions>()
            .Bind(configuration.GetSection(AiOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<AiOptions>, AiOptionsValidator>();

        services.AddSingleton<IChatClient>(CreateChatClient);

        //Create OpenAIClient for specifics OpenAI labs
        services.AddSingleton<OpenAIClient>(CreateOpenAIClient);

        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            CreateEmbeddingGenerator);

        services.AddSingleton<ChatLabs>();
        services.AddSingleton<VectorSearchLab>();
        services.AddSingleton<ConsoleChatRunner>();
        services.AddSingleton<EquipmentRulesEvaluationRunner>();

        return services;
    }

    public static IChatClient CreateChatClient(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<IOptions<AiOptions>>().Value;

        switch (options.Provider?.Trim())
        {
            case AiOptions.OllamaProvider:
                IChatClient client =  new OllamaApiClient(
                    options.Providers.Ollama.Endpoint,
                    options.Providers.Ollama.Model);

                return client.AsBuilder()
                            .UseFunctionInvocation()
                            .Build();

            case AiOptions.OpenAiProvider:
            {
                OpenAiOptions openAi = options.Providers.OpenAI;
                OpenAIClient openAiClient = new (openAi.ApiKey);
                    return openAiClient
                   .GetChatClient(openAi.Model)
                   .AsIChatClient();

                }

            case AiOptions.AzureOpenAiProvider:
                AzureOpenAiOptions azure = options.Providers.AzureOpenAI;

                AzureOpenAIClient azureClient = new(
                    new Uri(azure.Endpoint),
                    new AzureKeyCredential(azure.ApiKey));

                return azureClient
                    .GetChatClient(azure.Deployment)
                    .AsIChatClient();

            default:
                throw new InvalidOperationException(
                    $"Fournisseur AI inconnu : '{options.Provider}'.");
        }
    }

    private static OpenAIClient CreateOpenAIClient(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<IOptions<AiOptions>>().Value;

        OpenAiOptions openAi = options.Providers.OpenAI;
        OpenAIClient openAiClient = new(openAi.ApiKey);

        return openAiClient;
    }

    private static AzureOpenAIClient CreateAzureOpenAIClient(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<IOptions<AiOptions>>().Value;

        AzureOpenAiOptions azure = options.Providers.AzureOpenAI;

        AzureOpenAIClient azureClient = new(
            new Uri(azure.Endpoint),
            new AzureKeyCredential(azure.ApiKey));

        return azureClient;

    }

    private static IEmbeddingGenerator<string, Embedding<float>>
        CreateEmbeddingGenerator(IServiceProvider services)
    {
        AiOptions options = services
            .GetRequiredService<IOptions<AiOptions>>()
            .Value;

        if (IsProvider(options.Provider, AiOptions.OllamaProvider))
        {
            OllamaOptions ollama = options.Providers.Ollama;
            return new OllamaApiClient(
                new Uri(ollama.Endpoint),
                ollama.EmbeddingModel);
        }

        if (IsProvider(options.Provider, AiOptions.OpenAiProvider))
        {
            OpenAiOptions openAi = options.Providers.OpenAI;
            OpenAIClient client = new(openAi.ApiKey);

            return client
                .GetEmbeddingClient(openAi.EmbeddingModel)
                .AsIEmbeddingGenerator(openAi.EmbeddingDimensions);
        }

        if (IsProvider(options.Provider, AiOptions.AzureOpenAiProvider))
        {
            AzureOpenAiOptions azure = options.Providers.AzureOpenAI;
            AzureOpenAIClient client = new(
                new Uri(azure.Endpoint),
                new AzureKeyCredential(azure.ApiKey));

            return client
                .GetEmbeddingClient(azure.EmbeddingDeployment)
                .AsIEmbeddingGenerator(azure.EmbeddingDimensions);
        }

        throw new InvalidOperationException(
            $"Fournisseur AI inconnu : '{options.Provider}'.");
    }

    private static bool IsProvider(string configured, string expected) =>
       configured.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase);
}

