-- @type revenue numeric
-- @allow-unindexed canonical sakila ships no index on inventory.film_id, rental.inventory_id, payment.rental_id; this sample keeps the upstream schema unmodified
select f.film_id, f.title, round(sum(p.amount), 2) as revenue
from film f
join film_category fc on fc.film_id = f.film_id
join inventory i on i.film_id = f.film_id
join rental r on r.inventory_id = i.inventory_id
join payment p on p.rental_id = r.rental_id
where fc.category_id = @CategoryId
group by f.film_id, f.title
order by revenue desc, f.title
limit 5
