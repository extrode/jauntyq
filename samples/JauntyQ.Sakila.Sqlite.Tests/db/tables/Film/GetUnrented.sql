-- @allow-unindexed canonical sakila ships no index on rental.inventory_id, inventory.film_id; this sample keeps the upstream schema unmodified
select f.film_id, f.title
from film f
where not exists (
    select 1
    from inventory i
    join rental r on r.inventory_id = i.inventory_id
    where i.film_id = f.film_id
)
order by f.title
