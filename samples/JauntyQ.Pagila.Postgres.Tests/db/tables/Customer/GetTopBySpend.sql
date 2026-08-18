-- @type TotalSpend numeric
-- @allow-unindexed canonical pagila ships no index on payment.customer_id; this sample keeps the upstream schema unmodified
select c.customer_id, c.first_name, c.last_name, round(sum(p.amount), 2) as TotalSpend
from customer c
join payment p on p.customer_id = c.customer_id
group by c.customer_id, c.first_name, c.last_name
order by TotalSpend desc, c.customer_id
limit 5
