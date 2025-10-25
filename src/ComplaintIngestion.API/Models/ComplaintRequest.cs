using System.ComponentModel.DataAnnotations;

namespace ComplaintIngestion.API.Models;

// Usando Records do C# para DTOs imutáveis e concisos
public record ComplaintRequest(
    [Required] string CustomerName,
    [Required][EmailAddress] string CustomerEmail,
    [Required] string ComplaintType, // Ex: "Cobrança Indevida", "Atendimento", "Produto Defeituoso"
    [Required][StringLength(1000, MinimumLength = 10)] string Description
);