namespace QTKD.Api.Services;

public enum ErrorKind
{
    BadRequest,
    NotFound,
    Conflict
}

public record FieldError(string Field, string Code, string Message);

public record ServiceError(ErrorKind Kind, string Code, string Message, IReadOnlyList<FieldError> Errors)
{
    public static ServiceError NotFound(string code, string message) => new(ErrorKind.NotFound, code, message, Array.Empty<FieldError>());
    public static ServiceError Conflict(string code, string message) => new(ErrorKind.Conflict, code, message, Array.Empty<FieldError>());
    public static ServiceError Validation(IReadOnlyList<FieldError> errors) =>
        new(ErrorKind.BadRequest, "VALIDATION_FAILED", "Dữ liệu không hợp lệ.", errors);
}

// Kết quả của một thao tác nghiệp vụ: hoặc có giá trị, hoặc có lỗi (không dùng exception cho lỗi nghiệp vụ)
public class Result<T>
{
    private Result(T? value, ServiceError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }
    public ServiceError? Error { get; }
    public bool IsSuccess => Error is null;

    public static implicit operator Result<T>(T value) => new(value, null);
    public static implicit operator Result<T>(ServiceError error) => new(default, error);
}
