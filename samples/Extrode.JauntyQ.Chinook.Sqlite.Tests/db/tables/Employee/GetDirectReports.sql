-- @each ManagerIds
select EmployeeId, LastName, FirstName, Title, ReportsTo
from Employee
where ReportsTo in (@ManagerIds)
order by ReportsTo, EmployeeId
