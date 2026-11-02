namespace QTKD.Api.Domain;

// Lịch sử xử lý hợp đồng (bước 6 của UC-CON-03)
public class ContractHistory
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public string Action { get; set; } = string.Empty;
    public ContractStatus? FromStatus { get; set; }
    public ContractStatus? ToStatus { get; set; }
    public string PerformedBy { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Contract? Contract { get; set; }
}
