-- @each Ids
select ProductId, ProductName, SupplierId, CategoryId, UnitPrice, Discontinued
from Products
where ProductId in (@Ids)
