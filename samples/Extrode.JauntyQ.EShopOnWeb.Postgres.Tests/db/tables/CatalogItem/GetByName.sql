-- @first
select id, catalog_type_id, catalog_brand_id, description, name, price, picture_uri
from catalog_item
where name = @Name
