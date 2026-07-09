namespace JauntyQ.Conduit.MySql.Tests.Domain;

public sealed record CommentView(int Id, string CreatedAt, string UpdatedAt, string Body, Profile Author);
