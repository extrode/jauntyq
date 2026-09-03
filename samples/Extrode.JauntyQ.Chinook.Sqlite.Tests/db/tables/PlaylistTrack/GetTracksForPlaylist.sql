select pt.PlaylistId, pt.TrackId, t.Name as TrackName, t.Milliseconds
from PlaylistTrack pt
join Track t on t.TrackId = pt.TrackId
where pt.PlaylistId = @PlaylistId
order by pt.TrackId
