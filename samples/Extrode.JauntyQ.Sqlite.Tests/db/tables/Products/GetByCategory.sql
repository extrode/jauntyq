select ProductId, ProductName, SupplierId, CategoryId, UnitPrice, Discontinued
from Products
where CategoryId = @CategoryId
