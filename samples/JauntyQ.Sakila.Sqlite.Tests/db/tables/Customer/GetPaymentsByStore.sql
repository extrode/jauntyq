-- @type total_paid numeric
select c.customer_id, c.first_name, c.last_name, round(sum(p.amount), 2) as total_paid
from customer c
join payment p on p.customer_id = c.customer_id
where c.store_id = @StoreId
group by c.customer_id, c.first_name, c.last_name
order by c.last_name, c.first_name
