select customer_id, first_name, last_name, email, uuid
from customer
where uuid = @Uuid
