using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using ComplaintProcessor.Worker.Data;
using ComplaintProcessor.Worker.Events;
using ComplaintProcessor.Worker.Policies;
using System.Text.Json;
using SqsMessageAttributeValue = Amazon.SQS.Model.MessageAttributeValue;
using SnsMessageAttributeValue = Amazon.SimpleNotificationService.Model.MessageAttributeValue;

namespace ComplaintProcessor.Worker;

public record ComplaintRequest(string CustomerName, string CustomerEmail, string ComplaintType, string Description);

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IAmazonSQS _sqsClient;
    private readonly IAmazonSimpleNotificationService _snsClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _sqsQueueUrl;
    private readonly string _snsTopicArn;
    private readonly DbResiliencePolicy _dbPolicy;
    private readonly SnsResiliencePolicy _snsPolicy;

    public Worker(
        ILogger<Worker> logger,
        IAmazonSQS sqsClient,
        IAmazonSimpleNotificationService snsClient,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        DbResiliencePolicy dbPolicy,
        SnsResiliencePolicy snsPolicy)
    {
        _logger = logger;
        _sqsClient = sqsClient;
        _snsClient = snsClient;
        _scopeFactory = scopeFactory;
        _dbPolicy = dbPolicy;
        _snsPolicy = snsPolicy;
        _sqsQueueUrl = configuration["Aws:SqsQueueUrl"] ?? throw new ArgumentNullException("Aws:SqsQueueUrl");
        _snsTopicArn = configuration["Aws:SnsTopicArn"] ?? throw new ArgumentNullException("Aws:SnsTopicArn");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker iniciado em: {time}", DateTimeOffset.Now);

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Verificando a fila SQS por novas mensagens...");

            var receiveRequest = new ReceiveMessageRequest
            {
                QueueUrl = _sqsQueueUrl,
                MaxNumberOfMessages = 5,
                WaitTimeSeconds = 20
            };

            var response = await _sqsClient.ReceiveMessageAsync(receiveRequest, stoppingToken);

            if (response?.Messages?.Any() != true)
            {
                _logger.LogInformation("Nenhuma mensagem na fila.");
                continue;
            }

            _logger.LogInformation("{count} mensagens recebidas.", response.Messages.Count);

            foreach (var message in response.Messages)
            {
                try
                {
                    var complaintRequest = JsonSerializer.Deserialize<ComplaintRequest>(message.Body);
                    if (complaintRequest is null)
                    {
                        _logger.LogWarning("Não foi possível deserializar a mensagem. Body: {body}", message.Body);
                        await _sqsClient.DeleteMessageAsync(_sqsQueueUrl, message.ReceiptHandle, stoppingToken);
                        continue;
                    }

                    _logger.LogInformation("Processando reclamação de {email}", complaintRequest.CustomerEmail);

                    var complaintEntity = new Complaint
                    {
                        Id = Guid.NewGuid(),
                        CustomerName = complaintRequest.CustomerName,
                        CustomerEmail = complaintRequest.CustomerEmail,
                        ComplaintType = complaintRequest.ComplaintType,
                        Description = complaintRequest.Description,
                        ReceivedAt = DateTime.UtcNow,
                        Status = ComplaintStatus.Processing
                        // [CORREÇÃO] Removido o "__" que estava aqui
                    };

                    using var scope = _scopeFactory.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    dbContext.Complaints.Add(complaintEntity);

                    await _dbPolicy.Policy.ExecuteAsync(async () => await dbContext.SaveChangesAsync(stoppingToken));

                    _logger.LogInformation("Reclamação {id} salva no banco de dados.", complaintEntity.Id);

                    await PublishProcessedEvent(complaintEntity, stoppingToken);

                    await _sqsClient.DeleteMessageAsync(_sqsQueueUrl, message.ReceiptHandle, stoppingToken);
                    _logger.LogInformation("Mensagem {receipt} apagada da fila.", message.ReceiptHandle);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao processar a mensagem {messageId}", message.MessageId);
                }
            }
            
        }
    }

    private async Task PublishProcessedEvent(Complaint complaint, CancellationToken stoppingToken)
    {
        var processedEvent = new ComplaintProcessedEvent(
            complaint.Id,
            complaint.CustomerEmail,
            complaint.ComplaintType,
            DateTime.UtcNow
        );

        var publishRequest = new PublishRequest
        {
            TopicArn = _snsTopicArn,
            Message = JsonSerializer.Serialize(processedEvent),
            
            MessageAttributes = new Dictionary<string, SnsMessageAttributeValue>
            {
                {
                    "EventType",
                    new SnsMessageAttributeValue
                    {
                        DataType = "String",
                        StringValue = nameof(ComplaintProcessedEvent)
                    }
                }
                
            }
        };

        await _snsPolicy.Policy.ExecuteAsync(async () => await _snsClient.PublishAsync(publishRequest, stoppingToken));

        _logger.LogInformation("Evento ComplaintProcessedEvent publicado para a reclamação {id}", complaint.Id);
    }
}