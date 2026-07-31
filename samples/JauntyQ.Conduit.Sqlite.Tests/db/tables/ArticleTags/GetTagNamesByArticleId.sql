-- @each ArticleIds
select atg.article_id, t.name
from article_tags atg
join tags t on t.id = atg.tag_id
where atg.article_id in (@ArticleIds)
order by atg.article_id, t.name
