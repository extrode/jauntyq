-- @first
select id, slug, title, description, body, author_id, created_at, updated_at
from articles
where slug = @Slug
