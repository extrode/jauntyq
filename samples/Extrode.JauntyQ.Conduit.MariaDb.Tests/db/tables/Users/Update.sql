-- @params Id:int, Username:string, Email:string, Bio:string, Image:string?, PasswordHash:string
update users
set username = @Username,
    email = @Email,
    bio = @Bio,
    image = @Image,
    password_hash = @PasswordHash
where id = @Id
