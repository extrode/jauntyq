select r.rental_id, c.customer_id, c.first_name, c.last_name, r.rental_date, f.title
from rental r
join customer c on c.customer_id = r.customer_id
join inventory i on i.inventory_id = r.inventory_id
join film f on f.film_id = i.film_id
where r.return_date is null
order by r.rental_date
