-- Reads Sales.vSalesOrderDetailExtended, a plain reporting VIEW (see
-- schema.mssql.sql) — modeled here as a read-only query source, never as an
-- AutoCrud target (this project has JauntyQAutoCrud off). Confirms a view
-- works like any other SELECT source with no special handling needed.
select d.SalesOrderDetailID, d.ProductName, d.OrderQty, d.LineTotal
from Sales.vSalesOrderDetailExtended d
where d.SalesOrderID = @OrderId
order by d.SalesOrderDetailID
