namespace Microsoft.eShopWeb.MySql.Tests.Domain;

public sealed class CatalogItemOrdered
{
    public int CatalogItemId { get; }
    public string ProductName { get; }
    public string PictureUri { get; }

    public CatalogItemOrdered(int catalogItemId, string productName, string pictureUri)
    {
        CatalogItemId = catalogItemId;
        ProductName = productName;
        PictureUri = pictureUri;
    }
}
