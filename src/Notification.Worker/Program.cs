using Amazon.SQS;
using Notification.Worker;
using Serilog;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        var configuration = hostContext.Configuration;

        // Configura e registra os servi�os da AWS
        services.AddDefaultAWSOptions(configuration.GetAWSOptions());
        services.AddAWSService<IAmazonSQS>();

        // Registra o Worker
        services.AddHostedService<Worker>();
    })
    .UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console())
    .Build();

try
{
    Log.Information("Iniciando o Worker de Notifica��o");
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "O Worker de Notifica��o falhou ao iniciar");
}
finally
{
    await Log.CloseAndFlushAsync();
}