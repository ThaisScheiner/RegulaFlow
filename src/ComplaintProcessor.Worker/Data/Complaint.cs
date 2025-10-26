using System;
using System.ComponentModel.DataAnnotations;

namespace ComplaintProcessor.Worker.Data;

public class Complaint
{
    [Key]
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string ComplaintType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }
    public ComplaintStatus Status { get; set; }
}

public enum ComplaintStatus
{
    Received,
    Processing,
    Closed
}