using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetIALabs.Configuration
{
    public sealed class AiOptionsValidator : IValidateOptions<AiOptions>
    {
        public ValidateOptionsResult Validate(
         string? name,
         AiOptions options)
        {
            List<string> errors = [];
            string provider = options.Provider?.Trim() ?? string.Empty;

            if (provider.Equals(
                    AiOptions.OllamaProvider,
                    StringComparison.OrdinalIgnoreCase))
            {
                ValidateOllama(options.Providers.Ollama, errors);
            }
            else if (provider.Equals(
                         AiOptions.OpenAiProvider,
                         StringComparison.OrdinalIgnoreCase))
            {
                ValidateOpenAi(options.Providers.OpenAI, errors);
            }
            else if (provider.Equals(
                         AiOptions.AzureOpenAiProvider,
                         StringComparison.OrdinalIgnoreCase))
            {
                ValidateAzureOpenAi(
                    options.Providers.AzureOpenAI,
                    errors);
            }
            else
            {
                errors.Add(
                    $"AI:Provider contient '{options.Provider}'. " +
                    "Valeurs acceptées : Ollama, OpenAI, AzureOpenAI.");
            }

            ValidateGeneration(options.Generation, errors);

            return errors.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(errors);
        }

        private static void ValidateOllama(
            OllamaOptions options,
            ICollection<string> errors)
        {
            Require(options.Model, "AI:Providers:Ollama:Model", errors);
            Require(options.EmbeddingModel,"AI:Providers:Ollama:EmbeddingModel",errors);
            RequirePositive(options.EmbeddingDimensions,
                            "AI:Providers:Ollama:EmbeddingDimensions",
                            errors);
            RequireHttpEndpoint(
                options.Endpoint,
                "AI:Providers:Ollama:Endpoint",
                errors);
        }

        private static void ValidateOpenAi(
            OpenAiOptions options,
            ICollection<string> errors)
        {
            Require(options.Model, "AI:Providers:OpenAI:Model", errors);
            Require(
            options.EmbeddingModel,
            "AI:Providers:OpenAI:EmbeddingModel",
            errors);
            RequirePositive(
            options.EmbeddingDimensions,
            "AI:Providers:OpenAI:EmbeddingDimensions",
            errors);
            Require(
                options.ApiKey,
                "AI:Providers:OpenAI:ApiKey",
                errors,
                "Configurez-la avec User Secrets ou l'environnement.");
        }

        private static void ValidateAzureOpenAi(
            AzureOpenAiOptions options,
            ICollection<string> errors)
        {
            Require(
                options.Deployment,
                "AI:Providers:AzureOpenAI:Deployment",
                errors);
            Require(
                    options.EmbeddingDeployment,
                    "AI:Providers:AzureOpenAI:EmbeddingDeployment",
                    errors);
            RequirePositive(
                options.EmbeddingDimensions,
                "AI:Providers:AzureOpenAI:EmbeddingDimensions",
                errors);
            RequireHttpEndpoint(
                options.Endpoint,
                "AI:Providers:AzureOpenAI:Endpoint",
                errors);
            Require(
                options.ApiKey,
                "AI:Providers:AzureOpenAI:ApiKey",
                errors,
                "Configurez-la avec User Secrets ou l'environnement.");
        }

        private static void ValidateGeneration(
            GenerationOptions options,
            ICollection<string> errors)
        {
            if (options.Temperature is < 0 or > 2)
            {
                errors.Add(
                    "AI:Generation:Temperature doit être comprise entre 0 et 2.");
            }

            if (options.MaxOutputTokens is <= 0)
            {
                errors.Add(
                    "AI:Generation:MaxOutputTokens doit être supérieur à zéro.");
            }
        }

        private static void Require(
            string? value,
            string path,
            ICollection<string> errors,
            string? guidance = null)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string error = $"{path} est obligatoire.";
            errors.Add(string.IsNullOrWhiteSpace(guidance)
                ? error
                : $"{error} {guidance}");
        }

        private static void RequirePositive( int value,
                                             string path,
                                             ICollection<string> errors)
        {
            if (value <= 0)
            {
                errors.Add($"{path} doit être supérieur à zéro.");
            }
        }

        private static void RequireHttpEndpoint(
            string? value,
            string path,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{path} est obligatoire.");
                return;
            }

            bool isValid = Uri.TryCreate(
                value,
                UriKind.Absolute,
                out Uri? endpoint)
                && (endpoint.Scheme == Uri.UriSchemeHttp
                    || endpoint.Scheme == Uri.UriSchemeHttps);

            if (!isValid)
            {
                errors.Add(
                    $"{path} doit être une URI absolue HTTP ou HTTPS.");
            }
        }
    }
}
