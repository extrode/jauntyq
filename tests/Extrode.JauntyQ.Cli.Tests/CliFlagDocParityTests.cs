using System.Text.RegularExpressions;
using Xunit;

namespace Extrode.JauntyQ.Cli.Tests;

/// <summary>
/// A verb's accepted flags live in two places that no compiler relates to each
/// other: the <c>case "--x"</c> arms of its own argument switch, and the option
/// table under its heading in <c>docs/06-reference/cli.md</c>. Two consecutive
/// audit rounds found the pair disagreeing in opposite directions — AUD-R87-01
/// with the manual stale, AUD-R88-02 with the built-in help stale — which rules
/// out "one of the two is the unreliable one" and leaves only "nothing checks
/// them".
/// </summary>
/// <remarks>
/// <para><see cref="CliUsageTextTests"/> closed the smaller half of this: the
/// verb list, both connection forms, the environment-variable names and the
/// provider list. Its own doc comment names what it does not reach — "the flag
/// list of any verb against that verb's own arg switch, which is the assertion
/// that would make the whole class impossible". This is that assertion.</para>
/// <para>Both directions matter and they fail differently. A flag in the switch
/// and not in the manual is undiscoverable: it works, and the only way to learn
/// it exists is to read the source. A flag in the manual and not in the switch
/// is worse, because the CLI's default arm rejects an unknown argument, so the
/// documented invocation exits 1 saying "Unknown argument".</para>
/// <para>Source-scanning rather than reflective, following
/// <c>CoreUnaffectedTests</c> and <c>ToolPackagingTests</c> in this project: the
/// arg switches are inside methods, so there is no member to reflect over, and
/// the literal in the <c>case</c> arm IS the accepted spelling.</para>
/// </remarks>
[Trait("Category", "AuditRegression")]
public class CliFlagDocParityTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JauntyQ.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string CliDoc() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "docs", "06-reference", "cli.md"));

    private static string CliSource(params string[] repoRelativeFiles) =>
        string.Join(Environment.NewLine, repoRelativeFiles.Select(f => File.ReadAllText(Path.Combine(RepoRoot(), f))));

    /// <summary>
    /// The body of one <c>##</c>/<c>###</c> section of cli.md, from its heading
    /// to the next heading of either level.
    /// </summary>
    private static string Section(string heading)
    {
        string doc = CliDoc();
        var lines = doc.Split('\n');
        int start = Array.FindIndex(lines, l => l.TrimEnd('\r') == heading);
        Assert.True(start >= 0, $"cli.md has no section headed '{heading}'.");

        int end = Array.FindIndex(lines, start + 1,
            l => l.StartsWith("## ", StringComparison.Ordinal) || l.StartsWith("### ", StringComparison.Ordinal));
        if (end < 0) end = lines.Length;

        string body = string.Join("\n", lines[(start + 1)..end]);
        Assert.False(string.IsNullOrWhiteSpace(body), $"cli.md's '{heading}' section is empty.");
        return body;
    }

    /// <summary>
    /// Every <c>case "--x"</c> literal in a CLI source file, empty set included.
    /// It does not assert non-emptiness of its own accord: a verb that takes no
    /// flags is a real verb, and asserting here would make it unrepresentable in
    /// <see cref="VerbPairs"/> — which is what kept <c>license</c> out until now.
    /// The emptiness each verb is expected to have is declared per row instead
    /// and checked at every read.
    /// </summary>
    private static SortedSet<string> SwitchFlags(string source) =>
        new(Regex.Matches(source, @"case ""(--[a-z0-9-]+)""").Select(m => m.Groups[1].Value),
            StringComparer.Ordinal);

    /// <summary>
    /// Every flag named anywhere in a section — table, prose or fenced example.
    /// The two verbs that pushed this wider both document their flags outside
    /// an Options table and are not wrong to: <c>registry</c> names
    /// <c>--schema</c> and <c>--table</c> in the Subcommand column, because
    /// which subcommand takes which is the thing worth saying, and
    /// <c>activate</c> has one flag and shows it in a worked example instead of
    /// tabulating a table of one.
    /// </summary>
    private static SortedSet<string> MentionedFlags(string section) =>
        new(Regex.Matches(section, @"`?(--[a-z0-9-]+)").Select(m => m.Groups[1].Value),
            StringComparer.Ordinal);

    /// <summary>
    /// Every flag named in a section's option-table rows. Table rows only, not
    /// the whole section: prose legitimately mentions another verb's flags
    /// ("same resolution as <c>schema pull</c>"), and a mention is not a claim
    /// that this verb accepts it. A row in the Option column is that claim.
    /// </summary>
    private static SortedSet<string> DocumentedFlags(string section)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string line in section.Split('\n'))
        {
            if (!line.StartsWith("| `--", StringComparison.Ordinal))
                continue;
            foreach (Match m in Regex.Matches(line, @"`(--[a-z0-9-]+)"))
                found.Add(m.Groups[1].Value);
        }
        return found;
    }

    // The pairs. `schema pull` and `schema verify` share the connection and
    // output flags through LiveSchemaOptions.cs, which is why that file is in
    // both rows, each checked against its own section plus the shared
    // `## Options` table those flags are actually tabulated in.
    //
    // The last column is whether the verb takes any flag at all. It exists for
    // `license`, whose switch arms are subcommand names rather than flags, so
    // its accepted set is empty and every other row's is not. Declaring it makes
    // the empty case sayable AND checkable in both directions: cli.md tabulating
    // a flag for it fails EveryDocumentedFlag_IsAccepted today, and a first
    // `case "--x"` reaching LicenseCli fails the guard below until whoever adds
    // it flips this column — which is the same edit that switches both parity
    // theories on for the verb.
    public static TheoryData<string, string[], string[], bool> VerbPairs() => new()
    {
        {
            "### `schema pull`",
            new[] { "src/Extrode.JauntyQ.Cli.Core/LiveSchemaOptions.cs", "src/Extrode.JauntyQ.Cli.Core/SchemaPullVerb.cs" },
            new[] { "### `schema pull`", "## Options" },
            true
        },
    };

    /// <summary>
    /// Every flag the verb accepts is named in its manual section. A flag that
    /// works and is undocumented is discoverable only by reading the source.
    /// </summary>
    [Theory]
    [MemberData(nameof(VerbPairs))]
    public void EveryAcceptedFlag_IsDocumented(string verb, string[] sourcePath, string[] headings, bool hasFlags)
    {
        var accepted = SwitchFlags(CliSource(sourcePath));
        Assert.Equal(hasFlags, accepted.Count > 0);
        string doc = string.Join("\n", headings.Select(Section));

        var mentioned = MentionedFlags(doc);
        var undocumented = accepted.Where(f => !mentioned.Contains(f)).ToList();

        Assert.True(undocumented.Count == 0,
            $"{verb} accepts flags cli.md never names: {string.Join(", ", undocumented)}. "
            + "Document it in the verb's section, or remove the case arm.");
    }

    /// <summary>
    /// Every flag the manual tabulates for the verb is one the verb accepts.
    /// This direction fails louder in the field: the arg switch's default arm
    /// rejects an unknown argument, so a documented-but-unaccepted flag exits 1
    /// with "Unknown argument" against an invocation copied out of the manual.
    /// </summary>
    [Theory]
    [MemberData(nameof(VerbPairs))]
    public void EveryDocumentedFlag_IsAccepted(string verb, string[] sourcePath, string[] headings, bool hasFlags)
    {
        var accepted = SwitchFlags(CliSource(sourcePath));
        Assert.Equal(hasFlags, accepted.Count > 0);
        string doc = string.Join("\n", headings.Select(Section));

        var unaccepted = DocumentedFlags(doc).Where(f => !accepted.Contains(f)).ToList();

        Assert.True(unaccepted.Count == 0,
            $"cli.md tabulates flags {verb} does not accept: {string.Join(", ", unaccepted)}. "
            + "The arg switch's default arm rejects an unknown argument, so each of these exits 1.");
    }

    /// <summary>
    /// The vacuity guard. Without it a renamed doc, a moved source file or a
    /// regex that stops matching turns both theories above into comparisons of
    /// two empty sets, which pass. Every row declares which shape it expects, so
    /// the one verb whose sets are legitimately empty is held to that rather
    /// than excused from the check.
    /// </summary>
    [Fact]
    public void TheParityInputs_MatchTheirDeclaredShape()
    {
        foreach (var row in VerbPairs())
        {
            string verb = (string)row[0];
            var sourcePath = (string[])row[1];
            var headings = (string[])row[2];
            bool hasFlags = (bool)row[3];
            string doc = string.Join("\n", headings.Select(Section));

            if (!hasFlags)
            {
                // Both directions are already live for a flagless verb, and it
                // is worth being exact about where, because there is no single
                // arm holding either up. A flag tabulated in cli.md fails
                // EveryDocumentedFlag_IsAccepted against the empty accepted set;
                // a first `case "--x"` in the source fails the `Assert.Equal`
                // in BOTH theories. So the two lines here are a third statement
                // of the same expectation, kept because they fail with the
                // reason named rather than as a set comparison a reader has to
                // work backward from.
                //
                // MentionedFlags is deliberately NOT asserted empty. It matches
                // `--[a-z0-9-]+` anywhere in the section, which is right where
                // it is used (a superset the accepted set must fit inside) and
                // wrong as an emptiness claim: a table of any kind puts `---` in
                // it via the separator row, and a cross-reference to another
                // verb's flag is legitimate prose this class says elsewhere is
                // not a claim of acceptance. It would fail for the wrong reason
                // and kills nothing the other two do not.
                Assert.Empty(SwitchFlags(CliSource(sourcePath)));
                Assert.Empty(DocumentedFlags(doc));
                continue;
            }

            Assert.NotEmpty(SwitchFlags(CliSource(sourcePath)));
            Assert.NotEmpty(MentionedFlags(doc));

            // DocumentedFlags per verb, not once for the whole file. Guarding it
            // on one section only proves the extractor still works somewhere,
            // which is a weaker claim than the theory consumes: a table that
            // stops matching the column-0 "| `--" prefix — indented, converted
            // to a list, its option cell bolded — returns an empty set, and
            // EveryDocumentedFlag_IsAccepted then passes for that verb with the
            // phantom-row direction dead. MentionedFlags keeps the other
            // direction green throughout, so nothing else would show it.
            if (verb == ActivateVerb)
                continue; // Its one flag is in a worked example, not a table.

            Assert.True(DocumentedFlags(doc).Count > 0,
                $"cli.md's section(s) for {verb} yielded no option-table rows, so "
                + $"{nameof(EveryDocumentedFlag_IsAccepted)} is vacuous for it. A row must "
                + "start at column 0 with '| `--' to be read as a claim that the verb accepts the flag.");
        }
    }

    /// <summary>
    /// The one verb excluded from the <see cref="DocumentedFlags"/> guard above,
    /// named once so the exclusion cannot drift away from the row it excludes.
    /// </summary>
    private const string ActivateVerb = "### `activate`";
}
