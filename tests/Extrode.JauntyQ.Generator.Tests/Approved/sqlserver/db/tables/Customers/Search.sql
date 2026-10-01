-- @params Term:string, Skip:int, Take:int
select id, name
from customers
where name like @Term
order by id
