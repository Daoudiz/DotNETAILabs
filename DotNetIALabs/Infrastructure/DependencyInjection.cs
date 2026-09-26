using System;
using System.Collections.Generic;
using System.Text;
using DotNetIALabs.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OpenAI;
using Azure;
using Azure.AI.OpenAI;

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

        return services;
    }

    public static IChatClient CreateChatClient(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<IOptions<AiOptions>>().Value;

        switch (options.Provider?.Trim())
        {
            case AiOptions.OllamaProvider:
                return new OllamaApiClient(
                    options.Providers.Ollama.Endpoint,
                    options.Providers.Ollama.Model);

            case AiOptions.OpenAiProvider:
            {
                OpenAiOptions openAi = options.Providers.OpenAI;
                OpenAIClient openAiClient = new OpenAIClient(openAi.ApiKey);
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
}

