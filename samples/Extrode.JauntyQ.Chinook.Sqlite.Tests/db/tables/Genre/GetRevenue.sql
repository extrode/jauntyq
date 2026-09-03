-- @type Revenue numeric
select g.GenreId, g.Name, round(sum(il.UnitPrice * il.Quantity), 2) as Revenue
from Genre g
join Track t on t.GenreId = g.GenreId
join InvoiceLine il on il.TrackId = t.TrackId
group by g.GenreId, g.Name
order by Revenue desc, g.GenreId
limit 5
