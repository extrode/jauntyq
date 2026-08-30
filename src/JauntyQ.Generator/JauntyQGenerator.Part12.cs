using System;
using Microsoft.CodeAnalysis;

namespace JauntyQ.Generator;

public partial class JauntyQGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Turns an escaped exception into JNT0001 text.
    ///
    /// Roslyn already has a story for a generator that throws and it is the wrong
    /// one for anybody downstream: the exception is caught at the driver, reported
    /// as CS8785, and CS8785 is a <b>warning</b>. So the build continues, every
    /// type this generator would have emitted is simply absent, and the consumer's
    /// error list is nothing but the CS0246s that follow from the absence. There is
    /// no mention of JauntyQ in any of them.
    ///
    /// The message therefore has three jobs, in this order: say that JauntyQ failed
    /// (so the consumer stops looking at their own code), say which stage (so the
    /// bug is reportable), and say what the consequence is (so the CS0246 storm is
    /// recognised as a symptom rather than investigated as forty separate faults).
    /// The exception type and message are included verbatim; the stack trace is not,
    /// because a diagnostic message is a single line in most consumers' build output
    /// and a trace would push the first two jobs off the end of it.
    /// </summary>
    /// <remarks>
    /// The consequence sentence is hedged ("may be incomplete or entirely missing")
    /// on purpose, because it is not the same at every call site: a throw out of the
    /// aggregate step really does remove every generated type, while a throw out of
    /// the N+1 or predicate-drift pass loses only diagnostics and leaves the emitted
    /// code intact. Naming the stage is what makes the difference legible; claiming
    /// total erasure everywhere would be wrong in three of the five places this is
    /// used, and a diagnostic that overstates is one a consumer learns to discount.
    /// </remarks>
    internal static string InternalErrorMessage(string stage, Exception ex) =>
        "JauntyQ's source generator threw while " + stage + ". Its generated output may be "
        + "incomplete or entirely missing, so any CS0246 'type or namespace not found' errors on "
        + "JauntyDb, an entity or a row POCO are consequences of this one failure rather than "
        + "separate problems. This is a bug in JauntyQ, not in your SQL or schema -- please report "
        + "it with this message: " + ex.GetType().FullName + ": " + ex.Message;

    /// <summary>
    /// Reports <see cref="JauntyDiagnostics.JNT0001"/> for an exception that escaped
    /// one output stage. <see cref="Location.None"/> deliberately: the failure is not
    /// attributable to any one source file, and anchoring it to an arbitrary .sql
    /// would send the consumer to edit a file that is not the cause.
    /// </summary>
    private static void ReportInternalError(SourceProductionContext ctx, string stage, Exception ex) =>
        ctx.ReportDiagnostic(Diagnostic.Create(
            JauntyDiagnostics.JNT0001, Location.None, InternalErrorMessage(stage, ex)));

    /// <summary>
    /// Contains a throw out of <c>ProcessFileCore</c> to the one .sql file that
    /// caused it. Without this the exception leaves the per-file pipeline node,
    /// which is not a per-file failure at all: the driver aborts the generator, so
    /// one malformed query takes down every other query's generated code and the
    /// aggregate types with it.
    ///
    /// The failing file still gets a FileResult, carrying JNT0001 and emitting
    /// nothing. It is reported at the per-file output step like any other
    /// diagnostic, so it lands with the rest and the build fails on that one file
    /// rather than on the absence of everything.
    /// </summary>
    private static FileResult ProcessFile(
        AdditionalText sqlFile,
        string commonPrefix,
        SchemaState schemaState,
        System.Threading.CancellationToken cancellationToken)
    {
        try
        {
            return ProcessFileCore(sqlFile, commonPrefix, schemaState, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            string entityName = ExtractEntityName(sqlFile.Path, commonPrefix);
            string methodName = System.IO.Path.GetFileNameWithoutExtension(sqlFile.Path);

            return FileResult.WithDiagnostics(entityName, methodName,
                System.Collections.Immutable.ImmutableArray.Create(
                    DiagnosticInfo.From(JauntyDiagnostics.JNT0001,
                        InternalErrorMessage("processing '" + sqlFile.Path + "'", ex))));
        }
    }
}
