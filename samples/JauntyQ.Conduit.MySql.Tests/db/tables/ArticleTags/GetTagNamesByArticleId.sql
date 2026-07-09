select t.name
from article_tags atg
join tags t on t.id = atg.tag_id
where atg.article_id = @ArticleId
order by t.name
