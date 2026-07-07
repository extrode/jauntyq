-- @type total_amount numeric
select st.staff_id, st.first_name, st.last_name, count(p.payment_id) as payment_count, round(sum(p.amount), 2) as total_amount
from staff st
join payment p on p.staff_id = st.staff_id
group by st.staff_id, st.first_name, st.last_name
order by st.staff_id
