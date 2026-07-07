-- @first
select id, buyer_id, order_date, ship_to_street, ship_to_city, ship_to_state, ship_to_country, ship_to_zipcode
from orders
where id = @Id
