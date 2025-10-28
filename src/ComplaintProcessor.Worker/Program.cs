using Amazon.SQS;
using Amazon.SimpleNotificationService;
using ComplaintProcessor.Worker;
using ComplaintProcessor.Worker.Data;
using Microsoft.EntityFrameworkCore;
using Serilog; 
using Polly;
using Polly.Retry;
using Amazon.Runtime;
using System.Net.Sockets;
using MySqlConnector;
using ComplaintProcessor.Worker.Policies;
using Amazon.Extensions.NETCore.Setup;
using Serilog.Sinks.AmazonCloudWatch;
using AWS.Logger.Serilog;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        var configuration = hostContext.Configuration;

        // 1. Configura o DbContext 
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<AppDbContext>(options =>
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

        // 2. Configura os serviços da AWS 
        services.AddDefaultAWSOptions(configuration.GetAWSOptions());
        services.AddAWSService<IAmazonSQS>();
        services.AddAWSService<IAmazonSimpleNotificationService>();

        // --- Configuração do Polly  ---

        // 3.1. Política do Banco de Dados
        AsyncRetryPolicy dbPolicy = Policy
            .Handle<SocketException>()
            .Or<MySqlException>(ex => ex.IsTransient)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // 2s, 4s, 8s
                onRetry: (exception, timespan, attempt, context) =>
                {
                    Console.WriteLine($"[Polly-DB] Tentativa {attempt} falhou: {exception.Message}. Tentando novamente em {timespan.TotalSeconds}s...");
                }
            );

        // 3.2. Política do SNS
        AsyncRetryPolicy snsPolicy = Policy
            .Handle<AmazonServiceException>()
            .Or<SocketException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(attempt), // 1s, 2s, 3s
                onRetry: (exception, timespan, attempt, context) =>
                {
                    Console.WriteLine($"[Polly-SNS] Tentativa {attempt} falhou: {exception.Message}. Tentando novamente em {timespan.TotalSeconds}s...");
                }
            );

        // 3.3. Registrar políticas 
        services.AddSingleton(new DbResiliencePolicy(dbPolicy));
        services.AddSingleton(new SnsResiliencePolicy(snsPolicy));

        // --- Fim da Configuração do Polly ---

        // 4. Registra nosso Worker 
        services.AddHostedService<Worker>();
    })
    .UseSerilog((context, services, configuration) => configuration // Configura o Serilog
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console() // Manter o log de console

        // --- [Observabilidade: CloudWatch] INÍCIO ---
        .WriteTo.AmazonCloudWatch(
            // Nome do grupo de logs separado para este Worker
            logGroup: "/aws/dotnet/RegulaFlow/ComplaintProcessor.Worker",
            logStreamPrefix: "processor-worker-instance",

            // Puxa as opções da AWS (região, credenciais) da configuração
            awsOptions: context.Configuration.GetAWSOptions()
        )
    // --- [Observabilidade: CloudWatch] FIM ---
    )
    .Build();

try
{
    Log.Information("Iniciando o Worker de Processamento de Reclamações");
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