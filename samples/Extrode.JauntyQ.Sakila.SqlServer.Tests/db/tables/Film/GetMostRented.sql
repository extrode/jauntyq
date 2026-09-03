-- @type rental_count int
-- @allow-unindexed canonical sakila ships no index on inventory.film_id, rental.inventory_id; this sample keeps the upstream schema unmodified
select f.film_id, f.title, count(r.rental_id) as rental_count
from film f
join inventory i on i.film_id = f.film_id
join rental r on r.inventory_id = i.inventory_id
group by f.film_id, f.title
order by rental_count desc, f.title
offset 0 rows fetch next 10 rows only
