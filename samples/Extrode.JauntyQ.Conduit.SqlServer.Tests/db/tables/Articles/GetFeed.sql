-- @params UserId:int, Skip:int, Take:int
select a.id, a.slug, a.title, a.description, a.body, a.author_id, a.created_at, a.updated_at
from articles a
where a.author_id in (select followed_id from follows where follower_id = @UserId)
order by a.created_at desc, a.id desc
offset @Skip rows fetch next @Take rows only
