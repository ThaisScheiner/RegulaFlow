using Amazon.SQS;
using ComplaintProcessor.Worker;
using ComplaintProcessor.Worker.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        var configuration = hostContext.Configuration;

        // Configura o DbContext para usar MySQL
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<AppDbContext>(options =>
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

        // Configura e registra os serviços da AWS
        services.AddDefaultAWSOptions(configuration.GetAWSOptions());
        services.AddAWSService<IAmazonSQS>();

        // Registra o Worker como um serviço hospedado
        services.AddHostedService<Worker>();
    })
    .UseSerilog((context, services, configuration) => configuration // Configura o Serilog
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console())
    .Build();

host.Run();