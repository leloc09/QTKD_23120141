using QTKD.Api.Domain;

namespace QTKD.Api.Dtos;

public record ContractRequest(
    string? ContractNo,
    Guid? CustomerId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? ServiceGroup,
    string? Content);

public record AttachmentRequest(string? FileName, string? DocumentType);

public record SubmitRequest(string? Comment);

public record ContractSummary(
    Guid Id,
    string ContractNo,
    Guid CustomerId,
    string CustomerName,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status);

public record ContractDetail(
    Guid Id,
    string ContractNo,
    Guid CustomerId,
    string CustomerName,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    string? ServiceGroup,
    string? Content,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AttachmentResponse> Attachments,
    SubmitCheck SubmitCheck);

// Điều kiện gửi trình duyệt hiển thị ngay trên màn hình chi tiết (đã xác nhận ở Prototype)
public record SubmitCheck(bool CanSubmit, IReadOnlyList<SubmitCondition> Conditions);

public record AttachmentResponse(Guid Id, string FileName, string DocumentType, DateTimeOffset UploadedAt);

public record SubmitResponse(
    Guid ContractId,
    string ContractNo,
    string Status,
    Guid ApprovalId,
    DateTimeOffset SubmittedAt,
    bool Replayed);

public record HistoryResponse(
    Guid Id,
    string Action,
    string? FromStatus,
    string? ToStatus,
    string PerformedBy,
    string? Note,
    DateTimeOffset CreatedAt);
