namespace JauntyQ.EShopOnWeb.Postgres.Tests.Domain;

public sealed class OrderItem
{
    public CatalogItemOrdered ItemOrdered { get; }
    public decimal UnitPrice { get; }
    public int Units { get; }

    public OrderItem(CatalogItemOrdered itemOrdered, decimal unitPrice, int units)
    {
        ItemOrdered = itemOrdered;
        UnitPrice = unitPrice;
        Units = units;
    }
}
