namespace QTKD.Api.Dtos;

public record CustomerRequest(string? Name, string? TaxCode, string? Address, string? Status);

public record CustomerResponse(Guid Id, string Name, string TaxCode, string? Address, string Status);
