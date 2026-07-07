-- @params Id:int, Username:string, Email:string, Bio:string, Image:string?
update users
set username = @Username,
    email = @Email,
    bio = @Bio,
    image = @Image
where id = @Id
