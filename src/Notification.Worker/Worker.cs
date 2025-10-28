using Amazon.SQS;
using Amazon.SQS.Model;
using Notification.Worker.Events;
using Polly; // [Polly] Importar a interface base do Polly
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Notification.Worker;

public record SnsMessage(
    [property: JsonPropertyName("Message")] string Message
);

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IAmazonSQS _sqsClient;
    private readonly string _queueUrl;

    // [Polly] Campo para armazenar nossa política combinada (Retry + Circuit Breaker)
    private readonly IAsyncPolicy _notificationPolicy;

    public Worker(
        ILogger<Worker> logger,
        IAmazonSQS sqsClient,
        IConfiguration configuration,
        IAsyncPolicy notificationPolicy) // [Polly] Injetando a política combinada
    {
        _logger = logger;
        _sqsClient = sqsClient;
        _notificationPolicy = notificationPolicy; // [Polly] Armazenando a política
        _queueUrl = configuration["Aws:SqsQueueUrl"] ?? throw new ArgumentNullException("Aws:SqsQueueUrl");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker de Notificação iniciado em: {time}", DateTimeOffset.Now);

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Verificando a fila de notificações por novas mensagens...");

            // ... (Nenhuma mudança na lógica de recebimento de SQS) ...
            var receiveRequest = new ReceiveMessageRequest
            {
                QueueUrl = _queueUrl,
                MaxNumberOfMessages = 1,
                WaitTimeSeconds = 20
            };

            var response = await _sqsClient.ReceiveMessageAsync(receiveRequest, stoppingToken);

            if (response?.Messages?.Any() != true)
            {
                _logger.LogInformation("Nenhuma mensagem na fila.");
                continue;
            }

            _logger.LogInformation("{count} mensagens de notificação recebidas.", response.Messages.Count);

            foreach (var message in response.Messages)
            {
                try
                {
                    // ... (Nenhuma mudança na lógica de deserialização) ...
                    if (string.IsNullOrWhiteSpace(message.Body))
                    {
                        _logger.LogWarning("Recebida mensagem com corpo vazio ou nulo. MessageId: {id}", message.MessageId);
                        await _sqsClient.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
                        continue;
                    }

                    var snsMessage = JsonSerializer.Deserialize<SnsMessage>(message.Body);

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

                    // [Polly] Executamos a simulação de envio de notificação dentro da política.
                    // Se o bloco abaixo falhar, o Polly tentará novamente.
                    // Se falhar 5 vezes seguidas, o Circuit Breaker abrirá.
                    await _notificationPolicy.ExecuteAsync(async () =>
                    {
                        _logger.LogInformation(">>> SIMULANDO ENVIO DE E-MAIL para {CustomerEmail}: Sua reclamação sobre '{ComplaintType}' foi recebida e está sendo processada.",
                            processedEvent.CustomerEmail, processedEvent.ComplaintType);

                        await Task.Delay(200, stoppingToken); // Simula o tempo de I/O de uma chamada de API

                        // [Polly] TESTE: Descomente a linha abaixo para simular uma falha.
                        // throw new Exception("Simulação de falha na API de notificação");
                    });

                    // [Polly] A mensagem só é apagada da fila se a política do Polly for bem-sucedida.
                    await _sqsClient.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
                    _logger.LogInformation("Mensagem de notificação apagada da fila.");
                }
                catch (Exception ex)
                {
                    // [Polly] Se o Polly falhar (ex: Circuit Breaker está aberto ou as retentativas falharam),
                    // a exceção será capturada aqui. A mensagem NÃO será apagada.
                    _logger.LogError(ex, "Erro fatal ao processar a mensagem de notificação {messageId}. A mensagem não será apagada para análise.", message.MessageId);
                }
            }
        }
    }
}
