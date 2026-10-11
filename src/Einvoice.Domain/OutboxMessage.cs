namespace Einvoice.Domain;

public sealed class OutboxMessage
{
    public Guid Id { get; private set; }
    public string Type { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public DateTimeOffset? PublishedAtUtc { get; private set; }
    public int Attempts { get; private set; }

    private OutboxMessage() { } // EF

    public static OutboxMessage Create(string type, string payload)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new DomainValidationException("Type is required.");
        if (string.IsNullOrWhiteSpace(payload))
            throw new DomainValidationException("Payload is required.");

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = type,
            Payload = payload,
            OccurredAtUtc = DateTimeOffset.UtcNow
        };
    }

    public void MarkPublished(DateTimeOffset publishedAtUtc) => PublishedAtUtc = publishedAtUtc;

    public void IncrementAttempt() => Attempts++;
}
