using System;

namespace Notification.Worker.Events;

// A estrutura da mensagem esperada a receber
public record ComplaintProcessedEvent(
    Guid ComplaintId,
    string CustomerEmail,
    string ComplaintType,
    DateTime ProcessedAt
);