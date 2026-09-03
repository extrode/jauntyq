-- @params UserId:int
-- @first
select count(*) as total
from articles a
where a.author_id in (select followed_id from follows where follower_id = @UserId)
