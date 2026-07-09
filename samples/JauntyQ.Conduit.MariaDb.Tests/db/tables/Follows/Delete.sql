delete from follows
where follower_id = @FollowerId and followed_id = @FollowedId
