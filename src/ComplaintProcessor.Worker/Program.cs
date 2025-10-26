using Amazon.SQS;
using Amazon.SimpleNotificationService; 
using ComplaintProcessor.Worker;
using ComplaintProcessor.Worker.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        var configuration = hostContext.Configuration;

        // 1. Configura o DbContext para usar MySQL, lendo a Connection String do appsettings.json
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<AppDbContext>(options =>
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

        // 2. Configura os servi�os da AWS, lendo a regi�o do appsettings.json
        services.AddDefaultAWSOptions(configuration.GetAWSOptions());
        services.AddAWSService<IAmazonSQS>(); // Registra o cliente para SQS (receber mensagens)
        services.AddAWSService<IAmazonSimpleNotificationService>(); // Registra o cliente para SNS (enviar eventos)

        // 3. Registra nosso Worker como o servi�o principal a ser executado em background
        services.AddHostedService<Worker>();
    })
    .UseSerilog((context, services, configuration) => configuration // Configura o Serilog para logging
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console())
    .Build();

// garantir para que os logs sejam salvos em caso de erro na inicializa��o
try
{
    Log.Information("Iniciando o Worker de Processamento de Reclama��es");
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "O Worker de Processamento falhou ao iniciar");
}
finally
{
    await Log.CloseAndFlushAsync();
}