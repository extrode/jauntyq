-- @result CustomerSummary
select id, name, balance
from customers
where id = @Id
