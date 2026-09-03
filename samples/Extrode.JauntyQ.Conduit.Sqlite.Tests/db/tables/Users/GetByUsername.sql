-- @first
select id, username, email, password_hash, bio, image
from users
where username = @Username
