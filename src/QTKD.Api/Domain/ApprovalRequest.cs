namespace QTKD.Api.Domain;

// Mô phỏng "phiên phê duyệt" được tạo khi gửi trình duyệt (chưa có quy trình Approval đầy đủ)
public class ApprovalRequest
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public string? Comment { get; set; }
    public string? IdempotencyKey { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }

    public Contract? Contract { get; set; }
}

public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected,
    RevisionRequested
}
