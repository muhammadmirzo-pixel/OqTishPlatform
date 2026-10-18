using System.Diagnostics.CodeAnalysis;

namespace OqTish.Application.Common;

/// <summary>Outcome of a handler without a value. Exceptions are for bugs and infrastructure failures only.</summary>
public readonly struct Result : IEquatable<Result>
{
    private Result(Error? error) => Error = error;

    public Error? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static Result Success => default;

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }

    public static implicit operator Result(Error error) => Failure(error);

    public static bool operator ==(Result left, Result right) => left.Equals(right);

    public static bool operator !=(Result left, Result right) => !left.Equals(right);

    public bool Equals(Result other) => Equals(Error, other.Error);

    public override bool Equals(object? obj) => obj is Result other && Equals(other);

    public override int GetHashCode() => Error?.GetHashCode() ?? 0;
}

/// <summary>Outcome of a handler: either a value or an <see cref="Common.Error"/>.</summary>
public readonly struct Result<T> : IEquatable<Result<T>>
{
    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        Error = null;
    }

    private Result(Error error)
    {
        _value = default;
        Error = error;
    }

    public Error? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    /// <summary>The value; throws when the result is a failure.</summary>
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Result is a failure ({Error.Code}).");

    public static implicit operator Result<T>(T value) => new(value);

    public static implicit operator Result<T>(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(error);
    }

    public static bool operator ==(Result<T> left, Result<T> right) => left.Equals(right);

    public static bool operator !=(Result<T> left, Result<T> right) => !left.Equals(right);

    public Result<TOut> Map<TOut>(Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return IsSuccess ? map(_value!) : Error;
    }

    public bool Equals(Result<T> other) =>
        Equals(Error, other.Error) && EqualityComparer<T?>.Default.Equals(_value, other._value);

    public override bool Equals(object? obj) => obj is Result<T> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Error, _value);
}
