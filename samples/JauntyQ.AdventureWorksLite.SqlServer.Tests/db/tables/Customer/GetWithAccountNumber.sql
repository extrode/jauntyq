-- @first
-- AccountNumber is a persisted computed column (see schema.mssql.sql).
select c.CustomerID, c.AccountNumber, c.PersonID
from Sales.Customer c
where c.CustomerID = @Id
