using Amazon.Extensions.NETCore.Setup; // Adicionar este using
using Amazon.SQS;
using ComplaintIngestion.API.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configuração do Serilog (continua igual)
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

// Adicionar serviços ao container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// --- CORREÇÃO APLICADA AQUI ---
// 1. Lê a seção "Aws" do appsettings.json e configura as opções padrão da AWS
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());

// 2. Registra o cliente SQS. Agora ele usará automaticamente a região configurada acima.
builder.Services.AddAWSService<IAmazonSQS>();

// Registrar nosso serviço SQS (continua igual)
builder.Services.AddScoped<SqsPublisherService>();

var app = builder.Build();

// Pipeline de requisições HTTP (continua igual)
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
    Log.Information("Iniciando a API de Ingestão de Reclamações");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "A aplicação falhou ao iniciar");
}
finally
{
    await Log.CloseAndFlushAsync();
}