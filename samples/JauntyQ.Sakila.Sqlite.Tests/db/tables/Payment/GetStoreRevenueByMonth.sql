-- @type year int
-- @type month int
-- @type revenue numeric
select
    cast(strftime('%Y', p.payment_date) as integer) as year,
    cast(strftime('%m', p.payment_date) as integer) as month,
    round(sum(p.amount), 2) as revenue
from payment p
join staff s on s.staff_id = p.staff_id
where s.store_id = @StoreId
group by 1, 2
order by 1, 2
