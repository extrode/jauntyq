-- @first
-- TotalDue is a persisted computed column, verbatim from real AdventureWorks:
-- isnull(SubTotal + TaxAmt + Freight, 0).
select h.SalesOrderID, h.OrderDate, h.SubTotal, h.TaxAmt, h.Freight, h.TotalDue
from Sales.SalesOrderHeader h
where h.SalesOrderID = @Id
