namespace QTKD.Api.Domain;

public class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    // Mã số thuế: bắt buộc, duy nhất (10 số hoặc 10 số + "-" + 3 số)
    public string TaxCode { get; set; } = string.Empty;
    public string? Address { get; set; }
    public CustomerStatus Status { get; set; } = CustomerStatus.Active;

    public List<Contract> Contracts { get; set; } = new();
}

public enum CustomerStatus
{
    Active,
    Inactive
}
