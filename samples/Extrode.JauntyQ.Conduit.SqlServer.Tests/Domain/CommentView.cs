namespace Conduit.SqlServer.Tests.Domain;

public sealed record CommentView(int Id, string CreatedAt, string UpdatedAt, string Body, Profile Author);
