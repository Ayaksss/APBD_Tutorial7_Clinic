namespace Tutorial7.DTOs;

public enum ServiceErrorType
{
    None,
    NotFound,
    BadRequest,
    Conflict
}

public class ServiceResult
{
    public bool IsSuccess { get; private set; }
    public string ErrorMessage { get; private set; } = string.Empty;
    public ServiceErrorType ErrorType { get; private set; }
    public int NewId { get; private set; }

    public static ServiceResult Ok(int newId = 0) =>
        new() { IsSuccess = true, NewId = newId };

    public static ServiceResult Fail(string message, ServiceErrorType type) =>
        new() { IsSuccess = false, ErrorMessage = message, ErrorType = type };
}
