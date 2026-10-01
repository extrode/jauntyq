-- @type total decimal
select customer_id, sum(total) as total
from orders
group by customer_id
