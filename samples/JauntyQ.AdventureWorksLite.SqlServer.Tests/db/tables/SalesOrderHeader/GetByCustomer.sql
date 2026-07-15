select h.SalesOrderID, h.OrderDate, h.TotalDue
from Sales.SalesOrderHeader h
where h.CustomerID = @CustomerId
order by h.SalesOrderID
