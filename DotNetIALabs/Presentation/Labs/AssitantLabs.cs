using Azure.AI.OpenAI;
using DotNetIALabs.Configuration;
using DotNetIALabs.Data;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Assistants;
using OpenAI.Files;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetIALabs.Presentation.Labs
{
    public sealed class AssitantLabs (
        OpenAIClient openAIClient,
        AzureOpenAIClient azureOpenAIClient,
        IOptions<AiOptions> aiOptions)
    {
        private readonly GenerationOptions _generation = aiOptions.Value.Generation;

        public async Task RunSimpleOpenAIAssistant(CancellationToken cancellationToken)
        {
            #pragma warning disable OPENAI001
            AssistantClient assistantClient = openAIClient.GetAssistantClient();
            OpenAIFileClient fileClient = openAIClient.GetOpenAIFileClient();

            OpenAIFile salesFile = await SalesHistory.UploadAsync(
                fileClient,
                cancellationToken);

            Console.WriteLine(
                $"Fichier de ventes chargé dans OpenAI : {salesFile.Filename} " +
                $"({salesFile.Id}).");
        }
    }
}
