select id, article_id, author_id, body, created_at, updated_at
from comments
where article_id = @ArticleId
order by created_at asc, id asc
