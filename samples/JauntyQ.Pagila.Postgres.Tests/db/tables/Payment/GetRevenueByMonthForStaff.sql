-- @type Year int
-- @type Month int
-- @type Revenue numeric
select extract(year from payment_date)::int as Year,
       extract(month from payment_date)::int as Month,
       round(sum(amount), 2) as Revenue
from payment
where staff_id = @StaffId
group by 1, 2
order by 1, 2
