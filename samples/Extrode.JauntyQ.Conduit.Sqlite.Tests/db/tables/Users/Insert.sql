-- @identity
insert into users (username, email, password_hash, bio, image)
values (@Username, @Email, @PasswordHash, @Bio, @Image)
