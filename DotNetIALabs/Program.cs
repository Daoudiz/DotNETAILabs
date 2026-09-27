using DotNetIALabs.Infrastructure;
using DotNetIALabs.Presentation;
using DotNetIALabs.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(
    new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplicationInfrastructure(builder.Configuration);
builder.Services.AddSingleton<ConsoleChatRunner>();

using IHost host = builder.Build();

try
{
    await host.StartAsync();

    ConsoleChatRunner runner = host.Services
        .GetRequiredService<ConsoleChatRunner>();

    IHostApplicationLifetime lifetime = host.Services
        .GetRequiredService<IHostApplicationLifetime>();

    await runner.RunAsync(lifetime.ApplicationStopping);
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
    // Arrêt normal, par exemple après Ctrl+C.
}
finally
{
    await host.StopAsync();
}

