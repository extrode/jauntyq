-- Cross-schema join: HumanResources.Employee -> Person.Person.
select emp.BusinessEntityID, emp.JobTitle, p.FirstName, p.LastName
from HumanResources.Employee emp
join Person.Person p on p.BusinessEntityID = emp.BusinessEntityID
order by emp.BusinessEntityID
