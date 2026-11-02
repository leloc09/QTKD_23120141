using Microsoft.AspNetCore.Mvc;
using QTKD.Api.Dtos;
using QTKD.Api.Services;

namespace QTKD.Api.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    // Chưa có xác thực: người thực hiện lấy từ header X-User để ghi lịch sử
    protected string CurrentUser =>
        Request.Headers.TryGetValue("X-User", out var user) && !string.IsNullOrWhiteSpace(user)
            ? user.ToString().Trim()
            : "anonymous";

    protected IActionResult FromResult<T>(Result<T> result, Func<T, IActionResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value!) : Fail(result.Error!);

    protected IActionResult Fail(ServiceError error)
    {
        var status = error.Kind switch
        {
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return StatusCode(status, new ErrorResponse(error.Code, error.Message, error.Errors));
    }
}
