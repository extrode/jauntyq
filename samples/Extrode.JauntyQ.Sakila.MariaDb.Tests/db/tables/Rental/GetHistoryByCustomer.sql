-- @allow-n-plus-one single-customer point lookup (e.g. a customer-service screen); this sample calls it standalone, never per-row against Customer.GetNeverRented or any other collection query
select r.rental_id, f.title, r.rental_date, r.return_date
from rental r
join inventory i on i.inventory_id = r.inventory_id
join film f on f.film_id = i.film_id
where r.customer_id = @CustomerId
order by r.rental_date
