using System;

namespace ComplaintProcessor.Worker.Events;

// Usando record para um DTO de evento simples e imutável
public record ComplaintProcessedEvent(
    Guid ComplaintId,
    string CustomerEmail,
    string ComplaintType,
    DateTime ProcessedAt
);