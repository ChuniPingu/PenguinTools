using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Application;

internal static class ApplicationDiagnostics
{
    internal static OperationResult<T> Failure<T>(MessageDescriptor message, string? path = null)
    {
        var collector = new DiagnosticCollector();
        collector.Report(path is null
            ? new Diagnostic(Severity.Error, message)
            : new PathDiagnostic(Severity.Error, message, path));
        return OperationResult<T>.Failure().WithDiagnostics(collector);
    }

    internal static OperationResult<T> FromException<T>(Exception exception)
    {
        var collector = new DiagnosticCollector();
        collector.Report(new Diagnostic(Severity.Error, Msg.Unhandled(exception.Message))
        {
            RelatedException = exception
        });
        return OperationResult<T>.Failure().WithDiagnostics(collector);
    }

    internal static OperationResult<T> Merge<T>(T value, DiagnosticSnapshot first, OperationResult second)
    {
        var diagnostics = first.Merge(second.Diagnostics);
        return (second.Succeeded ? OperationResult<T>.Success(value) : OperationResult<T>.Failure())
            .WithDiagnostics(diagnostics);
    }
}