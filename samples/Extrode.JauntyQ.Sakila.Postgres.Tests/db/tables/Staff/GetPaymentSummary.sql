-- @type total_amount numeric
-- @allow-unindexed canonical sakila ships no index on payment.staff_id; this sample keeps the upstream schema unmodified
select st.staff_id, st.first_name, st.last_name, count(p.payment_id) as payment_count, sum(p.amount) as total_amount
from staff st
join payment p on p.staff_id = st.staff_id
group by st.staff_id, st.first_name, st.last_name
order by st.staff_id
