namespace AutoFlow.Application.Common;

/// <summary>
/// The outcome of a use case: either a value or an <see cref="Common.Error"/>.
/// Create instances through <see cref="Result.Success{T}(T)"/> and
/// <see cref="Result.Failure{T}(Common.Error)"/>.
/// </summary>
public sealed class Result<T>
{
    private readonly T? _value;
    private readonly Error? _error;

    internal Result(T value)
    {
        _value = value;
        IsSuccess = true;
    }

    internal Result(Error error)
    {
        _error = error;
        IsSuccess = false;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    /// <summary>The value of a successful result.</summary>
    /// <exception cref="InvalidOperationException">The result is a failure.</exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({_error!.Code}).");

    /// <summary>The error of a failed result.</summary>
    /// <exception cref="InvalidOperationException">The result is a success.</exception>
    public Error Error => _error
        ?? throw new InvalidOperationException("Cannot read the error of a successful result.");

    /// <summary>Folds the result into a single value, e.g. an HTTP response.</summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess ? onSuccess(_value!) : onFailure(_error!);
    }
}

/// <summary>Factory methods for <see cref="Result{T}"/>.</summary>
public static class Result
{
    public static Result<T> Success<T>(T value) => new(value);

    public static Result<T> Failure<T>(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(error);
    }
}
