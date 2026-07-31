-- @each ArticleIds
select article_id, count(*) as total
from favorites
where article_id in (@ArticleIds)
group by article_id
