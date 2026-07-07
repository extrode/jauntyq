-- @type total_spend numeric
select c.customer_id, c.first_name, c.last_name, round(sum(p.amount), 2) as total_spend
from customer c
join payment p on p.customer_id = c.customer_id
group by c.customer_id, c.first_name, c.last_name
order by total_spend desc, c.customer_id
limit 5
