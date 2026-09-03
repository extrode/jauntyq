namespace Extrode.JauntyQ.SqlParser.IR;

/// <summary>
/// What one token inside a <see cref="PredicateAtom"/> is. The distinction that
/// matters is <see cref="Column"/> versus everything else: a column term is the
/// only one that has to be resolved against the schema before two atoms written
/// in different files can be compared, because the same column may be written
/// qualified in one and bare in the other.
/// </summary>
public enum AtomTermKind
{
    /// <summary>An identifier, split into its table qualifier (if any) and column name.</summary>
    Column,

    /// <summary>A <c>@name</c> parameter reference.</summary>
    Parameter,

    /// <summary>A string or numeric literal.</summary>
    Literal,

    /// <summary>A symbol: a comparison operator, a paren, a comma.</summary>
    Operator,

    /// <summary>A SQL keyword (IS, NULL, NOT, LIKE, IN, EXISTS, BETWEEN, ...).</summary>
    Keyword
}

/// <summary>One classified token of a predicate atom.</summary>
public struct AtomTerm
{
    public AtomTermKind Kind { get; set; }

    /// <summary>
    /// The term's text. For <see cref="AtomTermKind.Column"/> this is the bare
    /// column name with any qualifier moved to <see cref="TableAlias"/>.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// The table qualifier a <see cref="AtomTermKind.Column"/> term carried, or
    /// empty for an unqualified reference and for every other kind.
    /// </summary>
    public string TableAlias { get; set; }
}

/// <summary>
/// One top-level AND-conjunct of a WHERE clause, kept as classified tokens.
///
/// The parsed IR records only parameter bindings, literal bindings and lifted
/// subqueries, none of which sees an unparameterized predicate: nothing at all
/// is captured for <c>WHERE deleted_at IS NULL</c>. Atoms exist so two queries
/// declared to filter alike (<c>-- @mirrors</c>, JNT8011) can actually be
/// compared, including on the predicates the rest of the IR is blind to.
///
/// Deliberately shallow. An atom is a token run, not an expression tree: it can
/// say that two WHERE clauses differ and show where, and it cannot say that two
/// differently-written clauses mean the same thing.
/// </summary>
public sealed class PredicateAtom
{
    public List<AtomTerm> Terms { get; } = new();
}
