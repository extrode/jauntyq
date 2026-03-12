select
    e.EmployeeId,
    e.LastName,
    e.FirstName,
    e.Title,
    m.FirstName as ManagerFirstName,
    m.LastName as ManagerLastName
from Employees e
left join Employees m on e.ReportsTo = m.EmployeeId