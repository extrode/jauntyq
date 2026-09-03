--select p.ProductId, p.ProductName, p.SupplierId, p.CategoryId, p.QuantityPerUnit, p.UnitPrice, p.UnitsInStock, p.UnitsOnOrder, p.ReorderLevel, p.Discontinued
--from Products p

SELECT 
	ProductId, 
	SupplierId, 
	CategoryId, 
	QuantityPerUnit, 
	UnitPrice, 
	UnitsInStock, 
	UnitsOnOrder, 
	ReorderLevel, 
	Discontinued
FROM
	Products;