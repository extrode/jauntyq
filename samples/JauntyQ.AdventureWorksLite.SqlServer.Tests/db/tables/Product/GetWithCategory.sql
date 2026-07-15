select p.ProductID, p.Name, sc.Name as SubcategoryName, cat.Name as CategoryName
from Production.Product p
join Production.ProductSubcategory sc on sc.ProductSubcategoryID = p.ProductSubcategoryID
join Production.ProductCategory cat on cat.ProductCategoryID = sc.ProductCategoryID
order by p.ProductID
