-- @first
select p.BusinessEntityID, p.FirstName, p.LastName, e.EmailAddress
from Person.Person p
join Person.EmailAddress e on e.BusinessEntityID = p.BusinessEntityID
where p.BusinessEntityID = @Id
