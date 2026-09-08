namespace Restaurantes.Reporting.Infrastructure.Persistence;

public sealed class ReportingInboxMessage
{
    public Guid Id { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}
