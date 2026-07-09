-- @identity
insert into articles (slug, title, description, body, author_id, created_at, updated_at)
values (@Slug, @Title, @Description, @Body, @AuthorId, @CreatedAt, @UpdatedAt)
