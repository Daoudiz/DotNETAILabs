using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetIALabs.Configuration
{
    public sealed class Helpers
    {
        public static int GetActiveEmbeddingDimensions(AiOptions options)
        {
            string provider = options.Provider.Trim();

            if (provider.Equals(
                AiOptions.OllamaProvider,
                StringComparison.OrdinalIgnoreCase))
            {
                return options.Providers.Ollama.EmbeddingDimensions;
            }

            if (provider.Equals(
                AiOptions.OpenAiProvider,
                StringComparison.OrdinalIgnoreCase))
            {
                return options.Providers.OpenAI.EmbeddingDimensions;
            }

            if (provider.Equals(
                AiOptions.AzureOpenAiProvider,
                StringComparison.OrdinalIgnoreCase))
            {
                return options.Providers.AzureOpenAI.EmbeddingDimensions;
            }

            throw new InvalidOperationException(
                $"Fournisseur AI inconnu : '{options.Provider}'.");
        }
    }
}
