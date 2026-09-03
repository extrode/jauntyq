namespace Extrode.JauntyQ.Cli;

internal class Program
{
    internal static Task<int> Main(string[] args) => CliHost.Run(args, CoreVerbs.All);
}
