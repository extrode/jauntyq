-- @each AlbumIds
select t.AlbumId, t.TrackId, t.Name, t.Composer, t.Milliseconds, t.UnitPrice,
       g.Name as GenreName, mt.Name as MediaTypeName
from Track t
left join Genre g on g.GenreId = t.GenreId
join MediaType mt on mt.MediaTypeId = t.MediaTypeId
where t.AlbumId in (@AlbumIds)
order by t.AlbumId, t.TrackId
