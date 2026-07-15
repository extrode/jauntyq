select p.ProductID, p.Name, p.ListPrice
from Production.Product p
where p.ProductSubcategoryID = @SubcategoryId
order by p.ProductID
