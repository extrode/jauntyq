-- @params Tag:string?, Author:string?, FavoritedBy:string?
-- @first
select count(*) as total
from articles a
where (@Tag is null or exists (
        select 1
        from article_tags atg
        join tags t on t.id = atg.tag_id
        where atg.article_id = a.id and t.name = @Tag
      ))
  and (@Author is null or exists (
        select 1
        from users u
        where u.id = a.author_id and u.username = @Author
      ))
  and (@FavoritedBy is null or exists (
        select 1
        from favorites f
        join users u2 on u2.id = f.user_id
        where f.article_id = a.id and u2.username = @FavoritedBy
      ))
