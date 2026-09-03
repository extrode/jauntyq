select AlbumId, Title, ArtistId
from Album
where ArtistId = @ArtistId
order by AlbumId
