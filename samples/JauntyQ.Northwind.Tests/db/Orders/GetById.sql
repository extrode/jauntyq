select o.OrderId, o.CustomerId, o.EmployeeId, o.OrderDate, o.RequiredDate, o.ShippedDate, o.ShipVia, o.Freight, o.ShipName, o.ShipAddress, o.ShipCity, o.ShipRegion, o.ShipPostalCode, o.ShipCountry
from Orders o
where o.OrderId = @orderId