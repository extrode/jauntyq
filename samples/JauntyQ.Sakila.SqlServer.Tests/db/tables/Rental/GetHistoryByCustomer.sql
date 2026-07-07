select r.rental_id, f.title, r.rental_date, r.return_date
from rental r
join inventory i on i.inventory_id = r.inventory_id
join film f on f.film_id = i.film_id
where r.customer_id = @CustomerId
order by r.rental_date
