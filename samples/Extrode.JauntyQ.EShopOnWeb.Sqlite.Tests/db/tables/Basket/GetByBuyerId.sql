-- @first
select id, buyer_id
from basket
where buyer_id = @BuyerId
