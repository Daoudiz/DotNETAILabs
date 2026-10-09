using DotNetIALabs.Configuration;
using DotNetIALabs.Presentation.Labs;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic.FileIO;

namespace DotNetIALabs.Presentation
{
    public sealed class ConsoleChatRunner(
        IOptions<AiOptions> aiOptions,
        ChatLabs chatLabs,
        VectorSearchLab vectorSearchLab,
        FunctionsCallLabs functionCallLab)
    {      

        public async Task RunAsync(CancellationToken cancellationToken )
        {
            string aiModel = string.Empty;
            string embeddingModel = string.Empty;

            switch(aiOptions.Value.Provider)
            {
                case ("Ollama"):
                    aiModel = aiOptions.Value.Providers.Ollama.Model;
                    embeddingModel = aiOptions.Value.Providers.Ollama.EmbeddingModel;
                    break;
                case ("OpenAI"):
                    aiModel = aiOptions.Value.Providers.OpenAI.Model;
                    embeddingModel = aiOptions.Value.Providers.OpenAI.EmbeddingModel;
                        break;
                case ("AzureOpenAI"):
                    aiModel = aiOptions.Value.Providers.AzureOpenAI.Deployment;
                    embeddingModel = aiOptions.Value.Providers.AzureOpenAI.EmbeddingDeployment;
                    break;
            }           
            


            Console.WriteLine($"Assistant IA {aiModel}  prêt. avec le modèl d'embedding {embeddingModel} ");                              

            while (!cancellationToken.IsCancellationRequested)
            {
                WriteMenu();

                string? choice = await Console.In.ReadLineAsync(cancellationToken);

                if (choice is null)
                {
                    return;
                }

                choice = choice.Trim();


                if (choice.Equals("exit", StringComparison.OrdinalIgnoreCase)
                || choice.Equals("quit", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Fin de l'application.");
                    return;
                }

                try
                {
                    await RunSelectedLabAsync(choice, cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine(
                        $"Le lab n'a pas pu être exécuté : {exception.Message}");
                    Console.Error.WriteLine();
                }
            }

        }

        private async Task RunSelectedLabAsync(
        string choice,
        CancellationToken cancellationToken)
        {
            switch (choice)
            {
                case "1":
                    await chatLabs.RunSimpleCallAsync(cancellationToken);
                    break;
                case "2":
                    await chatLabs.RunConversationAsync(cancellationToken);
                    break;
                case "3":
                    await chatLabs.RunStructuredOutputAsync(cancellationToken);
                    break;
                case "4":
                    await vectorSearchLab.RunSearchAzureServicesAsync(cancellationToken);
                    break;
                case "5":
                    await vectorSearchLab.RunSearchEquipmentRulesAsync(cancellationToken);
                    break;
                case "6":
                    await functionCallLab.RunSimpleFunctionCallAsync(cancellationToken);
                    break;
                case "7":
                    await functionCallLab.RunMultipleFunctionCallAsync(cancellationToken);
                    break;
                case "8":
                    await functionCallLab.RunSimpleDirectFunctionCall();
                    break;
                case "9":
                    await functionCallLab.RunFunctionInvokingChatClient(cancellationToken);
                    break;
                default:
                    Console.WriteLine(
                        $"Choix inconnu : '{choice}'. Sélectionnez 1 à 6.");
                    break;
            }
        }

        private static void WriteMenu()
        {
            Console.WriteLine();
            Console.WriteLine("Veuillez choisir le lab :");
            Console.WriteLine("***************************** Simple chat *********************************");
            Console.WriteLine("1. Se connecter à un modèle d'IA et le solliciter");
            Console.WriteLine("2. Générer une application de conversation");
            Console.WriteLine("3. Demander une sortie structurée");

            Console.WriteLine("************************ Recherche vectorielle******************************");
            Console.WriteLine("4. Recherche vectorielle services Azure");
            Console.WriteLine("5. Recherche vectorielle règles équipements");

            Console.WriteLine("************************ Appel des fonctions ********************************");
            Console.WriteLine("6. Appeler une fonction .NET à l'aide d'un modèle");
            Console.WriteLine("7. Appeler une fonction .NET à l'aide d'un modèle avec multi-fonctions");
            Console.WriteLine("8. Appeler manuellement une fonction créer avec AIFunctionFactory");
            Console.WriteLine("9. Appeler une fonction créer avec AIFunctionFactory via Assitant AI");

            Console.WriteLine("exit ou quit. Quitter");
            Console.Write("Choix : ");
        }
    }
}
