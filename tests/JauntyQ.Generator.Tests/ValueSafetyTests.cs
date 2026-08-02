using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Tier 2 value safety: SQL literals proven against column constraints at
/// compile time (JNT5001/JNT5002), client-side length guards on write
/// parameters, and DbParameter.Size/Precision/Scale emission from the
/// schema snapshot.
/// </summary>
public class ValueSafetyTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 40, ""isUnicode"": false },
        ""unit_price"": { ""name"": ""unit_price"", ""dbType"": ""decimal"", ""isNullable"": false, ""precision"": 10, ""scale"": 2 },
        ""quantity"": { ""name"": ""quantity"", ""dbType"": ""smallint"", ""isNullable"": false },
        ""reorder_level"": { ""name"": ""reorder_level"", ""dbType"": ""smallint"", ""isNullable"": true },
        ""notes"": { ""name"": ""notes"", ""dbType"": ""nvarchar"", ""isNullable"": true, ""maxLength"": -1, ""isUnicode"": true }
      }
    }
  }
}";

    private static GeneratorDriverRunResult Run(string sql, string path = "db/Products/TestQuery.sql")
    {
        var compilation = CSharpCompilation.Create("ValueSafetyTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText(path, sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static string QuerySource(GeneratorDriverRunResult result) =>
        result.Results[0].GeneratedSources
            .Single(s => s.HintName == "Products.TestQuery.g.cs")
            .SourceText.ToString();

    private static bool HasQuerySource(GeneratorDriverRunResult result) =>
        result.Results[0].GeneratedSources.Any(s => s.HintName == "Products.TestQuery.g.cs");

    // ── JNT5001: string literals vs max length ─────────────────────────

    [Fact]
    public void StringLiteralTooLong_Insert_JNT5001_NoSource()
    {
        string tooLong = new string('A', 41);
        var result = Run($"insert into products (product_name) values ('{tooLong}')");

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT5001");
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("41 chars", diag.GetMessage());
        Assert.Contains("products.product_name", diag.GetMessage());
        Assert.Contains("max length of 40", diag.GetMessage());
        Assert.False(HasQuerySource(result));
    }

    [Fact]
    public void StringLiteralTooLong_Where_JNT5001()
    {
        string tooLong = new string('B', 41);
        var result = Run($"select product_id\nfrom products\nwhere products.product_name = '{tooLong}'");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT5001");
        Assert.False(HasQuerySource(result));
    }

    [Fact]
    public void StringLiteral_EscapedQuotesCountAsOneChar()
    {
        // 39 chars + one escaped quote = 40 decoded chars: fits exactly.
        string value = new string('C', 39) + "''";
        var result = Run($"select product_id\nfrom products\nwhere products.product_name = '{value}'");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT5001");
        Assert.True(HasQuerySource(result));
    }

    // ── JNT5001: maxLength is counted in the engine's own unit ─────────

    private static GeneratorDriverRunResult RunVarchar(string dialect, string sql)
    {
        string schema = @"{
  ""dialect"": """ + dialect + @""",
  ""tables"": {
    ""notes"": {
      ""name"": ""notes"",
      ""columns"": {
        ""note_id"": { ""name"": ""note_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""body"": { ""name"": ""body"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 40, ""isUnicode"": true }
      }
    }
  }
}";
        var compilation = CSharpCompilation.Create("VarcharTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("db/Notes/TestQuery.sql", sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", schema)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    // U+1D54F MATHEMATICAL DOUBLE-STRUCK CAPITAL X: one character, one code
    // point, two UTF-16 code units.
    private const string AstralChar = "\U0001D54F";

    private static string Repeat(string s, int count)
    {
        var sb = new System.Text.StringBuilder(s.Length * count);
        for (int i = 0; i < count; i++)
            sb.Append(s);
        return sb.ToString();
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("mysql")]
    [InlineData("sqlite")]
    public void AstralLiteralWithinCharacterLimit_NoJNT5001(string dialect)
    {
        // 21 characters against varchar(40). Postgres, MySQL and SQLite all
        // count maxLength in characters, so this fits with room to spare --
        // but .NET's string.Length counts the 42 UTF-16 code units.
        string value = Repeat(AstralChar, 21);
        var result = RunVarchar(dialect, $"insert into notes (body) values ('{value}')");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT5001");
    }

    [Fact]
    public void AstralLiteralWithinCharacterLimit_SqlServer_JNT5001()
    {
        // SQL Server is the exception: nvarchar(n) is n UTF-16 code units, so
        // 21 astral characters genuinely need 42 and genuinely do not fit.
        string value = Repeat(AstralChar, 21);
        var result = RunVarchar("sqlserver", $"insert into notes (body) values ('{value}')");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT5001");
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("mysql")]
    [InlineData("sqlite")]
    public void AstralLiteralOverCharacterLimit_JNT5001(string dialect)
    {
        // 41 characters against varchar(40): over the limit in every unit.
        string value = Repeat(AstralChar, 41);
        var result = RunVarchar(dialect, $"insert into notes (body) values ('{value}')");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT5001");
    }

    // ── JNT5002: numeric literals vs precision/scale and integer ranges ─

    [Fact]
    public void DecimalLiteralOverflow_JNT5002()
    {
        // decimal(10,2) holds at most 8 digits before the point.
        var result = Run("update products\nset unit_price = 123456789.99\nwhere product_id = @product_id");

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT5002");
        Assert.Contains("products.unit_price", diag.GetMessage());
        Assert.False(HasQuerySource(result));
    }

    [Fact]
    public void DecimalLiteralExcessScale_JNT5002()
    {
        // decimal(10,2) keeps two fractional digits: 1.234 is stored as 1.23.
        // Every engine rounds silently, so nothing surfaces at runtime either.
        var result = Run("update products\nset unit_price = 1.234\nwhere product_id = @product_id");

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT5002");
        Assert.Contains("products.unit_price", diag.GetMessage());
        Assert.Contains("scale 2", diag.GetMessage());
        Assert.False(HasQuerySource(result));
    }

    [Fact]
    public void NegativeDecimalLiteralExcessScale_JNT5002()
    {
        var result = Run("select product_id\nfrom products\nwhere products.unit_price = -1.234");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT5002");
    }

    [Fact]
    public void DecimalLiteralTrailingZerosWithinScale_NoJNT5002()
    {
        // 1.500 is written with three fractional digits but is exactly
        // representable at scale 2, so nothing is lost: counting digits
        // instead of comparing values would make this a false positive.
        var result = Run("update products\nset unit_price = 1.500\nwhere product_id = @product_id");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT5002");
        Assert.True(HasQuerySource(result));
    }

    [Fact]
    public void IntLiteralOutOfRange_JNT5002()
    {
        var result = Run("select product_id\nfrom products\nwhere products.product_id = 9999999999");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT5002");
    }

    [Fact]
    public void SmallintLiteralOutOfRange_JNT5002()
    {
        var result = Run("select product_id\nfrom products\nwhere products.quantity = 40000");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT5002");
    }

    [Fact]
    public void NegativeWhereLiteralOutOfRange_JNT5002()
    {
        // Negative WHERE literals once weren't captured at all, silently
        // skipping the range check.
        var result = Run("select product_id\nfrom products\nwhere products.quantity = -40000");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT5002");
    }

    [Fact]
    public void NegativeWhereLiteralInRange_NoJNT5002()
    {
        var result = Run("select product_id\nfrom products\nwhere products.quantity = -5");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT5002");
        Assert.True(HasQuerySource(result));
    }

    [Fact]
    public void FittingLiterals_NoValueSafetyDiagnostics()
    {
        var result = Run("select product_id\nfrom products\nwhere products.product_name = 'ok' and products.unit_price >= 99999999.99 and products.quantity = 32000");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("JNT5"));
        Assert.True(HasQuerySource(result));
    }

    // ── tinyint is dialect-dependent: SQL Server 0..255, MySQL -128..127 ─

    private static GeneratorDriverRunResult RunTinyint(string dialect, string dbType, string sql)
    {
        string schema = @"{
  ""dialect"": """ + dialect + @""",
  ""tables"": {
    ""flags"": {
      ""name"": ""flags"",
      ""columns"": {
        ""flag_id"": { ""name"": ""flag_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""level"": { ""name"": ""level"", ""dbType"": """ + dbType + @""", ""isNullable"": false }
      }
    }
  }
}";
        var compilation = CSharpCompilation.Create("TinyintTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("db/Flags/TestQuery.sql", sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", schema)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    [Fact]
    public void MySqlTinyint_MinusOne_Fits_NoJNT5002()
    {
        // MySQL tinyint is SIGNED (-128..127); -1 is a perfectly valid value.
        var result = RunTinyint("mysql", "tinyint",
            "insert into flags (flag_id, level) values (@flagId, -1)");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT5002");
    }

    [Fact]
    public void MySqlTinyint_200_OutOfRange_JNT5002()
    {
        var result = RunTinyint("mysql", "tinyint",
            "insert into flags (flag_id, level) values (@flagId, 200)");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT5002");
    }

    [Fact]
    public void MySqlTinyintUnsigned_200_Fits_NoJNT5002()
    {
        var result = RunTinyint("mysql", "tinyint unsigned",
            "insert into flags (flag_id, level) values (@flagId, 200)");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT5002");
    }

    [Fact]
    public void SqlServerTinyint_MinusOne_OutOfRange_JNT5002()
    {
        // SQL Server tinyint is unsigned 0..255.
        var result = RunTinyint("sqlserver", "tinyint",
            "insert into flags (flag_id, level) values (@flagId, -1)");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT5002");
    }

    [Fact]
    public void SqlServerTinyint_200_Fits_NoJNT5002()
    {
        var result = RunTinyint("sqlserver", "tinyint",
            "insert into flags (flag_id, level) values (@flagId, 200)");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT5002");
    }

    // ── MySQL mediumint/year: previously missing from the range switch ──

    [Fact]
    public void MySqlMediumint_99999999_OutOfRange_JNT5002()
    {
        // DialectMapper maps mediumint to System.Int32 (a safe superset for
        // storage), but mediumint's true database range (-8388608..8388607
        // signed) is far narrower than int32. 99999999 fits int32 -- before
        // this fix, mediumint had no case in the range switch at all, so
        // this compiled with zero warning and the database itself rejected
        // it at runtime ("Out of range value").
        var result = RunTinyint("mysql", "mediumint",
            "insert into flags (flag_id, level) values (@flagId, 99999999)");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT5002");
    }

    [Fact]
    public void MySqlMediumint_8000000_Fits_NoJNT5002()
    {
        var result = RunTinyint("mysql", "mediumint",
            "insert into flags (flag_id, level) values (@flagId, 8000000)");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT5002");
    }

    [Fact]
    public void MySqlMediumintUnsigned_16000000_Fits_NoJNT5002()
    {
        var result = RunTinyint("mysql", "mediumint unsigned",
            "insert into flags (flag_id, level) values (@flagId, 16000000)");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT5002");
    }

    [Fact]
    public void MySqlYear_9999_OutOfRange_JNT5002()
    {
        var result = RunTinyint("mysql", "year",
            "insert into flags (flag_id, level) values (@flagId, 9999)");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT5002");
    }

    [Fact]
    public void MySqlYear_2026_Fits_NoJNT5002()
    {
        var result = RunTinyint("mysql", "year",
            "insert into flags (flag_id, level) values (@flagId, 2026)");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT5002");
    }

    // ── Postgres SERIAL family: JNT5002's range switch (a sibling of
    // DialectMapper.MapDbTypeToCSharp's own serial-family switch, discovered
    // via this round's §4.4 sweep) had "serial"/"bigserial" but never
    // "smallserial" -- a residual gap left by AUD-R19-01, which fixed only
    // the C#-type-mapping side, not this compile-time range-check side --
    // nor the AUD-R21-01 "serial2"/"serial4"/"serial8" numeric synonyms.
    // Before this fix, all four fell to the switch's `_ => null` arm, so
    // `range.HasValue` was false and JNT5002 silently never fired for any of
    // them, no matter how far out of range the literal.

    [Fact]
    public void PostgresSmallserial_40000_OutOfRange_JNT5002()
    {
        // smallserial's underlying type is smallint (-32768..32767); 40000
        // overflows it.
        var result = RunTinyint("postgres", "smallserial",
            "insert into flags (flag_id, level) values (@flagId, 40000)");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT5002");
    }

    [Fact]
    public void PostgresSmallserial_20000_Fits_NoJNT5002()
    {
        var result = RunTinyint("postgres", "smallserial",
            "insert into flags (flag_id, level) values (@flagId, 20000)");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT5002");
    }

    [Fact]
    public void PostgresSerial2_40000_OutOfRange_JNT5002()
    {
        // "serial2" is smallserial's pure-numeric synonym -- same underlying
        // smallint range.
        var result = RunTinyint("postgres", "serial2",
            "insert into flags (flag_id, level) values (@flagId, 40000)");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT5002");
    }

    [Fact]
    public void PostgresSerial4_5000000000_OutOfRange_JNT5002()
    {
        // "serial4" is serial's synonym -- underlying int range
        // (-2147483648..2147483647); 5 billion overflows it.
        var result = RunTinyint("postgres", "serial4",
            "insert into flags (flag_id, level) values (@flagId, 5000000000)");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT5002");
    }

    [Fact]
    public void PostgresSerial8_9223372036854775807_Fits_NoJNT5002()
    {
        // "serial8" is bigserial's synonym -- long.MaxValue must fit exactly.
        var result = RunTinyint("postgres", "serial8",
            "insert into flags (flag_id, level) values (@flagId, 9223372036854775807)");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT5002");
    }

    // ── Guards and DbParameter sizing ───────────────────────────────────

    [Fact]
    public void WriteParams_GetGuardAndFixedSize_MaxColumnsGetMinusOne()
    {
        var result = Run("insert into products (product_name, unit_price, notes)\nvalues (@product_name, @unit_price, @notes)");
        string source = QuerySource(result);

        // guard: fail fast client-side, naming the column and limit
        Assert.Contains("if (product_name.Length > 40)", source);
        Assert.Contains("exceeds products.product_name max length (40)", source);
        Assert.Contains("nameof(product_name)", source);

        // fixed size is safe because the guard proved the value fits
        Assert.Contains(".Size = 40;", source);

        // decimal precision/scale from the snapshot
        Assert.Contains(".Precision = 10;", source);
        Assert.Contains(".Scale = 2;", source);

        // nvarchar(max): unbounded, no guard
        Assert.Contains(".Size = -1;", source);
        Assert.DoesNotContain("notes.Length >", source);
    }

    [Fact]
    public void NullableWriteParam_GuardIsNullSafe()
    {
        var result = Run("update products\nset notes = @notes, product_name = @product_name\nwhere product_id = @product_id");
        string source = QuerySource(result);

        // notes is nvarchar(max): no guard at all; product_name gets the
        // non-null guard because the schema says NOT NULL.
        Assert.Contains("if (product_name.Length > 40)", source);
    }

    [Fact]
    public void ComparisonParam_SizesDynamically_NeverTruncates()
    {
        var result = Run("select product_id\nfrom products\nwhere products.product_name = @product_name");
        string source = QuerySource(result);

        // read path: no guard, dynamic size (oversize values keep their
        // length and match nothing; fixed size could truncate into a WRONG match)
        Assert.DoesNotContain("throw new System.ArgumentException", source);
        Assert.Contains(".Size = product_name.Length > 40 ? product_name.Length : 40;", source);
    }

    [Fact]
    public void CrudWhereParam_AlsoSizesDynamically()
    {
        var result = Run("delete from products\nwhere product_name = @product_name");
        string source = QuerySource(result);

        Assert.DoesNotContain("throw new System.ArgumentException", source);
        Assert.Contains(".Size = product_name.Length > 40 ? product_name.Length : 40;", source);
    }

    // ── nullable value-type reader default ──────────────────────────────

    [Fact]
    public void CustomQueryReader_NullableValueTypeColumn_UsesTypedDefaultNotBareDefault()
    {
        // reorder_level is smallint NULL -> short?. A bare `default` in the null
        // arm would infer short (from GetInt16) and yield 0 instead of null.
        var result = Run("select reorder_level\nfrom products\nwhere products.product_id = @product_id");
        string source = QuerySource(result);

        Assert.Contains("default(short?)", source);
        Assert.DoesNotContain("? default :", source);
    }

    // ── Null guards for non-nullable reference parameters ───────────────

    [Fact]
    public void NonNullableStringParam_EmitsArgumentNullGuard()
    {
        // product_name is NOT NULL -> the parameter is `string`; a null from a
        // non-NRT caller must fail fast with the parameter name, not NRE in
        // the length guard or reach the provider as an unset value.
        var result = Run("insert into products (product_name) values (@product_name)");
        string source = QuerySource(result);

        Assert.Contains("if (product_name is null)", source);
        Assert.Contains("throw new ArgumentNullException(nameof(product_name));", source);
    }

    [Fact]
    public void NullableParam_NoArgumentNullGuard()
    {
        // reorder_level is nullable: null is a legitimate value (binds DBNull).
        var result = Run("select product_id, product_name\nfrom products\nwhere products.reorder_level = @reorder_level");
        string source = QuerySource(result);

        Assert.DoesNotContain("throw new System.ArgumentNullException(nameof(reorder_level));", source);
    }
}
