using Amazon.SQS;
using Amazon.SQS.Model;
using ComplaintIngestion.API.Models;
using Polly.Retry; 
using System.Text.Json;

namespace ComplaintIngestion.API.Services;

public class SqsPublisherService
{
    private readonly IAmazonSQS _sqsClient;
    private readonly ILogger<SqsPublisherService> _logger;
    private readonly string _queueUrl;
    private readonly AsyncRetryPolicy _sqsPolicy; 

    // [Polly] Injetando a política de Retry (AsyncRetryPolicy) definida no Program.cs
    public SqsPublisherService(
        IAmazonSQS sqsClient,
        IConfiguration configuration,
        ILogger<SqsPublisherService> logger,
        AsyncRetryPolicy sqsPolicy) // [Polly] Injeção da política
    {
        _sqsClient = sqsClient;
        _logger = logger;
        _sqsPolicy = sqsPolicy; // [Polly] Armazenando a política

        // Usando o parâmetro 'configuration' diretamente e depois ele é "descartado"
        _queueUrl = configuration["Aws:SqsQueueUrl"] ?? throw new InvalidOperationException("A configuração 'Aws:SqsQueueUrl' está faltando. Verifique seu appsettings.Development.json.");
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

            // [Polly] Executamos a chamada de SQS dentro da política de Retry.
            // Se SendMessageAsync falhar (ex: AmazonServiceException), 
            // o Polly tentará novamente automaticamente, conforme definido no Program.cs.
            var response = await _sqsPolicy.ExecuteAsync(async () =>
                await _sqsClient.SendMessageAsync(sendMessageRequest)
            );

            _logger.LogInformation("Mensagem enviada com sucesso para SQS. MessageId: {MessageId}", response.MessageId);
        }
        catch (Exception ex)
        {
            // [Polly] Se todas as tentativas do Polly falharem, a exceção final será capturada aqui.
            _logger.LogError(ex, "Erro ao enviar mensagem para SQS após todas as tentativas. Fila: {QueueUrl}", _queueUrl);
            throw;
        }
    }
}
