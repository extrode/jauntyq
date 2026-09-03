select f.film_id, f.title, f.release_year, f.rental_rate
from film f
join film_actor fa on fa.film_id = f.film_id
where fa.actor_id = @ActorId
order by f.title
