select p.PlaylistId, p.Name, count(pt.TrackId) as TrackCount
from Playlist p
left join PlaylistTrack pt on pt.PlaylistId = p.PlaylistId
group by p.PlaylistId, p.Name
order by TrackCount desc, p.PlaylistId
