namespace QTKD.Api.Domain;

public class Contract
{
    public Guid Id { get; set; }
    public string ContractNo { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public ContractStatus Status { get; set; } = ContractStatus.Draft;

    public Customer? Customer { get; set; }
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
