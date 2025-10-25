using ComplaintIngestion.API.Models;
using ComplaintIngestion.API.Services;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace ComplaintIngestion.API.Controllers;

[ApiController]
[Route("api/[controller]")] // Rota base será /api/Complaints
public class ComplaintsController : ControllerBase
{
    private readonly SqsPublisherService _sqsPublisher;
    private readonly ILogger<ComplaintsController> _logger;

    // Injetamos o serviço SQS e o logger
    public ComplaintsController(SqsPublisherService sqsPublisher, ILogger<ComplaintsController> logger)
    {
        _sqsPublisher = sqsPublisher;
        _logger = logger;
    }

    [HttpPost] // Mapeia para requisições POST
    [ProducesResponseType(StatusCodes.Status202Accepted)] // Resposta de sucesso
    [ProducesResponseType(StatusCodes.Status400BadRequest)] // Erro de validação
    [ProducesResponseType(StatusCodes.Status500InternalServerError)] // Erro interno (ex: falha ao enviar SQS)
    public async Task<IActionResult> SubmitComplaint([FromBody] ComplaintRequest request)
    {
        // A validação do modelo (DataAnnotations) é feita automaticamente pelo ASP.NET Core
        if (!ModelState.IsValid)
        {
            _logger.LogWarning("Recebida requisição inválida para submeter reclamação.");
            return BadRequest(ModelState); // Retorna 400 com os detalhes do erro
        }

        try
        {
            _logger.LogInformation("Recebida nova reclamação de {CustomerEmail}", request.CustomerEmail);
            await _sqsPublisher.PublishComplaintAsync(request);

            // Retornamos 202 Accepted, indicando que a requisição foi aceita
            // para processamento assíncrono, mas ainda não foi concluída.
            _logger.LogInformation("Reclamação de {CustomerEmail} enviada para processamento.", request.CustomerEmail);
            return Accepted();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar a submissão da reclamação de {CustomerEmail}", request.CustomerEmail);
            // Retorna um erro genérico 500 para não expor detalhes internos
            return StatusCode(StatusCodes.Status500InternalServerError, "Ocorreu um erro ao processar sua solicitação.");
        }
    }
}