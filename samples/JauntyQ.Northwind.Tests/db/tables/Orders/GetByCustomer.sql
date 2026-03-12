select o.OrderId, o.OrderDate, o.RequiredDate, o.ShippedDate, o.Freight, o.ShipName
from Orders o
where o.CustomerId = @CustomerId