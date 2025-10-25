using Amazon.SQS;
using Amazon.SQS.Model;
using ComplaintIngestion.API.Models;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace ComplaintIngestion.API.Services;

public class SqsPublisherService
{
    private readonly IAmazonSQS _sqsClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SqsPublisherService> _logger;
    private readonly string _queueUrl;

    // Injetamos o cliente SQS, configurações e logger
    public SqsPublisherService(IAmazonSQS sqsClient, IConfiguration configuration, ILogger<SqsPublisherService> logger)
    {
        _sqsClient = sqsClient;
        _configuration = configuration;
        _logger = logger;
        // Lemos a URL da fila do appsettings.json
        _queueUrl = _configuration["Aws:SqsQueueUrl"] ?? throw new ArgumentNullException("Aws:SqsQueueUrl cannot be null");
    }

    public async Task PublishComplaintAsync(ComplaintRequest complaint)
    {
        try
        {
            // Serializamos o objeto da reclamação para JSON
            string messageBody = JsonSerializer.Serialize(complaint);

            var sendMessageRequest = new SendMessageRequest
            {
                QueueUrl = _queueUrl,
                MessageBody = messageBody
            };

            _logger.LogInformation("Enviando reclamação para a fila SQS: {QueueUrl}", _queueUrl);

            // Enviamos a mensagem para a fila
            var response = await _sqsClient.SendMessageAsync(sendMessageRequest);

            _logger.LogInformation("Mensagem enviada com sucesso para SQS. MessageId: {MessageId}", response.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao enviar mensagem para SQS. Fila: {QueueUrl}", _queueUrl);
            // Em um cenário real, poderíamos implementar retentativas ou DLQ aqui
            throw; // Re-lança a exceção para o Controller saber que falhou
        }
    }
}