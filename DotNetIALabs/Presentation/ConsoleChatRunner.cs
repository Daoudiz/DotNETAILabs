using DotNetIALabs.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.AI;
using DotNetIALabs.Application;

namespace DotNetIALabs.Presentation
{
    public sealed class ConsoleChatRunner(IChatClient chatClient, IOptions<AiOptions> options, IQSLAbsIA qsLabsIA)
    {
        private readonly ChatOptions _chatOptions = new ChatOptions
        {
            MaxOutputTokens = options.Value.Generation.MaxOutputTokens,
            Temperature = options.Value.Generation.Temperature,
        };

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            Console.WriteLine("Assistant IA prêt.");
            Console.WriteLine("Saisissez 'exit' ou 'quit' pour terminer.");

            while (!cancellationToken.IsCancellationRequested)
            {


                Console.WriteLine("Veuillez choisir le lab : ");

                Console.WriteLine("******************************************************************");
                Console.WriteLine("** 1.Se connecter à un modèle d'IA et le solliciter          *****");
                Console.WriteLine("** 2.Générer une application de conversation                 *****");
                Console.WriteLine("** 3.Demander une sortie structurée                          *****");
                Console.WriteLine("** 4.Créer une application de recherche vectorielle IA .NET  *****");
                Console.WriteLine("** 5.Exécuter une fonction .NET locale                       *****");
                Console.WriteLine("** 6.Créer un assistant IA minimal                           *****");
                Console.WriteLine("** 7.Commencez à utiliser les modèles d'application IA       *****");
                Console.WriteLine("******************************************************************");

                var choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        await qsLabsIA.Lab1SimpleIACall(chatClient);
                        break;
                    case "2":
                        await qsLabsIA.Lab2SimpleIAChat(
                            chatClient,
                            cancellationToken);
                        break;
                    case "3":
                        await qsLabsIA.Lab3StructedOutput(chatClient);
                        break;
                }


            }

        }
    }
}
