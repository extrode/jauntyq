using System;
using System.Security.Cryptography;
using System.Text;
using JauntyQ.Generated;

namespace JauntyQ.Conduit.Postgres.Tests.Repositories;

public sealed class UserRepository
{
    private readonly JauntyDb _db;

    public UserRepository(JauntyDb db) => _db = db;

    public int Register(string username, string email, string password, string bio = "", string? image = null)
    {
        string hash = HashPassword(password);
        return _db.Users.Insert(Username: username, Email: email, PasswordHash: hash, Bio: bio, Image: image);
    }

    public User? Login(string email, string password)
    {
        var user = _db.Users.GetByEmail(email);
        if (user is null || user.PasswordHash != HashPassword(password)) return null;
        return user;
    }

    public User? GetById(int id) => _db.Users.GetById(id);

    public User? GetByUsername(string username) => _db.Users.GetByUsername(username);

    public User? GetByEmail(string email) => _db.Users.GetByEmail(email);

    public void UpdateProfile(int id, string username, string email, string bio, string? image, string? passwordHash = null)
    {
        string finalHash = passwordHash ?? _db.Users.GetById(id)!.PasswordHash;
        _db.Users.Update(Id: id, Username: username, Email: email, Bio: bio, Image: image, PasswordHash: finalHash);
    }

    public static string HashPassword(string password)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
