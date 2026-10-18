using Npgsql;
using OqTish.Application.Common;

namespace OqTish.Infrastructure.Common;

/// <summary>Classifies <see cref="PostgresException"/> (directly or wrapped, e.g. in DbUpdateException).</summary>
internal sealed class DbErrorClassifier : IDbErrorClassifier
{
    public DbError? Classify(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is PostgresException pg)
            {
                DbErrorKind? kind = pg.SqlState switch
                {
                    PostgresErrorCodes.ExclusionViolation => DbErrorKind.ExclusionViolation,
                    PostgresErrorCodes.UniqueViolation => DbErrorKind.UniqueViolation,
                    PostgresErrorCodes.ForeignKeyViolation => DbErrorKind.ForeignKeyViolation,
                    PostgresErrorCodes.CheckViolation => DbErrorKind.CheckViolation,
                    PostgresErrorCodes.SerializationFailure => DbErrorKind.SerializationFailure,
                    PostgresErrorCodes.DeadlockDetected => DbErrorKind.Deadlock,
                    _ => null,
                };
                return kind is null ? null : new DbError(kind.Value, pg.SqlState, pg.ConstraintName);
            }
        }

        return null;
    }
}
