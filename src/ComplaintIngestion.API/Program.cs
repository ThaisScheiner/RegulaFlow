using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using Amazon.SQS;
using ComplaintIngestion.API.Services;
using Polly;
using Polly.Retry;
using Serilog;
using AWS.Logger.Serilog; // <-- Esta linha depende do 'dotnet add package AWS.Logger.Serilog'

var builder = WebApplication.CreateBuilder(args);

// Configura��o do Serilog (Bootstrap)
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.Debug()
    .CreateBootstrapLogger();

// Configura��o do Serilog para a aplica��o
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console() // Manter o log de console

    // Este m�todo vem do pacote 'AWS.Logger.Serilog'
    .WriteTo.AWSSeriLog(context.Configuration)
);

// Adicionar servi�os ao container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// --- Configura��o dos Servi�os AWS (sem altera��es) ---
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonSQS>();
builder.Services.AddScoped<SqsPublisherService>();

// --- Configura��o do Polly (sem altera��es) ---
AsyncRetryPolicy sqsPolicy = Policy
    .Handle<AmazonServiceException>()
    .Or<System.Net.Sockets.SocketException>()
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: (attempt) => TimeSpan.FromSeconds(attempt),
        onRetry: (exception, timespan, attempt, context) =>
        {
            Console.WriteLine($"[Polly-API-SQS] Tentativa {attempt} falhou: {exception.Message}. Tentando novamente em {timespan.TotalSeconds}s...");
        }
    );
builder.Services.AddSingleton(sqsPolicy);
// --- Fim da Configura��o do Polly ---

var app = builder.Build();

// Pipeline de requisi��es HTTP (sem altera��es)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
// app.UseHttpsRedirection(); 
app.UseAuthorization();
app.MapControllers();

// Bloco try-finally (sem altera��es)
try
{
    Log.Information("Iniciando a API de Ingest�o de Reclama��es");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "A aplica��o falhou ao iniciar");
}
finally
{
    await Log.CloseAndFlushAsync();
}