-- @type year int
-- @type month int
-- @type revenue numeric
select
    year(p.payment_date) as year,
    month(p.payment_date) as month,
    sum(p.amount) as revenue
from payment p
join staff s on s.staff_id = p.staff_id
where s.store_id = @StoreId
group by year(p.payment_date), month(p.payment_date)
order by 1, 2
