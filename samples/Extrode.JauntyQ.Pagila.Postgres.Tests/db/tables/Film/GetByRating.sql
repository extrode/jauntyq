-- @allow-unindexed canonical pagila ships no index on film.rating; this sample keeps the upstream schema unmodified
select film_id, title, release_year, rating, length
from film
where rating = @Rating
order by film_id
