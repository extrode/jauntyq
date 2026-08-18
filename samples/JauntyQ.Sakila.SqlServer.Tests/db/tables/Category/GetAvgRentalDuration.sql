-- @type avg_days double precision
-- @allow-unindexed canonical sakila ships no index on inventory.film_id, rental.inventory_id; this sample keeps the upstream schema unmodified
select cat.category_id, cat.name,
    avg(cast(datediff(second, r.rental_date, r.return_date) as float) / 86400.0) as avg_days
from category cat
join film_category fc on fc.category_id = cat.category_id
join inventory i on i.film_id = fc.film_id
join rental r on r.inventory_id = i.inventory_id
where r.return_date is not null
group by cat.category_id, cat.name
order by cat.name
