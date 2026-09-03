-- @each EmployeeIds
select o.OrderId, o.CustomerId, o.OrderDate, o.ShippedDate, o.Freight
from Orders o
where o.EmployeeId in (@EmployeeIds)
