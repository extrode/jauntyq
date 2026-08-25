using System.Text;
using JauntyQ.SqlParser;
using JauntyQ.SqlParser.Tokens;
using SharpFuzz;

using Parser = JauntyQ.SqlParser.SqlParser;

namespace JauntyQ.Fuzz;

/// <summary>
/// libFuzzer harness for the SQL front end. The oracle is total-ness, not
/// correctness: <see cref="SqlTokenizer.Tokenize"/> and <see cref="Parser.Parse"/>
/// run UNGUARDED inside the Roslyn generator, so any escaping exception is
/// reported by Roslyn as CS8785 and every generated method vanishes from the
/// consumer's build. A crash here is therefore a build-breaking defect for
/// every consumer with a .sql file of that shape, not a cosmetic one.
///
/// Run (Linux, libFuzzer). The last line must go through the native driver:
/// launched as plain `dotnet JauntyQ.Fuzz.dll`, SharpFuzz finds none of the
/// IPC environment variables and replays args[1] as a single file instead.
///   dotnet publish tools/JauntyQ.Fuzz -c Release -o out/fuzz
///   sharpfuzz out/fuzz/JauntyQ.SqlParser.dll
///   ./libfuzzer-dotnet -max_total_time=600 --target_path="$(command -v dotnet)" \
///     --target_arg=out/fuzz/JauntyQ.Fuzz.dll tools/JauntyQ.Fuzz/corpus
///
/// Minimise any crash before promoting it (-minimize_crash=1), then add it to
/// HostileInputParserTests with a name and a reason, per the plan's
/// regression-corpus discipline.
/// </summary>
public static class Program
{
    public static void Main(string[] args) => Fuzzer.LibFuzzer.Run(Fuzz);

    private static void Fuzz(ReadOnlySpan<byte> bytes)
    {
        string sql;
        try
        {
            sql = Encoding.UTF8.GetString(bytes);
        }
        catch (ArgumentException)
        {
            // Not a defect in the parser: the generator only ever hands it text
            // Roslyn already decoded from an AdditionalFile.
            return;
        }

        var tokens = SqlTokenizer.Tokenize(sql);

        // Mirror the generator's own gates (JauntyQGenerator.Part2.cs): a token
        // list carrying a sentinel is rejected with a diagnostic and never
        // reaches Parse, so a Parse failure on one is not a reachable defect.
        foreach (var token in tokens)
        {
            if (token.Type is TokenType.Unterminated or TokenType.Unknown
                           or TokenType.TooLarge or TokenType.TooDeep)
                return;
        }

        Parser.Parse(tokens, "FuzzQuery");
    }
}
