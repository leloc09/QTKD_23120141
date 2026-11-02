namespace QTKD.Api.Domain;

public class Contract
{
    public Guid Id { get; set; }
    public string ContractNo { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public ContractStatus Status { get; set; } = ContractStatus.Draft;

    // Nội dung bắt buộc theo CTR-02 (giả định, xem docs/us03-traceability.md)
    public string? ServiceGroup { get; set; }
    public string? Content { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    // Ánh xạ cột hệ thống xmin của PostgreSQL: chặn 2 yêu cầu cùng sửa/gửi một hợp đồng
    public uint Version { get; set; }

    public Customer? Customer { get; set; }
    public List<ContractAttachment> Attachments { get; set; } = new();
    public List<ApprovalRequest> ApprovalRequests { get; set; } = new();
    public List<ContractHistory> History { get; set; } = new();
}

// Theo sơ đồ trạng thái Phase 1 (US03 chỉ dùng Draft -> PendingApproval)
public enum ContractStatus
{
    Draft,
    PendingApproval,
    RevisionRequested,
    Rejected,
    Approved,
    Active
}
