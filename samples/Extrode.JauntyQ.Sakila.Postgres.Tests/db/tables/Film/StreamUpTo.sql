-- @stream
select film_id, title
from film
where film_id < @Threshold
order by film_id
