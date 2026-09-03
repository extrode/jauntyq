-- @first
select count(*) as total
from follows
where follower_id = @FollowerId and followed_id = @FollowedId
