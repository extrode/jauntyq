-- @type film_count int
-- @allow-unindexed canonical sakila ships no index on film.language_id; this sample keeps the upstream schema unmodified
select l.language_id, l.name, count(f.film_id) as film_count
from language l
left join film f on f.language_id = l.language_id
group by l.language_id, l.name
order by l.name
