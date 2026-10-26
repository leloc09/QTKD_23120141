namespace QTKD.Api.Domain;

public class PriceList
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly EffectiveDate { get; set; }

    public List<PriceItem> Items { get; set; } = new();
}
