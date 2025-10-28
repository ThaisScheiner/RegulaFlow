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

    // [Polly] Injetando a pol�tica de Retry (AsyncRetryPolicy) definida no Program.cs
    public SqsPublisherService(
        IAmazonSQS sqsClient,
        IConfiguration configuration,
        ILogger<SqsPublisherService> logger,
        AsyncRetryPolicy sqsPolicy) // [Polly] Inje��o da pol�tica
    {
        _sqsClient = sqsClient;
        _logger = logger;
        _sqsPolicy = sqsPolicy; // [Polly] Armazenando a pol�tica

        // Usando o par�metro 'configuration' diretamente e depois ele � "descartado"
        _queueUrl = configuration["Aws:SqsQueueUrl"] ?? throw new InvalidOperationException("A configura��o 'Aws:SqsQueueUrl' est� faltando. Verifique seu appsettings.Development.json.");
    }

    public async Task PublishComplaintAsync(ComplaintRequest complaint)
    {
        try
        {
            // Serializamos o objeto da reclama��o para JSON
            string messageBody = JsonSerializer.Serialize(complaint);

            var sendMessageRequest = new SendMessageRequest
            {
                QueueUrl = _queueUrl,
                MessageBody = messageBody
            };

            _logger.LogInformation("Enviando reclama��o para a fila SQS: {QueueUrl}", _queueUrl);

            // [Polly] Executamos a chamada de SQS dentro da pol�tica de Retry.
            // Se SendMessageAsync falhar (ex: AmazonServiceException), 
            // o Polly tentar� novamente automaticamente, conforme definido no Program.cs.
            var response = await _sqsPolicy.ExecuteAsync(async () =>
                await _sqsClient.SendMessageAsync(sendMessageRequest)
            );

            _logger.LogInformation("Mensagem enviada com sucesso para SQS. MessageId: {MessageId}", response.MessageId);
        }
        catch (Exception ex)
        {
            // [Polly] Se todas as tentativas do Polly falharem, a exce��o final ser� capturada aqui.
            _logger.LogError(ex, "Erro ao enviar mensagem para SQS ap�s todas as tentativas. Fila: {QueueUrl}", _queueUrl);
            throw;
        }
    }
}
