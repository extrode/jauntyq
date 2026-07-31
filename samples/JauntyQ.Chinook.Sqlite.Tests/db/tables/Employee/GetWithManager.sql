select e.EmployeeId, e.LastName, e.FirstName, e.Title,
       m.LastName as ManagerLastName, m.FirstName as ManagerFirstName
from Employee e
left join Employee m on m.EmployeeId = e.ReportsTo
order by e.EmployeeId
