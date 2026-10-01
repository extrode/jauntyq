-- @stream
select id, name, balance, photo
from customers
where balance > @MinBalance
