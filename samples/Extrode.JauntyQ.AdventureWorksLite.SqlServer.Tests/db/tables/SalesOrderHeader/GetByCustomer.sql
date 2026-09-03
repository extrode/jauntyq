-- @allow-unindexed canonical adventureworks ships no index on SalesOrderHeader.CustomerID; this sample keeps the upstream schema unmodified
select h.SalesOrderID, h.OrderDate, h.TotalDue
from Sales.SalesOrderHeader h
where h.CustomerID = @CustomerId
order by h.SalesOrderID
