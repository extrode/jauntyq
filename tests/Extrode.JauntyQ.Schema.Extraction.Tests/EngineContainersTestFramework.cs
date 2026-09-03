using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: Xunit.TestFramework("Extrode.JauntyQ.Schema.Extraction.Tests.EngineContainersTestFramework", "Extrode.JauntyQ.Schema.Extraction.Tests")]

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

/// <summary>
/// Stock xUnit framework plus one hook: dispose the shared
/// <see cref="EngineContainers"/> when the assembly run finishes.
/// BeforeTestAssemblyFinishedAsync is the only teardown point that reliably
/// runs on this host — Testcontainers' Ryuk reaper never starts here and
/// AppDomain.ProcessExit never fired in the test host, both measured
/// 2026-07-31 (each leaked all four engine containers per run). Everything
/// else — discovery, [SkippableFact], parallelism — is the standard pipeline.
/// </summary>
public sealed class EngineContainersTestFramework : XunitTestFramework
{
    public EngineContainersTestFramework(IMessageSink messageSink)
        : base(messageSink)
    {
    }

    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName)
        => new Executor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);

    private sealed class Executor : XunitTestFrameworkExecutor
    {
        public Executor(AssemblyName assemblyName, ISourceInformationProvider sourceInformationProvider, IMessageSink diagnosticMessageSink)
            : base(assemblyName, sourceInformationProvider, diagnosticMessageSink)
        {
        }

        protected override async void RunTestCases(IEnumerable<IXunitTestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
        {
            using var runner = new Runner(TestAssembly, testCases, DiagnosticMessageSink, executionMessageSink, executionOptions);
            await runner.RunAsync();
        }
    }

    private sealed class Runner : XunitTestAssemblyRunner
    {
        public Runner(ITestAssembly testAssembly, IEnumerable<IXunitTestCase> testCases, IMessageSink diagnosticMessageSink, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
            : base(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions)
        {
        }

        protected override async Task BeforeTestAssemblyFinishedAsync()
        {
            await EngineContainers.DisposeAllAsync();
            await base.BeforeTestAssemblyFinishedAsync();
        }
    }
}
