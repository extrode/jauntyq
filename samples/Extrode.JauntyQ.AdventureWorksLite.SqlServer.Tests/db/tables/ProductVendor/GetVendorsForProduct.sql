-- Cross-schema composite-PK junction: Purchasing.ProductVendor references
-- both Production.Product and Purchasing.Vendor.
select v.Name as VendorName, pv.AverageLeadTime, pv.StandardPrice
from Purchasing.ProductVendor pv
join Purchasing.Vendor v on v.BusinessEntityID = pv.BusinessEntityID
join Production.Product p on p.ProductID = pv.ProductID
where p.ProductID = @ProductId
order by pv.StandardPrice
