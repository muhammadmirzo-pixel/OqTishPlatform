namespace OqTish.Application.Common;

/// <summary>
/// <c>Idempotency-Key</c> support (CONVENTIONS §5) backed by <c>common.idempotency_keys</c>
/// (PK user_id + key). Call inside <see cref="ITransactionRunner"/>:
/// <list type="number">
/// <item>The key row is inserted first (<c>ON CONFLICT DO NOTHING</c>). A concurrent duplicate blocks on the
/// primary key until the first transaction finishes.</item>
/// <item>Existing row with a different request hash → 422 <c>common.idempotency_mismatch</c>.</item>
/// <item>Existing completed row → the stored response is deserialised and returned (replay); the work is not run.</item>
/// <item>Otherwise the work runs; on success the response is stored in the same transaction. On failure
/// the transaction rolls back and the key is free again.</item>
/// </list>
/// </summary>
public interface IIdempotencyStore
{
    public const int MaxKeyLength = 64;

    Task<Result<T>> RunAsync<T>(
        Guid userId,
        string key,
        object request,
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken);
}
