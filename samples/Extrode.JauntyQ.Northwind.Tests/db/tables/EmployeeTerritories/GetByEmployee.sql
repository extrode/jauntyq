-- @each EmployeeIds
select et.EmployeeId, et.TerritoryId, t.Description as TerritoryDescription
from EmployeeTerritories et
join Territories t on et.TerritoryId = t.TerritoryId
where et.EmployeeId in (@EmployeeIds)
