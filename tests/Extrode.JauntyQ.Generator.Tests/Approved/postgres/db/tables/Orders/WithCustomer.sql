select o.id, o.total, o.note, c.name as customer_name
from orders o
join customers c on c.id = o.customer_id
where o.customer_id = @CustomerId
