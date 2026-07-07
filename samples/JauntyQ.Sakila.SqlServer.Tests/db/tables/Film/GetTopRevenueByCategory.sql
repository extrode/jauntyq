-- @type revenue numeric
select f.film_id, f.title, sum(p.amount) as revenue
from film f
join film_category fc on fc.film_id = f.film_id
join inventory i on i.film_id = f.film_id
join rental r on r.inventory_id = i.inventory_id
join payment p on p.rental_id = r.rental_id
where fc.category_id = @CategoryId
group by f.film_id, f.title
order by revenue desc, f.title
offset 0 rows fetch next 5 rows only
