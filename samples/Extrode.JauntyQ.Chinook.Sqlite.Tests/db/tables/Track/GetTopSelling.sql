-- @type UnitsSold int
select t.TrackId, t.Name, sum(il.Quantity) as UnitsSold
from Track t
join InvoiceLine il on il.TrackId = t.TrackId
group by t.TrackId, t.Name
order by UnitsSold desc, t.TrackId
limit 10
