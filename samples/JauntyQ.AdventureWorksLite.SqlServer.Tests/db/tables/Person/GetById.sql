-- @first
select p.BusinessEntityID, p.FirstName, p.LastName
from Person.Person p
where p.BusinessEntityID = @Id
