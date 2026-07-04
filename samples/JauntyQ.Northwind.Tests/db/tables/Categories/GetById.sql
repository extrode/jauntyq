-- @first
select c.CategoryId, c.CategoryName, c.Description
from Categories c
where c.CategoryId = @CategoryId