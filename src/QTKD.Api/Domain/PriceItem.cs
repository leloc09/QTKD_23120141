namespace QTKD.Api.Domain;

public class PriceItem
{
    public Guid PriceListId { get; set; }
    public string ServiceCode { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }

    public PriceList? PriceList { get; set; }
}
