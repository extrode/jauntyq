-- @params BrandId:int?, TypeId:int?, Skip:int, Take:int
select id, catalog_type_id, catalog_brand_id, description, name, price, picture_uri
from catalog_item
where (@BrandId is null or catalog_brand_id = @BrandId)
  and (@TypeId is null or catalog_type_id = @TypeId)
order by id
offset @Skip rows fetch next @Take rows only
