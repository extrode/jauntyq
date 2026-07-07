-- @identity
insert into basket_item (basket_id, catalog_item_id, unit_price, quantity)
values (@BasketId, @CatalogItemId, @UnitPrice, @Quantity)
