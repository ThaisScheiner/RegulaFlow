using Amazon.SQS;
using Notification.Worker;
using Serilog;
using Polly;
using Polly.Retry;
using Polly.CircuitBreaker;
using System.Net.Sockets;
using Amazon.Extensions.NETCore.Setup;
using AWS.Logger.Serilog;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        var configuration = hostContext.Configuration;

        // Configura e registra os servi�os da AWS 
        services.AddDefaultAWSOptions(configuration.GetAWSOptions());
        services.AddAWSService<IAmazonSQS>();

        // --- Configura��o do Polly ---

        // 1. Pol�tica de Retry
        var retryPolicy = Policy
            .Handle<SocketException>() // Captura falhas de rede
                                       // Captura a exce��o espec�fica da simula��o
            .Or<Exception>(ex => ex.Message.Contains("API de notifica��o falhou"))
            .WaitAndRetryAsync(
                retryCount: 3, // Tenta 3 vezes
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(attempt), // Espera 1s, 2s, 3s
                onRetry: (exception, timespan, attempt, context) =>
                {
                    // Loga a nova tentativa
                    Console.WriteLine($"[Polly-Notify-Retry] Tentativa {attempt} falhou: {exception.Message}. Tentando novamente em {timespan.TotalSeconds}s...");
                }
            );

        // 2. Pol�tica de Circuit Breaker
        var circuitBreakerPolicy = Policy
            .Handle<Exception>() // Captura qualquer exce��o que passou pelo Retry
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 5, // Quebra ap�s 5 falhas seguidas
                durationOfBreak: TimeSpan.FromMinutes(1), // Mant�m o circuito aberto por 1 minuto
                onBreak: (exception, duration) =>
                {
                    // Loga quando o circuito abre
                    Console.WriteLine($"[Polly-Notify-CB] Circuito aberto por {duration.TotalSeconds}s devido a: {exception.Message}");
                },
                onReset: () =>
                {
                    // Loga quando o circuito fecha
                    Console.WriteLine("[Polly-Notify-CB] Circuito fechado. Tentativas permitidas.");
                }
            );

        // 3. Combina as pol�ticas (Retry primeiro, depois Circuit Breaker)
        IAsyncPolicy combinedPolicy = Policy.WrapAsync(circuitBreakerPolicy, retryPolicy);

        // 4. Registra a pol�tica combinada no DI 
        services.AddSingleton(combinedPolicy);

        // --- Fim da Configura��o do Polly ---

        // Registra o Worker 
        services.AddHostedService<Worker>();
    })
    .UseSerilog((context, services, configuration) => configuration // Configura o Serilog
        .ReadFrom.Configuration(context.Configuration) // [carrega a config do AWS do appsettings
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console() // Manter o log de console

        // --- Observabilidade ---
        // Este m�todo l� a configura��o da se��o "Serilog" no appsettings.json
        .WriteTo.AWSSeriLog(context.Configuration)
    // --- FIM ---
    )
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