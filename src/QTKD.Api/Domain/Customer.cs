namespace QTKD.Api.Domain;

public class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public CustomerStatus Status { get; set; } = CustomerStatus.Active;

    public List<Contract> Contracts { get; set; } = new();
}

public enum CustomerStatus
{
    Active,
    Inactive
}
