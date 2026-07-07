select f.film_id, f.title, count(r.rental_id) as rental_count
from film f
join inventory i on i.film_id = f.film_id
join rental r on r.inventory_id = i.inventory_id
group by f.film_id, f.title
order by rental_count desc, f.title
limit 10
