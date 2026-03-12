select
    p.product_id,
    p.product_name,
    p.unit_price,
    c.category_name
from products p
join categories c on p.category_id = c.category_id
where p.category_id = @category_id
