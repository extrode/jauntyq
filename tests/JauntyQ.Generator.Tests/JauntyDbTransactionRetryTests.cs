using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// CodeEmitter.Part5.cs's <c>EmitJauntyDb</c> emits a <c>JauntyDb</c> class whose
/// <c>BeginTransaction()</c>/<c>BeginTransactionAsync()</c> track "did I open this
/// connection myself" in a private field (<c>_txOpenedConnection</c>) alongside
/// the active transaction (<c>_tx</c>), so <c>EndTransaction()</c> knows whether to
/// close the connection again once the transaction ends (documented: "if the
/// connection was opened here it is closed when the transaction ends").
///
/// Both fields are written together on the *happy* path and cleared together in
/// <c>EndTransaction()</c> -- but they are NOT written atomically with respect to
/// failure: <c>_txOpenedConnection</c> is assigned from live connection state
/// BEFORE <c>_conn.BeginTransaction()</c> is attempted, so if that call throws
/// (a real possibility -- e.g. a cancellation that lands between the connection
/// opening and the "BEGIN TRANSACTION" round-trip, or any transient provider
/// failure), the method exits with <c>_txOpenedConnection == true</c> but
/// <c>_tx == null</c>. The class's own re-entry guard (<c>if (_tx != null) throw</c>)
/// permits a caller to retry <c>BeginTransaction()</c> on the same JauntyDb
/// instance after such a failure (this is the documented/intended recovery path --
/// there is no "poisoned" state). On that retry, <c>_txOpenedConnection</c> is
/// recomputed from <c>_conn.State</c>, which is now Open (left open by the failed
/// attempt) -- so the retry's recomputation silently overwrites the true history
/// with <c>false</c>, even though this JauntyDb instance really did open the
/// connection. When that retried transaction later ends, <c>EndTransaction()</c>
/// never closes the connection: it leaks open for the lifetime of the JauntyDb
/// instance, contradicting the documented "closed when the transaction ends"
/// contract.
///
/// These tests compile the actual <c>EmitJauntyDb</c> output (not a
/// reimplementation) against a fault-injecting fake <c>DbConnection</c>, load it,
/// and drive the retry sequence via reflection -- real execution, not static
/// reasoning, per the audit's verification discipline.
/// </summary>
public class JauntyDbTransactionRetryTests
{
    private const string HarnessSource = @"
using System.Data;
using System.Data.Common;

namespace JauntyQ.Generated.TestHarness
{
    public sealed class FakeDbTransaction : DbTransaction
    {
        private readonly FakeDbConnection _conn;
        public FakeDbTransaction(FakeDbConnection conn) { _conn = conn; }
        protected override DbConnection DbConnection => _conn;
        public override IsolationLevel IsolationLevel => IsolationLevel.Unspecified;
        public override void Commit() { }
        public override void Rollback() { }
    }

    public sealed class FakeDbConnection : DbConnection
    {
        public int FailBeginCount;
        public int BeginAttempts;
        public int OpenCount;
        public int CloseCount;
        private ConnectionState _state = ConnectionState.Closed;

        public override string ConnectionString { get; set; } = """";
        public override string Database => ""fake"";
        public override string DataSource => ""fake"";
        public override string ServerVersion => ""1.0"";
        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName) { }

        public override void Close()
        {
            _state = ConnectionState.Closed;
            CloseCount++;
        }

        public override void Open()
        {
            _state = ConnectionState.Open;
            OpenCount++;
        }

        protected override DbCommand CreateDbCommand() => throw new System.NotSupportedException();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            BeginAttempts++;
            if (BeginAttempts <= FailBeginCount)
                throw new System.InvalidOperationException(""simulated transient begin-transaction failure"");
            return new FakeDbTransaction(this);
        }
    }
}
";

    private static Assembly CompileJauntyDbWithHarness()
    {
        string jauntyDbSource = CodeEmitter.EmitJauntyDb(System.Array.Empty<string>());

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Data.Common.DbConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Data.Common.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Threading.Tasks.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll")),
        };

        var compilation = CSharpCompilation.Create(
            "JauntyDbTransactionRetryTestAssembly",
            new[]
            {
                CSharpSyntaxTree.ParseText(jauntyDbSource),
                CSharpSyntaxTree.ParseText(HarnessSource),
            },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);
        Assert.True(emitResult.Success, string.Join("\n", emitResult.Diagnostics));

        return Assembly.Load(ms.ToArray());
    }

    [Fact]
    public void BeginTransaction_RetriedAfterFailure_ClosesConnectionWhenTransactionEnds()
    {
        var asm = CompileJauntyDbWithHarness();
        var connType = asm.GetType("JauntyQ.Generated.TestHarness.FakeDbConnection")!;
        var dbType = asm.GetType("JauntyQ.Generated.JauntyDb")!;

        object conn = Activator.CreateInstance(connType)!;
        connType.GetField("FailBeginCount")!.SetValue(conn, 1);
        object db = Activator.CreateInstance(dbType, conn)!;

        var beginMethod = dbType.GetMethod("BeginTransaction", Type.EmptyTypes)!;

        // First attempt: the connection opens (JauntyDb owns it), but the
        // underlying BeginDbTransaction fails -- the documented recovery path
        // is that _tx stays null and the instance is not "poisoned".
        var ex = Assert.Throws<TargetInvocationException>(() => beginMethod.Invoke(db, null));
        Assert.IsType<InvalidOperationException>(ex.InnerException);

        // Sanity: the connection really was opened by this failed attempt (it
        // is JauntyDb's job to close it again once done with it).
        int openCountAfterFailure = (int)connType.GetField("OpenCount")!.GetValue(conn)!;
        Assert.Equal(1, openCountAfterFailure);

        // Retry on the same instance: this is the documented/intended recovery
        // (the re-entry guard only rejects a *second concurrent* transaction,
        // not a retry after a failed first attempt).
        object txObj = beginMethod.Invoke(db, null)!;
        var txType = txObj.GetType();
        txType.GetMethod("Commit")!.Invoke(txObj, null);

        var state = (System.Data.ConnectionState)connType.GetProperty("State")!.GetValue(conn)!;
        Assert.Equal(System.Data.ConnectionState.Closed, state);
    }

    [Fact]
    public async Task BeginTransactionAsync_RetriedAfterFailure_ClosesConnectionWhenTransactionEnds()
    {
        var asm = CompileJauntyDbWithHarness();
        var connType = asm.GetType("JauntyQ.Generated.TestHarness.FakeDbConnection")!;
        var dbType = asm.GetType("JauntyQ.Generated.JauntyDb")!;

        object conn = Activator.CreateInstance(connType)!;
        connType.GetField("FailBeginCount")!.SetValue(conn, 1);
        object db = Activator.CreateInstance(dbType, conn)!;

        var beginAsyncMethod = dbType.GetMethod("BeginTransactionAsync")!;

        var firstTask = (Task)beginAsyncMethod.Invoke(db, new object[] { default(System.Threading.CancellationToken) })!;
        Exception? caught = null;
        try
        {
            await firstTask;
        }
        catch (Exception ex)
        {
            caught = ex;
        }
        Assert.IsType<InvalidOperationException>(caught);

        var secondTask = (Task)beginAsyncMethod.Invoke(db, new object[] { default(System.Threading.CancellationToken) })!;
        await secondTask;
        object txObj = secondTask.GetType().GetProperty("Result")!.GetValue(secondTask)!;
        var txType = txObj.GetType();
        txType.GetMethod("Commit")!.Invoke(txObj, null);

        var state = (System.Data.ConnectionState)connType.GetProperty("State")!.GetValue(conn)!;
        Assert.Equal(System.Data.ConnectionState.Closed, state);
    }
}
