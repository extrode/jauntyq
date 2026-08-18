-- @allow-unindexed canonical adventureworks ships no index on Customer.TerritoryID; this sample keeps the upstream schema unmodified
select c.CustomerID, c.AccountNumber, t.Name as TerritoryName
from Sales.Customer c
join Sales.SalesTerritory t on t.TerritoryID = c.TerritoryID
where t.TerritoryID = @TerritoryId
order by c.CustomerID
