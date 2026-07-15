-- Aggregate over a computed column, joined cross-schema — SUM(LineTotal)
-- must infer its C# type from LineTotal's own decimal column type (fixed in
-- an earlier torture-test pass), not require an explicit -- @type directive.
select p.Name as ProductName, sum(d.LineTotal) as Revenue
from Sales.SalesOrderDetail d
join Production.Product p on p.ProductID = d.ProductID
group by p.Name
order by Revenue desc
