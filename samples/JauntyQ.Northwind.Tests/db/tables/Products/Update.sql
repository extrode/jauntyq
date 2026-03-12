UPDATE Products
SET ProductName = @ProductName, UnitPrice = @UnitPrice, Discontinued = @Discontinued
WHERE ProductId = @ProductId
