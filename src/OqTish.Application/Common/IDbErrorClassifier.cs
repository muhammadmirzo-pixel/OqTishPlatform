namespace OqTish.Application.Common;

public enum DbErrorKind
{
    /// <summary>23P01 exclusion_violation (e.g. overlapping doctor time blocks) → Conflict.</summary>
    ExclusionViolation = 1,

    /// <summary>23505 unique_violation → Conflict or idempotent replay.</summary>
    UniqueViolation = 2,

    /// <summary>23503 foreign_key_violation → NotFound / Validation.</summary>
    ForeignKeyViolation = 3,

    /// <summary>23514 check_violation (a bug: validation should have caught it).</summary>
    CheckViolation = 4,

    /// <summary>40001 serialization_failure (retryable).</summary>
    SerializationFailure = 5,

    /// <summary>40P01 deadlock_detected (retryable).</summary>
    Deadlock = 6,
}

/// <summary>A classified PostgreSQL error. <see cref="ConstraintName"/> lets handlers tell constraints apart.</summary>
public sealed record DbError(DbErrorKind Kind, string SqlState, string? ConstraintName)
{
    public bool IsTransient => Kind is DbErrorKind.SerializationFailure or DbErrorKind.Deadlock;
}

/// <summary>Maps database exceptions (also when wrapped in DbUpdateException) to <see cref="DbError"/>.</summary>
public interface IDbErrorClassifier
{
    /// <summary>Returns null when the exception is not a classified PostgreSQL error.</summary>
    DbError? Classify(Exception exception);
}
