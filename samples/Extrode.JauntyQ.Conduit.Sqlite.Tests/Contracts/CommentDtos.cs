using Conduit.Sqlite.Tests.Domain;

namespace Conduit.Sqlite.Tests.Contracts;

/// <summary>
/// Domain.CommentView's field names already match the wire shape 1:1
/// ({"comment": {id, createdAt, updatedAt, body, author}}) -- just needs
/// wrapping.
/// </summary>
public sealed record CommentResponseEnvelope(CommentView Comment);

/// <summary>
/// Confirmed against the official RealWorld spec: bare {"comments": [...]},
/// no count field (unlike the articles list endpoint).
/// </summary>
public sealed record CommentsResponse(IReadOnlyList<CommentView> Comments);

public sealed record AddCommentRequest(string Body);
public sealed record AddCommentRequestEnvelope(AddCommentRequest Comment);
