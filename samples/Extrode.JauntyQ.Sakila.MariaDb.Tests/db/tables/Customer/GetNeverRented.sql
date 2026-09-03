select c.customer_id, c.first_name, c.last_name
from customer c
where not exists (select 1 from rental r where r.customer_id = c.customer_id)
order by c.last_name, c.first_name
