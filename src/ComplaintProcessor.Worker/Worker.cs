using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using ComplaintProcessor.Worker.Data;
using ComplaintProcessor.Worker.Events;
using ComplaintProcessor.Worker.Policies; 
using System.Text.Json;

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

    // [Polly] Campos para armazenar nossas políticas injetadas
    private readonly DbResiliencePolicy _dbPolicy;
    private readonly SnsResiliencePolicy _snsPolicy;

    public Worker(
        ILogger<Worker> logger,
        IAmazonSQS sqsClient,
        IAmazonSimpleNotificationService snsClient,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        DbResiliencePolicy dbPolicy, // [Polly] Injetando a política de DB
        SnsResiliencePolicy snsPolicy) // [Polly] Injetando a política de SNS
    {
        _logger = logger;
        _sqsClient = sqsClient;
        _snsClient = snsClient;
        _scopeFactory = scopeFactory;
        _dbPolicy = dbPolicy; // [Polly] Armazenando a política de DB
        _snsPolicy = snsPolicy; // [Polly] Armazenando a política de SNS
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
                        await _sqsClient.DeleteMessageAsync(_sqsQueueUrl, message.ReceiptHandle, stoppingToken); // Delete bad message
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
                    };

                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        dbContext.Complaints.Add(complaintEntity);

                        // [Polly] Executamos o SaveChangesAsync dentro da política de resiliência do DB.
                        // Se o banco de dados estiver offline (ex: MySqlException transiente), 
                        // o Polly tentará novamente com backoff exponencial.
                        await _dbPolicy.Policy.ExecuteAsync(async () =>
                            await dbContext.SaveChangesAsync(stoppingToken)
                        );
                    }
                    _logger.LogInformation("Reclamação {id} salva no banco de dados.", complaintEntity.Id);

                    // [Polly] A publicação do evento também é protegida pela sua própria política (SNS).
                    await PublishProcessedEvent(complaintEntity, stoppingToken);

                    await _sqsClient.DeleteMessageAsync(_sqsQueueUrl, message.ReceiptHandle, stoppingToken);
                    _logger.LogInformation("Mensagem {receipt} apagada da fila.", message.ReceiptHandle);
                }
                catch (Exception ex)
                {
                    // [Polly] Se a política de DB ou SNS falhar em todas as tentativas, 
                    // a exceção será capturada aqui, e a mensagem NÃO será apagada da fila.
                    // Isso garante que ela será reprocessada mais tarde.
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
            MessageAttributes = new Dictionary<string, Amazon.SimpleNotificationService.Model.MessageAttributeValue>
            {
                {
                    "EventType",
                    new Amazon.SimpleNotificationService.Model.MessageAttributeValue
                    {
                        DataType = "String",
                        StringValue = nameof(ComplaintProcessedEvent)
                    }
                }
            }
        };

        // [Polly] Executamos a publicação no SNS dentro da política de Retry do SNS.
        // Se a API do SNS falhar, o Polly tentará novamente.
        await _snsPolicy.Policy.ExecuteAsync(async () =>
            await _snsClient.PublishAsync(publishRequest, stoppingToken)
        );

        _logger.LogInformation("Evento ComplaintProcessedEvent publicado no tópico SNS para a reclamação {id}", complaint.Id);
    }
}