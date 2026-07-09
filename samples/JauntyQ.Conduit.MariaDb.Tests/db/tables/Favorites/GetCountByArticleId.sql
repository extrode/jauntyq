-- @first
select count(*) as total
from favorites
where article_id = @ArticleId
