-- @each OrderIds
select od.OrderId, od.ProductId, p.ProductName, od.UnitPrice, od.Quantity, od.Discount
from [Order Details] od
join Products p on od.ProductId = p.ProductId
where od.OrderId in (@OrderIds)
