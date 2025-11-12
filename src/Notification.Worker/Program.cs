using Amazon.SQS;
using Notification.Worker;
using Serilog;
using Polly; 
using Polly.Retry; 
using Amazon.Runtime; 
using System.Net.Sockets; 

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        var configuration = hostContext.Configuration;

        // 1. Configura os serviços da AWS
        services.AddDefaultAWSOptions(configuration.GetAWSOptions());
        services.AddAWSService<IAmazonSQS>();

        // 2. Configuração do Polly para SQS
        AsyncRetryPolicy sqsPolicy = Policy
            .Handle<AmazonServiceException>() // Erros da AWS
            .Or<SocketException>() // Erros de rede
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(attempt), // 1s, 2s, 3s
                onRetry: (exception, timespan, attempt, context) =>
                {
                    // Usar ILogger é o ideal, mas Console funciona para depuração
                    Console.WriteLine($"[Polly-SQS-Notification] Tentativa {attempt} falhou: {exception.Message}. Tentando novamente em {timespan.TotalSeconds}s...");
                }
            );

        // 3. Registrar a política
        services.AddSingleton<IAsyncPolicy>(sqsPolicy);
        
        // 4. Registra o Worker (agora ele pode ser construído)
        services.AddHostedService<Worker>();
    })
    .UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
    // .WriteTo.CloudWatch() 
    )
    .Build();

try
{
    Log.Information("Iniciando o Worker de Notificação");
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "O Worker de Notificação falhou ao iniciar");
}
finally
{
    await Log.CloseAndFlushAsync();
}