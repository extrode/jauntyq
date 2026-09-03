-- @first
select count(*) as total
from favorites
where user_id = @UserId and article_id = @ArticleId
