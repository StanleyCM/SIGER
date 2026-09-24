namespace SIGER.Application.Base;

public class Result
{
    protected Result(bool isSuccess, IEnumerable<string>? errors = null)
    {
        IsSuccess = isSuccess;
        Errors = (errors ?? []).Where(error => !string.IsNullOrWhiteSpace(error)).ToArray();
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error => Errors.FirstOrDefault();
    public IReadOnlyCollection<string> Errors { get; }

    public static Result Success() => new(true);

    public static Result Failure(string error) => new(false, [error]);

    public static Result Failure(IEnumerable<string> errors) => new(false, errors);
}

public class Result<T> : Result
{
    private Result(bool isSuccess, T? value, IEnumerable<string>? errors = null)
        : base(isSuccess, errors)
    {
        Value = value;
    }

    public T? Value { get; }

    public static Result<T> Success(T value) => new(true, value);

    public new static Result<T> Failure(string error) => new(false, default, [error]);

    public new static Result<T> Failure(IEnumerable<string> errors) => new(false, default, errors);
}
