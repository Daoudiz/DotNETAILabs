using DotNetIALabs.Evaluation;
using DotNetIALabs.Infrastructure;
using DotNetIALabs.Presentation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

const string evaluationArgument = "--evaluate-equipment-rules";

bool runEquipmentRulesEvaluation = args.Contains(
    evaluationArgument,
    StringComparer.OrdinalIgnoreCase);

string[] hostArgs = [.. args
    .Where(argument => !argument.Equals(
        evaluationArgument,
        StringComparison.OrdinalIgnoreCase))];


HostApplicationBuilder builder = Host.CreateApplicationBuilder(
    new HostApplicationBuilderSettings
    {
        Args = hostArgs,
        ContentRootPath = AppContext.BaseDirectory
    });

builder.Services.AddInfrastructure(builder.Configuration);

using IHost host = builder.Build();

try
{
    await host.StartAsync();

    if (runEquipmentRulesEvaluation)
    {
        EquipmentRulesEvaluationRunner evaluationRunner = host.Services
            .GetRequiredService<EquipmentRulesEvaluationRunner>();

        EquipmentRulesEvaluationReport report =
            await evaluationRunner.RunAsync(
                host.Services
                    .GetRequiredService<IHostApplicationLifetime>()
                    .ApplicationStopping);

        Environment.ExitCode = report.Passed ? 0 : 1;
    }
    else
    {
        ConsoleChatRunner runner = host.Services
            .GetRequiredService<ConsoleChatRunner>();

        IHostApplicationLifetime lifetime = host.Services
            .GetRequiredService<IHostApplicationLifetime>();

        await runner.RunAsync(lifetime.ApplicationStopping);
    }
}
catch (OptionsValidationException exception)
{
    Console.Error.WriteLine("La configuration IA est invalide :");

    foreach (string failure in exception.Failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    Environment.ExitCode = 1;
}
catch (OperationCanceledException)
{
    // Arrêt normal demandé par l'hôte, par exemple avec Ctrl+C.
}
finally
{
    await host.StopAsync();
}
