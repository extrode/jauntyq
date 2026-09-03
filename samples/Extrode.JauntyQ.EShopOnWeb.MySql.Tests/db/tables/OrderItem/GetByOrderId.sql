-- @each OrderIds
select id, order_id, unit_price, units, ordered_catalog_item_id, ordered_product_name, ordered_picture_uri
from order_item
where order_id in (@OrderIds)
order by order_id, id
