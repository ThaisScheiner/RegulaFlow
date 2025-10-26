using Amazon.Extensions.NETCore.Setup; // Adicionar este using
using Amazon.SQS;
using ComplaintIngestion.API.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configura��o do Serilog (continua igual)
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.Debug()
    .CreateBootstrapLogger();

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Adicionar servi�os ao container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// --- CORRE��O APLICADA AQUI ---
// 1. L� a se��o "Aws" do appsettings.json e configura as op��es padr�o da AWS
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());

// 2. Registra o cliente SQS. Agora ele usar� automaticamente a regi�o configurada acima.
builder.Services.AddAWSService<IAmazonSQS>();

// Registrar nosso servi�o SQS (continua igual)
builder.Services.AddScoped<SqsPublisherService>();

var app = builder.Build();

// Pipeline de requisi��es HTTP (continua igual)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
// app.UseHttpsRedirection(); // Comentado temporariamente para facilitar testes locais em HTTP
app.UseAuthorization();
app.MapControllers();

// Bloco try-finally (continua igual)
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