using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QTKD.Api.Data;
using QTKD.Api.Domain;
using QTKD.Api.Dtos;

namespace QTKD.Api.Services;

public class PricingService
{
    private static readonly Regex ServiceCodePattern = new(@"^[A-Z0-9][A-Z0-9-]{0,49}$");

    private readonly QtkdDbContext _db;

    public PricingService(QtkdDbContext db) => _db = db;

    public Task<List<PriceListSummary>> ListAsync() =>
        _db.PriceLists.AsNoTracking()
            .OrderByDescending(p => p.EffectiveDate)
            .Select(p => new PriceListSummary(p.Id, p.Name, p.EffectiveDate, p.Items.Count))
            .ToListAsync();

    public async Task<Result<PriceListResponse>> GetAsync(Guid id)
    {
        var priceList = await _db.PriceLists.AsNoTracking().Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id);
        return priceList is null ? NotFound(id) : ToResponse(priceList);
    }

    public async Task<Result<PriceListResponse>> CreateAsync(PriceListRequest request)
    {
        var errors = new List<FieldError>();
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            errors.Add(new("name", "REQUIRED", "Tên bảng giá là bắt buộc."));
        else if (name.Length > 200)
            errors.Add(new("name", "TOO_LONG", "Tên bảng giá tối đa 200 ký tự."));
        if (request.EffectiveDate is null)
            errors.Add(new("effectiveDate", "REQUIRED", "Ngày hiệu lực là bắt buộc."));

        var items = request.Items ?? new List<PriceItemRequest>();
        for (var i = 0; i < items.Count; i++)
            errors.AddRange(ValidateItem(items[i], $"items[{i}]."));

        var duplicate = items.Select(x => x.ServiceCode?.Trim()).Where(c => c is not null)
            .GroupBy(c => c).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            errors.Add(new("items", "DUPLICATE_SERVICE_CODE", $"Mã dịch vụ {duplicate.Key} bị lặp trong bảng giá."));

        if (errors.Count > 0) return ServiceError.Validation(errors);

        var priceList = new PriceList
        {
            Id = Guid.NewGuid(),
            Name = name!,
            EffectiveDate = request.EffectiveDate!.Value,
            Items = items.Select(x => new PriceItem { ServiceCode = x.ServiceCode!.Trim(), UnitPrice = x.UnitPrice!.Value }).ToList()
        };
        _db.PriceLists.Add(priceList);
        await _db.SaveChangesAsync();
        return ToResponse(priceList);
    }

    public async Task<Result<List<PriceItemResponse>>> ListItemsAsync(Guid priceListId)
    {
        if (!await _db.PriceLists.AnyAsync(p => p.Id == priceListId)) return NotFound(priceListId);

        return await _db.PriceItems.AsNoTracking()
            .Where(i => i.PriceListId == priceListId)
            .OrderBy(i => i.ServiceCode)
            .Select(i => new PriceItemResponse(i.ServiceCode, i.UnitPrice))
            .ToListAsync();
    }

    public async Task<Result<PriceItemResponse>> AddItemAsync(Guid priceListId, PriceItemRequest request)
    {
        if (!await _db.PriceLists.AnyAsync(p => p.Id == priceListId)) return NotFound(priceListId);

        var errors = ValidateItem(request, "");
        if (errors.Count > 0) return ServiceError.Validation(errors);

        var item = new PriceItem
        {
            PriceListId = priceListId,
            ServiceCode = request.ServiceCode!.Trim(),
            UnitPrice = request.UnitPrice!.Value
        };
        if (await _db.PriceItems.AnyAsync(i => i.PriceListId == priceListId && i.ServiceCode == item.ServiceCode))
            return ItemExists(item.ServiceCode);

        _db.PriceItems.Add(item);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return ItemExists(item.ServiceCode);
        }
        return new PriceItemResponse(item.ServiceCode, item.UnitPrice);
    }

    public static List<FieldError> ValidateItem(PriceItemRequest item, string prefix)
    {
        var errors = new List<FieldError>();
        var code = item.ServiceCode?.Trim();
        if (string.IsNullOrEmpty(code))
            errors.Add(new($"{prefix}serviceCode", "REQUIRED", "Mã dịch vụ là bắt buộc."));
        else if (!ServiceCodePattern.IsMatch(code))
            errors.Add(new($"{prefix}serviceCode", "INVALID_FORMAT", "Mã dịch vụ chỉ gồm chữ in hoa, số và '-', tối đa 50 ký tự."));

        if (item.UnitPrice is null)
            errors.Add(new($"{prefix}unitPrice", "REQUIRED", "Đơn giá là bắt buộc."));
        else if (item.UnitPrice <= 0)
            errors.Add(new($"{prefix}unitPrice", "MUST_BE_POSITIVE", "Đơn giá phải lớn hơn 0."));
        else if (decimal.Round(item.UnitPrice.Value, 2) != item.UnitPrice || item.UnitPrice >= 10_000_000_000_000_000m)
            errors.Add(new($"{prefix}unitPrice", "INVALID_PRECISION", "Đơn giá tối đa 16 chữ số phần nguyên và 2 chữ số thập phân."));
        return errors;
    }

    private static ServiceError NotFound(Guid id) =>
        ServiceError.NotFound("PRICELIST_NOT_FOUND", $"Không tìm thấy bảng giá {id}.");

    private static ServiceError ItemExists(string code) =>
        ServiceError.Conflict("PRICEITEM_EXISTS", $"Mã dịch vụ {code} đã có trong bảng giá.");

    private static PriceListResponse ToResponse(PriceList p) =>
        new(p.Id, p.Name, p.EffectiveDate,
            p.Items.OrderBy(i => i.ServiceCode).Select(i => new PriceItemResponse(i.ServiceCode, i.UnitPrice)).ToList());
}
