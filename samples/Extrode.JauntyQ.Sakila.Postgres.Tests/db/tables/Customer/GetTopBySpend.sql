-- @type total_spend numeric
-- @allow-unindexed canonical sakila ships no index on payment.customer_id; this sample keeps the upstream schema unmodified
select c.customer_id, c.first_name, c.last_name, sum(p.amount) as total_spend
from customer c
join payment p on p.customer_id = c.customer_id
group by c.customer_id, c.first_name, c.last_name
order by total_spend desc, c.customer_id
limit 5
