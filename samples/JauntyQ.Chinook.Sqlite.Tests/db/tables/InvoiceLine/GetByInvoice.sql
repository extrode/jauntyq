-- @each InvoiceIds
select il.InvoiceLineId, il.InvoiceId, il.TrackId, t.Name as TrackName,
       il.UnitPrice, il.Quantity
from InvoiceLine il
join Track t on t.TrackId = il.TrackId
where il.InvoiceId in (@InvoiceIds)
order by il.InvoiceId, il.InvoiceLineId
