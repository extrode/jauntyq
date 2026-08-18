-- @each CustomerIds
-- @allow-unindexed canonical pagila ships no index on inventory.film_id, rental.customer_id; this sample keeps the upstream schema unmodified
select r.customer_id, r.rental_id, r.rental_date, r.return_date, f.title
from rental r
join inventory i on i.inventory_id = r.inventory_id
join film f on f.film_id = i.film_id
where r.customer_id in (@CustomerIds)
order by r.customer_id, r.rental_date, r.rental_id
