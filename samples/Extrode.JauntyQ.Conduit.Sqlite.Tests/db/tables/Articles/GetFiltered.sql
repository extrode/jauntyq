-- @params Tag:string?, Author:string?, FavoritedBy:string?, Skip:int, Take:int
select a.id, a.slug, a.title, a.description, a.body, a.author_id, a.created_at, a.updated_at
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
order by a.created_at desc, a.id desc
limit @Take offset @Skip
