-- @type year int
-- @type month int
-- @type revenue numeric
select
    extract(year from p.payment_date) as year,
    extract(month from p.payment_date) as month,
    sum(p.amount) as revenue
from payment p
join staff s on s.staff_id = p.staff_id
where s.store_id = @StoreId
group by 1, 2
order by 1, 2
