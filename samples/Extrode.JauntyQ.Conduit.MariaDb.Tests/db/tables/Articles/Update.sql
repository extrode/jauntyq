-- @params Id:int, Title:string, Description:string, Body:string, UpdatedAt:string
update articles
set title = @Title,
    description = @Description,
    body = @Body,
    updated_at = @UpdatedAt
where id = @Id
