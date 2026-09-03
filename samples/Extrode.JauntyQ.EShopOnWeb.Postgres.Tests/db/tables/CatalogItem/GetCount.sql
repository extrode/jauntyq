-- @params BrandId:int?, TypeId:int?
-- @first
select count(*) as total
from catalog_item
where (@BrandId is null or catalog_brand_id = @BrandId)
  and (@TypeId is null or catalog_type_id = @TypeId)
