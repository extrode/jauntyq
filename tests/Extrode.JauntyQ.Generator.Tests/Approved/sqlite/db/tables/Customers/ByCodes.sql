-- @each Codes
select id, code
from customers
where code in (@Codes) and name like @Pattern
