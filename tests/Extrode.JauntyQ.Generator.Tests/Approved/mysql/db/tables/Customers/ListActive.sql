select id, name, email
from customers
where is_active = @IsActive
order by name
