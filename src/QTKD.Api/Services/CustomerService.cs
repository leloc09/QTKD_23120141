using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QTKD.Api.Data;
using QTKD.Api.Domain;
using QTKD.Api.Dtos;

namespace QTKD.Api.Services;

public class CustomerService
{
    private static readonly Regex TaxCodePattern = new(@"^\d{10}(-\d{3})?$");

    private readonly QtkdDbContext _db;

    public CustomerService(QtkdDbContext db) => _db = db;

    public async Task<List<CustomerResponse>> ListAsync()
    {
        var customers = await _db.Customers.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        return customers.Select(ToResponse).ToList();
    }

    public async Task<Result<CustomerResponse>> GetAsync(Guid id)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        return customer is null ? NotFound(id) : ToResponse(customer);
    }

    public async Task<Result<CustomerResponse>> CreateAsync(CustomerRequest request)
    {
        var errors = Validate(request, out var status);
        if (errors.Count > 0) return ServiceError.Validation(errors);

        var customer = new Customer { Id = Guid.NewGuid() };
        Apply(customer, request, status);
        return await SaveAsync(customer, isNew: true);
    }

    public async Task<Result<CustomerResponse>> UpdateAsync(Guid id, CustomerRequest request)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id);
        if (customer is null) return NotFound(id);

        var errors = Validate(request, out var status);
        if (errors.Count > 0) return ServiceError.Validation(errors);

        Apply(customer, request, status);
        return await SaveAsync(customer, isNew: false);
    }

    public static List<FieldError> Validate(CustomerRequest request, out CustomerStatus status)
    {
        var errors = new List<FieldError>();
        status = CustomerStatus.Active;

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            errors.Add(new("name", "REQUIRED", "Tên khách hàng là bắt buộc."));
        else if (name.Length > 200)
            errors.Add(new("name", "TOO_LONG", "Tên khách hàng tối đa 200 ký tự."));

        var taxCode = request.TaxCode?.Trim();
        if (string.IsNullOrEmpty(taxCode))
            errors.Add(new("taxCode", "REQUIRED", "Mã số thuế là bắt buộc."));
        else if (!TaxCodePattern.IsMatch(taxCode))
            errors.Add(new("taxCode", "INVALID_FORMAT", "Mã số thuế gồm 10 chữ số hoặc 10 chữ số kèm '-' và 3 chữ số."));

        if (request.Address?.Trim().Length > 500)
            errors.Add(new("address", "TOO_LONG", "Địa chỉ tối đa 500 ký tự."));

        if (request.Status is not null && (!Enum.GetNames<CustomerStatus>().Contains(request.Status) || !Enum.TryParse(request.Status, out status)))
            errors.Add(new("status", "INVALID_VALUE", "Trạng thái chỉ nhận Active hoặc Inactive."));

        return errors;
    }

    private static void Apply(Customer customer, CustomerRequest request, CustomerStatus status)
    {
        customer.Name = request.Name!.Trim();
        customer.TaxCode = request.TaxCode!.Trim();
        customer.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        customer.Status = status;
    }

    private async Task<Result<CustomerResponse>> SaveAsync(Customer customer, bool isNew)
    {
        if (await _db.Customers.AnyAsync(c => c.TaxCode == customer.TaxCode && c.Id != customer.Id))
            return TaxCodeExists(customer.TaxCode);

        if (isNew) _db.Customers.Add(customer);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return TaxCodeExists(customer.TaxCode);
        }
        return ToResponse(customer);
    }

    private static ServiceError NotFound(Guid id) =>
        ServiceError.NotFound("CUSTOMER_NOT_FOUND", $"Không tìm thấy khách hàng {id}.");

    private static ServiceError TaxCodeExists(string taxCode) =>
        ServiceError.Conflict("CUSTOMER_TAXCODE_EXISTS", $"Mã số thuế {taxCode} đã thuộc về khách hàng khác.");

    private static CustomerResponse ToResponse(Customer c) =>
        new(c.Id, c.Name, c.TaxCode, c.Address, c.Status.ToString());
}
