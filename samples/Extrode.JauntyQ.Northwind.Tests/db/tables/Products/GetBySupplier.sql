select p.ProductId, p.ProductName, p.UnitPrice, s.CompanyName as SupplierName
from Products p
join Suppliers s on p.SupplierId = s.SupplierId
where p.SupplierId = @SupplierId