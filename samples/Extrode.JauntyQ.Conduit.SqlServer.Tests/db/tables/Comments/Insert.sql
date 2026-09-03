-- @identity
insert into comments (article_id, author_id, body, created_at, updated_at)
values (@ArticleId, @AuthorId, @Body, @CreatedAt, @UpdatedAt)
