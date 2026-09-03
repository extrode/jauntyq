select a.actor_id, a.first_name, a.last_name, count(fa.film_id) as FilmCount
from actor a
join film_actor fa on fa.actor_id = a.actor_id
group by a.actor_id, a.first_name, a.last_name
order by FilmCount desc, a.actor_id
limit 5
