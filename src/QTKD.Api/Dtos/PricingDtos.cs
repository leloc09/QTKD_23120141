namespace QTKD.Api.Dtos;

public record PriceListRequest(string? Name, DateOnly? EffectiveDate, List<PriceItemRequest>? Items);

public record PriceItemRequest(string? ServiceCode, decimal? UnitPrice);

public record PriceListSummary(Guid Id, string Name, DateOnly EffectiveDate, int ItemCount);

public record PriceListResponse(Guid Id, string Name, DateOnly EffectiveDate, IReadOnlyList<PriceItemResponse> Items);

public record PriceItemResponse(string ServiceCode, decimal UnitPrice);
