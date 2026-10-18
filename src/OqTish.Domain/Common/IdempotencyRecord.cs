namespace OqTish.Domain.Common;

public enum IdempotencyStatus
{
    InProgress = 1,
    Completed = 2,
}

/// <summary>
/// Stored outcome of a request made with an <c>Idempotency-Key</c> (CONVENTIONS §5). The row is inserted
/// at the start of the business transaction and completed before commit, so concurrent duplicates block
/// on the primary key and then replay the committed response.
/// </summary>
public sealed class IdempotencyRecord
{
    private IdempotencyRecord()
    {
        Key = string.Empty;
        RequestHash = string.Empty;
    }

    public Guid UserId { get; private set; }

    public string Key { get; private set; }

    public string RequestHash { get; private set; }

    public IdempotencyStatus Status { get; private set; }

    public string? ResponseJson { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }
}
