select p.ProductId, p.ProductName, p.UnitPrice, p.UnitsInStock, c.CategoryName
from Products p
join Categories c on p.CategoryId = c.CategoryId
where p.CategoryId = @categoryId