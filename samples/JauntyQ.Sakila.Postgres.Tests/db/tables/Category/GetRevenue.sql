-- @type revenue numeric
-- @allow-unindexed canonical sakila ships no index on inventory.film_id, rental.inventory_id, payment.rental_id; this sample keeps the upstream schema unmodified
select cat.category_id, cat.name, sum(p.amount) as revenue
from category cat
join film_category fc on fc.category_id = cat.category_id
join inventory i on i.film_id = fc.film_id
join rental r on r.inventory_id = i.inventory_id
join payment p on p.rental_id = r.rental_id
group by cat.category_id, cat.name
order by revenue desc, cat.name
