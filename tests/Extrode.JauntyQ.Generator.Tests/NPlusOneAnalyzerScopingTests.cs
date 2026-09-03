using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

[Trait("Category", "AuditRegression")]
public class NPlusOneAnalyzerScopingTests
{
    private const string RealmsTable = @"
    ""realms"": {
      ""name"": ""realms"",
      ""columns"": {
        ""realm_id"": { ""name"": ""realm_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""name"": { ""name"": ""name"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 60 }
      },
      ""indexes"": []
    }";

    private static string Schema(string tables, string foreignKeys) => @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {" + tables + @"
  },
  ""foreignKeys"": [" + foreignKeys + @"]
}";

    private const string RealmToMembershipFk =
        @"{ ""fromTable"": ""memberships"", ""fromColumn"": ""realm_id"", ""toTable"": ""realms"", ""toColumn"": ""realm_id"" }";

    private const string RealmsGetAll = "select realm_id, name\nfrom realms";

    private static GeneratorDriverRunResult Run(string schemaJson, params (string Path, string Text)[] files)
    {
        var compilation = CSharpCompilation.Create("NPlusOneScopingTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = new List<AdditionalText> { new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson) };
        foreach (var (path, text) in files)
            texts.Add(new InMemoryAdditionalText(path, text));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts.ToImmutableArray())
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        return driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _).GetRunResult();
    }

    private static bool Fired(GeneratorDriverRunResult result) =>
        result.Diagnostics.Any(d => d.Id == "JNT8008");

    // ── scoping FK alongside a more selective unique key ──────────────────

    private const string MembershipsKeyedByRealmAndUsername = @",
    ""memberships"": {
      ""name"": ""memberships"",
      ""columns"": {
        ""realm_id"": { ""name"": ""realm_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""username"": { ""name"": ""username"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 40, ""isPrimaryKey"": true },
        ""email"": { ""name"": ""email"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 80 }
      },
      ""indexes"": []
    }";

    [Fact]
    public void ChildPinnedByAPrimaryKeyReachingBeyondTheForeignKey_DoesNotFire()
    {
        var result = Run(
            Schema(RealmsTable + MembershipsKeyedByRealmAndUsername, RealmToMembershipFk),
            ("db/Realms/GetAll.sql", RealmsGetAll),
            ("db/Memberships/GetByUsername.sql",
                "select email\nfrom memberships\nwhere memberships.realm_id = @realm_id and memberships.username = @username"));

        Assert.False(Fired(result));
    }

    [Fact]
    public void ChildFilteredOnlyByTheForeignKeyHalfOfThatKey_StillFires()
    {
        var result = Run(
            Schema(RealmsTable + MembershipsKeyedByRealmAndUsername, RealmToMembershipFk),
            ("db/Realms/GetAll.sql", RealmsGetAll),
            ("db/Memberships/GetByRealm.sql",
                "select email\nfrom memberships\nwhere memberships.realm_id = @realm_id"));

        Assert.True(Fired(result));
    }

    private static string Members(string indexes) => @",
    ""members"": {
      ""name"": ""members"",
      ""columns"": {
        ""member_id"": { ""name"": ""member_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""realm_id"": { ""name"": ""realm_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""email"": { ""name"": ""email"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 80 }
      },
      ""indexes"": [" + indexes + @"]
    }";

    private const string PlainUniqueMemberEmail =
        @"{ ""name"": ""ux_members_email"", ""columns"": [""realm_id"", ""email""], ""isUnique"": true }";

    private const string ExpressionUniqueMemberEmail =
        @"{ ""name"": ""ux_members_lower_email"", ""columns"": [""realm_id"", ""email""], ""isUnique"": true, ""hasExpressionKeyPart"": true }";

    private const string MembersGetByEmail =
        "select member_id\nfrom members\nwhere members.realm_id = @realm_id and members.email = @email";

    private const string RealmToMemberFk =
        @"{ ""fromTable"": ""members"", ""fromColumn"": ""realm_id"", ""toTable"": ""realms"", ""toColumn"": ""realm_id"" }";

    [Fact]
    public void ChildPinnedByAUniqueIndexReachingBeyondTheForeignKey_DoesNotFire()
    {
        var result = Run(
            Schema(RealmsTable + Members(PlainUniqueMemberEmail), RealmToMemberFk),
            ("db/Realms/GetAll.sql", RealmsGetAll),
            ("db/Members/GetByEmail.sql", MembersGetByEmail));

        Assert.False(Fired(result));
    }

    [Fact]
    public void ChildWhoseOnlyUniqueIndexIsExpressionKeyed_StillFires()
    {
        var result = Run(
            Schema(RealmsTable + Members(ExpressionUniqueMemberEmail), RealmToMemberFk),
            ("db/Realms/GetAll.sql", RealmsGetAll),
            ("db/Members/GetByEmail.sql", MembersGetByEmail));

        Assert.True(Fired(result));
    }

    [Fact]
    public void ChildFilteredOnlyByTheForeignKeyPartOfItsUniqueIndex_StillFires()
    {
        var result = Run(
            Schema(RealmsTable + Members(PlainUniqueMemberEmail), RealmToMemberFk),
            ("db/Realms/GetAll.sql", RealmsGetAll),
            ("db/Members/GetByRealm.sql",
                "select member_id\nfrom members\nwhere members.realm_id = @realm_id"));

        Assert.True(Fired(result));
    }

    // ── parent narrowed to one row by a unique index rather than its PK ───

    private static string RealmsWithSlug(string indexes) => @"
    ""realms"": {
      ""name"": ""realms"",
      ""columns"": {
        ""realm_id"": { ""name"": ""realm_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""slug"": { ""name"": ""slug"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 40 },
        ""name"": { ""name"": ""name"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 60 }
      },
      ""indexes"": [" + indexes + @"]
    }";

    private const string PlainUniqueSlug =
        @"{ ""name"": ""ux_realms_slug"", ""columns"": [""slug""], ""isUnique"": true }";

    private const string ExpressionUniqueSlug =
        @"{ ""name"": ""ux_realms_lower_slug"", ""columns"": [""slug""], ""isUnique"": true, ""hasExpressionKeyPart"": true }";

    private const string RealmsGetBySlug = "select realm_id, name\nfrom realms\nwhere realms.slug = @slug";

    private const string MembershipsGetByRealm =
        "select email\nfrom memberships\nwhere memberships.realm_id = @realm_id";

    private const string PlainMemberships = @",
    ""memberships"": {
      ""name"": ""memberships"",
      ""columns"": {
        ""membership_id"": { ""name"": ""membership_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""realm_id"": { ""name"": ""realm_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""email"": { ""name"": ""email"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 80 }
      },
      ""indexes"": []
    }";

    [Fact]
    public void ParentPinnedToOneRowByAUniqueIndex_IsNotACollection()
    {
        var result = Run(
            Schema(RealmsWithSlug(PlainUniqueSlug) + PlainMemberships, RealmToMembershipFk),
            ("db/Realms/GetBySlug.sql", RealmsGetBySlug),
            ("db/Memberships/GetByRealm.sql", MembershipsGetByRealm));

        Assert.False(Fired(result));
    }

    [Fact]
    public void ParentWhoseOnlyUniqueIndexIsExpressionKeyed_IsStillACollection()
    {
        var result = Run(
            Schema(RealmsWithSlug(ExpressionUniqueSlug) + PlainMemberships, RealmToMembershipFk),
            ("db/Realms/GetBySlug.sql", RealmsGetBySlug),
            ("db/Memberships/GetByRealm.sql", MembershipsGetByRealm));

        Assert.True(Fired(result));
    }

    [Fact]
    public void ParentReadingTheSameUniqueColumnWithoutPinningIt_IsStillACollection()
    {
        var result = Run(
            Schema(RealmsWithSlug(PlainUniqueSlug) + PlainMemberships, RealmToMembershipFk),
            ("db/Realms/GetAll.sql", "select realm_id, slug, name\nfrom realms"),
            ("db/Memberships/GetByRealm.sql", MembershipsGetByRealm));

        Assert.True(Fired(result));
    }

    // ── a child pinned to two distinct parents at once ────────────────────

    private const string TwoParentTables = @"
    ""users"": {
      ""name"": ""users"",
      ""columns"": {
        ""user_id"": { ""name"": ""user_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""display_name"": { ""name"": ""display_name"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 60 }
      },
      ""indexes"": []
    },
    ""applications"": {
      ""name"": ""applications"",
      ""columns"": {
        ""application_id"": { ""name"": ""application_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""owner_id"": { ""name"": ""owner_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""label"": { ""name"": ""label"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 60 }
      },
      ""indexes"": []
    },
    ""sessions"": {
      ""name"": ""sessions"",
      ""columns"": {
        ""session_id"": { ""name"": ""session_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""user_id"": { ""name"": ""user_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""application_id"": { ""name"": ""application_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""started_at"": { ""name"": ""started_at"", ""dbType"": ""datetime"", ""isNullable"": false }
      },
      ""indexes"": []
    }";

    private const string SessionFks =
        @"{ ""fromTable"": ""sessions"", ""fromColumn"": ""user_id"", ""toTable"": ""users"", ""toColumn"": ""user_id"" },
          { ""fromTable"": ""sessions"", ""fromColumn"": ""application_id"", ""toTable"": ""applications"", ""toColumn"": ""application_id"" },
          { ""fromTable"": ""applications"", ""fromColumn"": ""owner_id"", ""toTable"": ""users"", ""toColumn"": ""user_id"" }";

    [Fact]
    public void ChildPinningFullForeignKeysToTwoParents_DoesNotFireForEither()
    {
        var result = Run(
            Schema(TwoParentTables, SessionFks),
            ("db/Users/GetAll.sql", "select user_id, display_name\nfrom users"),
            ("db/Applications/GetAll.sql", "select application_id, label\nfrom applications"),
            ("db/Sessions/GetScoped.sql",
                "select session_id, started_at\nfrom sessions\nwhere sessions.user_id = @user_id and sessions.application_id = @application_id"));

        Assert.False(Fired(result));
    }

    [Fact]
    public void ChildPinningOnlyOneOfTheTwoForeignKeys_FiresForThatParent()
    {
        var result = Run(
            Schema(TwoParentTables, SessionFks),
            ("db/Users/GetAll.sql", "select user_id, display_name\nfrom users"),
            ("db/Applications/GetAll.sql", "select application_id, label\nfrom applications"),
            ("db/Sessions/GetByUser.sql",
                "select session_id, started_at\nfrom sessions\nwhere sessions.user_id = @user_id"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8008");
        Assert.Contains("users", diag.GetMessage());
    }

    // ── the FK graph itself ───────────────────────────────────────────────

    [Fact]
    public void ADuplicateForeignKeyRow_IsCollapsedRatherThanDemandingTheColumnTwice()
    {
        var result = Run(
            Schema(RealmsTable + PlainMemberships,
                RealmToMembershipFk + "," + RealmToMembershipFk),
            ("db/Realms/GetAll.sql", RealmsGetAll),
            ("db/Memberships/GetByRealm.sql",
                "select email\nfrom memberships\nwhere memberships.realm_id = @realm_id"));

        Assert.True(Fired(result));
    }

    [Fact]
    public void ForeignKeyRowsMissingTheirEndpoints_LeaveThePassInert()
    {
        var result = Run(
            Schema(RealmsTable + PlainMemberships,
                @"{ ""fromTable"": """", ""fromColumn"": ""realm_id"", ""toTable"": ""realms"", ""toColumn"": ""realm_id"" }"),
            ("db/Realms/GetAll.sql", RealmsGetAll),
            ("db/Memberships/GetByRealm.sql",
                "select email\nfrom memberships\nwhere memberships.realm_id = @realm_id"));

        Assert.False(Fired(result));
    }

    // ── child-side shapes ─────────────────────────────────────────────────

    [Fact]
    public void ChildFilteringTheForeignKeyByBothEqualityAndAnInList_DoesNotFire()
    {
        var result = Run(
            Schema(RealmsTable + PlainMemberships, RealmToMembershipFk),
            ("db/Realms/GetAll.sql", RealmsGetAll),
            ("db/Memberships/GetByRealmOrList.sql",
                "select email\nfrom memberships\nwhere memberships.realm_id = @realm_id and memberships.realm_id in (@realm_ids)"));

        Assert.False(Fired(result));
    }

    [Fact]
    public void ChildComputingAnAggregateOverABareColumn_FiresWithTheGroupedJoinSuggestion()
    {
        var result = Run(
            Schema(RealmsTable + PlainMemberships, RealmToMembershipFk),
            ("db/Realms/GetAll.sql", RealmsGetAll),
            ("db/Memberships/CountByRealm.sql",
                "select sum(membership_id) as total\nfrom memberships\nwhere memberships.realm_id = @realm_id\n-- @type total int"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8008");
        Assert.Contains("GROUP BY", diag.GetMessage());
    }

    [Fact]
    public void ParentWhoseParameterCannotBeAttributedToAColumn_IsStillACollection()
    {
        var result = Run(
            Schema(RealmsTable + PlainMemberships, RealmToMembershipFk),
            ("db/Realms/GetByUpperName.sql",
                "select realm_id, name\nfrom realms\nwhere upper(realms.name) = @name"),
            ("db/Memberships/GetByRealm.sql",
                "select email\nfrom memberships\nwhere memberships.realm_id = @realm_id"));

        Assert.True(Fired(result));
    }

    // ── a self-referencing foreign key ────────────────────────────────────

    private const string SelfReferencingNodes = @"
    ""nodes"": {
      ""name"": ""nodes"",
      ""columns"": {
        ""node_id"": { ""name"": ""node_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""parent_id"": { ""name"": ""parent_id"", ""dbType"": ""int"", ""isNullable"": true },
        ""label"": { ""name"": ""label"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 60 }
      },
      ""indexes"": []
    }";

    private const string SelfFk =
        @"{ ""fromTable"": ""nodes"", ""fromColumn"": ""parent_id"", ""toTable"": ""nodes"", ""toColumn"": ""node_id"" }";

    [Fact]
    public void SelfReferencingChildThatSelfJoins_IsAlreadySetBased()
    {
        var result = Run(
            Schema(SelfReferencingNodes, SelfFk),
            ("db/Nodes/GetAll.sql", "select node_id, label\nfrom nodes"),
            ("db/Nodes/GetWithParent.sql",
                "select c.node_id, p.label\nfrom nodes c\njoin nodes p on c.parent_id = p.node_id\nwhere c.parent_id = @parent_id"));

        Assert.False(Fired(result));
    }

    [Fact]
    public void SelfReferencingChildReadingTheTableOnce_StillFires()
    {
        var result = Run(
            Schema(SelfReferencingNodes, SelfFk),
            ("db/Nodes/GetAll.sql", "select node_id, label\nfrom nodes"),
            ("db/Nodes/GetChildren.sql",
                "select node_id, label\nfrom nodes\nwhere nodes.parent_id = @parent_id"));

        Assert.True(Fired(result));
    }
}
