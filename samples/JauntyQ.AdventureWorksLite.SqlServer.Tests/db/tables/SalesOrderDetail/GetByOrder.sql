-- @allow-unindexed canonical adventureworks ships no index on SalesOrderDetail.ProductID; this sample keeps the upstream schema unmodified
-- Cross-schema join: Sales.SalesOrderDetail -> Production.Product.
-- LineTotal is a persisted computed column, verbatim from real
-- AdventureWorks: isnull(UnitPrice * (1 - UnitPriceDiscount) * OrderQty, 0).
select d.SalesOrderDetailID, p.Name as ProductName, d.OrderQty, d.UnitPrice, d.LineTotal
from Sales.SalesOrderDetail d
join Production.Product p on p.ProductID = d.ProductID
where d.SalesOrderID = @OrderId
order by d.SalesOrderDetailID
