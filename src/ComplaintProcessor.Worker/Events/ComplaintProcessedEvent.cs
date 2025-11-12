using System;

namespace ComplaintProcessor.Worker.Events;

// DTO imutável de evento
public record ComplaintProcessedEvent(
    Guid ComplaintId,
    string CustomerEmail,
    string ComplaintType,
    DateTime ProcessedAt
);
