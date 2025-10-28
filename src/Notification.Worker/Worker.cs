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

    // [Polly] Campo para armazenar nossa pol�tica combinada (Retry + Circuit Breaker)
    private readonly IAsyncPolicy _notificationPolicy;

    public Worker(
        ILogger<Worker> logger,
        IAmazonSQS sqsClient,
        IConfiguration configuration,
        IAsyncPolicy notificationPolicy) // [Polly] Injetando a pol�tica combinada
    {
        _logger = logger;
        _sqsClient = sqsClient;
        _notificationPolicy = notificationPolicy; // [Polly] Armazenando a pol�tica
        _queueUrl = configuration["Aws:SqsQueueUrl"] ?? throw new ArgumentNullException("Aws:SqsQueueUrl");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker de Notifica��o iniciado em: {time}", DateTimeOffset.Now);

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Verificando a fila de notifica��es por novas mensagens...");

            // ... (Nenhuma mudan�a na l�gica de recebimento de SQS) ...
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

            _logger.LogInformation("{count} mensagens de notifica��o recebidas.", response.Messages.Count);

            foreach (var message in response.Messages)
            {
                try
                {
                    // ... (Nenhuma mudan�a na l�gica de deserializa��o) ...
                    if (string.IsNullOrWhiteSpace(message.Body))
                    {
                        _logger.LogWarning("Recebida mensagem com corpo vazio ou nulo. MessageId: {id}", message.MessageId);
                        await _sqsClient.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
                        continue;
                    }

                    var snsMessage = JsonSerializer.Deserialize<SnsMessage>(message.Body);

                    if (snsMessage is null || string.IsNullOrWhiteSpace(snsMessage.Message))
                    {
                        _logger.LogWarning("N�o foi poss�vel deserializar o envelope SNS ou a mensagem interna est� vazia. Body: {body}", message.Body);
                        await _sqsClient.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
                        continue;
                    }

                    var processedEvent = JsonSerializer.Deserialize<ComplaintProcessedEvent>(snsMessage.Message);
                    if (processedEvent is null)
                    {
                        _logger.LogWarning("N�o foi poss�vel deserializar o evento ComplaintProcessedEvent. Message: {message}", snsMessage.Message);
                        await _sqsClient.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
                        continue;
                    }

                    _logger.LogInformation("Evento de reclama��o processada recebido para o ID: {ComplaintId}", processedEvent.ComplaintId);

                    // [Polly] Executamos a simula��o de envio de notifica��o dentro da pol�tica.
                    // Se o bloco abaixo falhar, o Polly tentar� novamente.
                    // Se falhar 5 vezes seguidas, o Circuit Breaker abrir�.
                    await _notificationPolicy.ExecuteAsync(async () =>
                    {
                        _logger.LogInformation(">>> SIMULANDO ENVIO DE E-MAIL para {CustomerEmail}: Sua reclama��o sobre '{ComplaintType}' foi recebida e est� sendo processada.",
                            processedEvent.CustomerEmail, processedEvent.ComplaintType);

                        await Task.Delay(200, stoppingToken); // Simula o tempo de I/O de uma chamada de API

                        // [Polly] TESTE: Descomente a linha abaixo para simular uma falha.
                        // throw new Exception("Simula��o de falha na API de notifica��o");
                    });

                    // [Polly] A mensagem s� � apagada da fila se a pol�tica do Polly for bem-sucedida.
                    await _sqsClient.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
                    _logger.LogInformation("Mensagem de notifica��o apagada da fila.");
                }
                catch (Exception ex)
                {
                    // [Polly] Se o Polly falhar (ex: Circuit Breaker est� aberto ou as retentativas falharam),
                    // a exce��o ser� capturada aqui. A mensagem N�O ser� apagada.
                    _logger.LogError(ex, "Erro fatal ao processar a mensagem de notifica��o {messageId}. A mensagem n�o ser� apagada para an�lise.", message.MessageId);
                }
            }
        }
    }
}
