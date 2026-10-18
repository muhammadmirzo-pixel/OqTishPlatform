namespace OqTish.Domain.Common;

/// <summary>All entity ids are UUID v7 (ADR-010): time-ordered, not enumerable.</summary>
public static class IdGenerator
{
    public static Guid New() => Guid.CreateVersion7();
}
