-- @allow-unindexed canonical adventureworks ships no index on Product.ProductSubcategoryID; this sample keeps the upstream schema unmodified
select p.ProductID, p.Name, p.ListPrice
from Production.Product p
where p.ProductSubcategoryID = @SubcategoryId
order by p.ProductID
