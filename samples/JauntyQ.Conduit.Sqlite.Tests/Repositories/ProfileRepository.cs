using JauntyQ.Generated;

namespace JauntyQ.Conduit.Sqlite.Tests.Repositories;

public sealed class ProfileRepository
{
    private readonly JauntyDb _db;

    public ProfileRepository(JauntyDb db) => _db = db;

    public Domain.Profile? GetProfile(string username, int? viewerId)
    {
        var user = _db.Users.GetByUsername(username);
        if (user is null) return null;

        bool following = viewerId is not null
            && _db.Follows.Exists(FollowerId: viewerId.Value, FollowedId: user.Id)?.Total > 0;

        return new Domain.Profile(user.Username, user.Bio, user.Image, following);
    }

    public void Follow(int followerId, int followedId)
        => _db.Follows.Insert(FollowerId: followerId, FollowedId: followedId);

    public void Unfollow(int followerId, int followedId)
        => _db.Follows.Delete(FollowerId: followerId, FollowedId: followedId);
}
