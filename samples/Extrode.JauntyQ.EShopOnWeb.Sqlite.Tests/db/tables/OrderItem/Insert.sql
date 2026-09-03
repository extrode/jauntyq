-- @identity
insert into order_item (order_id, unit_price, units, ordered_catalog_item_id, ordered_product_name, ordered_picture_uri)
values (@OrderId, @UnitPrice, @Units, @OrderedCatalogItemId, @OrderedProductName, @OrderedPictureUri)
