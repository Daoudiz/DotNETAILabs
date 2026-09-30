using DotNetIALabs.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.AI;
using DotNetIALabs.Presentation.Labs;

namespace DotNetIALabs.Presentation
{
    public sealed class ConsoleChatRunner(
        ChatLabs chatLabs,
        VectorSearchLab vectorSearchLab)
    {      

        public async Task RunAsync(CancellationToken cancellationToken )
        {
            Console.WriteLine("Assistant IA prêt.");                  

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
                    await chatLabs.RunSimpleFunctionCallAsync(cancellationToken);
                    break;
                case "7":
                    await chatLabs.RunMultipleFunctionCallAsync(cancellationToken);
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
            Console.WriteLine("1. Se connecter à un modèle d'IA et le solliciter");
            Console.WriteLine("2. Générer une application de conversation");
            Console.WriteLine("3. Demander une sortie structurée");
            Console.WriteLine("4. Recherche vectorielle services Azure");
            Console.WriteLine("5. Recherche vectorielle règles équipements");
            Console.WriteLine("6. Appeler une fonction .NET à l'aide d'un modèle");
            Console.WriteLine("7. Appeler une fonction .NET à l'aide d'un modèle avec multi-fonctions");
            Console.WriteLine("exit ou quit. Quitter");
            Console.Write("Choix : ");
        }
    }
}
