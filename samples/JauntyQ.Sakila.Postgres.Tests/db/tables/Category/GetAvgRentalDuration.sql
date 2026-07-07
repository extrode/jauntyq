-- @type avg_days double precision
select cat.category_id, cat.name,
    avg(extract(epoch from (r.return_date - r.rental_date)) / 86400.0) as avg_days
from category cat
join film_category fc on fc.category_id = cat.category_id
join inventory i on i.film_id = fc.film_id
join rental r on r.inventory_id = i.inventory_id
where r.return_date is not null
group by cat.category_id, cat.name
order by cat.name
