-- @each Ids
select id, name
from customers
where id in (@Ids)
