-- @first
select e.EmployeeId, e.LastName, e.FirstName, e.Title, e.TitleOfCourtesy, e.BirthDate, e.HireDate, e.Address, e.City, e.Region, e.PostalCode, e.Country, e.HomePhone, e.Extension, e.ReportsTo
from Employees e
where e.EmployeeId = @EmployeeId