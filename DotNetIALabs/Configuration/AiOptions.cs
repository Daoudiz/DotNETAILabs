using OllamaSharp.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetIALabs.Configuration;

public sealed class AiOptions
{
    public const string SectionName = "AI";

    public const string OllamaProvider = "Ollama";
    public const string OpenAiProvider = "OpenAI";
    public const string AzureOpenAiProvider = "AzureOpenAI";

    public string Provider { get; init; } = string.Empty;
    public GenerationOptions Generation { get; init; } = new();
    public AiProvidersOptions Providers { get; init; } = new();
}

public sealed class GenerationOptions
{
    public float Temperature { get; init; } = 0.7f;
    public int MaxOutputTokens { get; init; } = 1024;
}

public sealed class AiProvidersOptions
{
    public OllamaOptions Ollama { get; init; } = new();
    public OpenAiOptions OpenAI { get; init; } = new();
    public AzureOpenAiOptions AzureOpenAI { get; init; } = new();
}

public sealed class OllamaOptions
{
    public string Model { get; init; } = string.Empty;
    
    // Modèle utilisé par IEmbeddingGenerator.
    public string EmbeddingModel { get; init; } = string.Empty;
    public int EmbeddingDimensions { get; init; }
    public string Endpoint { get; init; } = string.Empty;
}

public sealed class OpenAiOptions
{
    public string Model { get; init; } = string.Empty;
    public string EmbeddingModel { get; init; } = string.Empty;
    public int EmbeddingDimensions { get; init; }


    // Alimentée par User Secrets ou une variable d'environnement.
    public string ApiKey { get; init; } = string.Empty;
}

public sealed class AzureOpenAiOptions
{
    // Nom du déploiement Azure, pas nécessairement celui du modèle.
    public string Deployment { get; init; } = string.Empty;
    public string EmbeddingDeployment { get; init; } = string.Empty;
    public int EmbeddingDimensions { get; init; }
    public string Endpoint { get; init; } = string.Empty;

    // Alimentée par User Secrets ou une variable d'environnement.
    public string ApiKey { get; init; } = string.Empty;
}

