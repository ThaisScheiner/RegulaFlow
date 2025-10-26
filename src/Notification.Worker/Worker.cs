using Amazon.SQS;
using Amazon.SQS.Model;
using Notification.Worker.Events;
using System.Text.Json;
using System.Text.Json.Serialization; // Adicionar este using

namespace Notification.Worker;

// DTO para deserializar a mensagem externa que vem do SNS
// Adicionamos o [JsonPropertyName] para garantir o mapeamento correto, independentemente do case.
public record SnsMessage(
    [property: JsonPropertyName("Message")] string Message
);

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IAmazonSQS _sqsClient;
    private readonly string _queueUrl;

    public Worker(ILogger<Worker> logger, IAmazonSQS sqsClient, IConfiguration configuration)
    {
        _logger = logger;
        _sqsClient = sqsClient;
        _queueUrl = configuration["Aws:SqsQueueUrl"] ?? throw new ArgumentNullException("Aws:SqsQueueUrl");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker de Notificação iniciado em: {time}", DateTimeOffset.Now);

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Verificando a fila de notificações por novas mensagens...");

            var receiveRequest = new ReceiveMessageRequest
            {
                QueueUrl = _queueUrl,
                MaxNumberOfMessages = 1,
                WaitTimeSeconds = 20
            };

            var response = await _sqsClient.ReceiveMessageAsync(receiveRequest, stoppingToken);

            // --- CORREÇÃO 1: VERIFICAÇÃO DEFENSIVA ---
            // Garante que a resposta e a lista de mensagens não são nulas antes de continuar.
            if (response?.Messages?.Any() != true)
            {
                _logger.LogInformation("Nenhuma mensagem na fila.");
                continue; // Volta para o início do loop
            }

            _logger.LogInformation("{count} mensagens de notificação recebidas.", response.Messages.Count);

            foreach (var message in response.Messages)
            {
                try
                {
                    // --- CORREÇÃO 2: VERIFICAÇÃO DE MENSAGEM VAZIA ---
                    if (string.IsNullOrWhiteSpace(message.Body))
                    {
                        _logger.LogWarning("Recebida mensagem com corpo vazio ou nulo. MessageId: {id}", message.MessageId);
                        // Apaga a mensagem "ruim" para não travar a fila
                        await _sqsClient.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
                        continue;
                    }

                    var snsMessage = JsonSerializer.Deserialize<SnsMessage>(message.Body);

                    // --- CORREÇÃO 3: VERIFICAÇÃO APÓS DESERIALIZAÇÃO ---
                    if (snsMessage is null || string.IsNullOrWhiteSpace(snsMessage.Message))
                    {
                        _logger.LogWarning("Não foi possível deserializar o envelope SNS ou a mensagem interna está vazia. Body: {body}", message.Body);
                        await _sqsClient.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
                        continue;
                    }

                    var processedEvent = JsonSerializer.Deserialize<ComplaintProcessedEvent>(snsMessage.Message);
                    if (processedEvent is null)
                    {
                        _logger.LogWarning("Não foi possível deserializar o evento ComplaintProcessedEvent. Message: {message}", snsMessage.Message);
                        await _sqsClient.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
                        continue;
                    }

                    _logger.LogInformation("Evento de reclamação processada recebido para o ID: {ComplaintId}", processedEvent.ComplaintId);

                    _logger.LogInformation(">>> SIMULANDO ENVIO DE E-MAIL para {CustomerEmail}: Sua reclamação sobre '{ComplaintType}' foi recebida e está sendo processada.",
                        processedEvent.CustomerEmail, processedEvent.ComplaintType);

                    await _sqsClient.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
                    _logger.LogInformation("Mensagem de notificação apagada da fila.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro fatal ao processar a mensagem de notificação {messageId}. A mensagem não será apagada para análise.", message.MessageId);
                }
            }
        }
    }
}