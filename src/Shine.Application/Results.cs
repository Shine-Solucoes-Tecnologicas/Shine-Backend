namespace Shine.Application;

public sealed record Error(string Code, string Message, string Category = "Failure");

public static class ErrorCatalog
{
    public static readonly Error Validation = new("validation_error", "One or more validation errors occurred.", "Validation");
    public static readonly Error Unauthorized = new("unauthorized", "Authentication is required.", "Authorization");
    public static readonly Error Forbidden = new("forbidden", "You are not allowed to perform this operation.", "Authorization");
    public static readonly Error NotFound = new("not_found", "The requested resource was not found.", "NotFound");
    public static readonly Error Unexpected = new("unexpected_error", "An unexpected error occurred.", "Failure");
}

public class Result
{
    protected Result(bool isSuccess, IReadOnlyCollection<Error> errors)
    {
        IsSuccess = isSuccess;
        Errors = errors;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public IReadOnlyCollection<Error> Errors { get; }

    public static Result Success() => new(true, []);
    public static Result Failure(params Error[] errors) => new(false, errors);
}

public sealed class Result<T> : Result
{
    private Result(T value) : base(true, []) => Value = value;
    private Result(IReadOnlyCollection<Error> errors) : base(false, errors) { }

    public T? Value { get; }
    public static Result<T> Success(T value) => new(value);
    public static new Result<T> Failure(params Error[] errors) => new(errors);
}
