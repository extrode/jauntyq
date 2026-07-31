select ar.ArtistId, ar.Name, count(al.AlbumId) as AlbumCount
from Artist ar
join Album al on al.ArtistId = ar.ArtistId
group by ar.ArtistId, ar.Name
order by AlbumCount desc, ar.ArtistId
limit 5
