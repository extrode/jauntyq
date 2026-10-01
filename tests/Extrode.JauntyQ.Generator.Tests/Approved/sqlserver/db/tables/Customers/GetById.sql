-- @first
select id, code, name, email, balance, photo, is_active, created_at, birthday, rating, visits
from customers
where id = @Id
