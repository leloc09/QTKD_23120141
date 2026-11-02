using QTKD.Api.Services;

namespace QTKD.Api.Dtos;

// Mẫu phản hồi lỗi thống nhất cho mọi API (xem docs/api.md)
public record ErrorResponse(string Code, string Message, IReadOnlyList<FieldError> Errors);
