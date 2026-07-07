select id, basket_id, catalog_item_id, unit_price, quantity
from basket_item
where basket_id = @BasketId
order by id
