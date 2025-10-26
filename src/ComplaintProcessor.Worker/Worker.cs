using Amazon.SQS;
using Amazon.SQS.Model;
using ComplaintProcessor.Worker.Data;
using System.Text.Json;

namespace ComplaintProcessor.Worker;

// DTO para deserializar a mensagem da fila. Deve ser idêntico ao Request da API.
public record ComplaintRequest(string CustomerName, string CustomerEmail, string ComplaintType, string Description);

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IAmazonSQS _sqsClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _queueUrl;

    public Worker(ILogger<Worker> logger, IAmazonSQS sqsClient, IConfiguration configuration, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _sqsClient = sqsClient;
        _scopeFactory = scopeFactory;
        _queueUrl = configuration["Aws:SqsQueueUrl"] ?? throw new ArgumentNullException("Aws:SqsQueueUrl cannot be null");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker iniciado em: {time}", DateTimeOffset.Now);

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Verificando a fila SQS por novas mensagens...");

            var receiveRequest = new ReceiveMessageRequest
            {
                QueueUrl = _queueUrl,
                MaxNumberOfMessages = 5, // Pega até 5 mensagens por vez
                WaitTimeSeconds = 20     // Long polling: espera até 20s se a fila estiver vazia
            };

            var response = await _sqsClient.ReceiveMessageAsync(receiveRequest, stoppingToken);

            if (response.Messages.Any())
            {
                _logger.LogInformation("{count} mensagens recebidas.", response.Messages.Count);

                foreach (var message in response.Messages)
                {
                    try
                    {
                        var complaintRequest = JsonSerializer.Deserialize<ComplaintRequest>(message.Body);
                        if (complaintRequest is null)
                        {
                            _logger.LogWarning("Não foi possível deserializar a mensagem. Body: {body}", message.Body);
                            continue; // Pula para a próxima mensagem
                        }

                        _logger.LogInformation("Processando reclamação de {email}", complaintRequest.CustomerEmail);

                        // Mapeia o DTO para a Entidade do banco
                        var complaintEntity = new Complaint
                        {
                            Id = Guid.NewGuid(),
                            CustomerName = complaintRequest.CustomerName,
                            CustomerEmail = complaintRequest.CustomerEmail,
                            ComplaintType = complaintRequest.ComplaintType,
                            Description = complaintRequest.Description,
                            ReceivedAt = DateTime.UtcNow,
                            Status = ComplaintStatus.Received
                        };

                        // Salva no banco de dados
                        using (var scope = _scopeFactory.CreateScope())
                        {
                            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                            dbContext.Complaints.Add(complaintEntity);
                            await dbContext.SaveChangesAsync(stoppingToken);
                        }
                        _logger.LogInformation("Reclamação {id} salva no banco de dados.", complaintEntity.Id);

                        // MUITO IMPORTANTE: Apaga a mensagem da fila para não ser processada novamente
                        await _sqsClient.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
                        _logger.LogInformation("Mensagem {receipt} apagada da fila.", message.ReceiptHandle);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Erro ao processar a mensagem {messageId}", message.MessageId);
                        // Em um cenário real, teria uma lógica para enviar para uma Dead-Letter Queue (DLQ)
                    }
                }
            }
            else
            {
                _logger.LogInformation("Nenhuma mensagem na fila.");
            }
        }
    }
}