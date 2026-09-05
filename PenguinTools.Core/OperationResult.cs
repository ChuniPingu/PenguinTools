using System.Diagnostics.CodeAnalysis;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Core;

public readonly record struct OperationResult(bool Succeeded)
{
    public DiagnosticSnapshot Diagnostics { get; init; } = DiagnosticSnapshot.Empty;

    public static OperationResult Success()
    {
        return new OperationResult(true);
    }

    public static OperationResult Failure()
    {
        return new OperationResult(false);
    }

    public OperationResult WithDiagnostics(DiagnosticSnapshot diagnostics)
    {
        return this with { Diagnostics = diagnostics };
    }

    public OperationResult WithDiagnostics(IDiagnosticSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        return WithDiagnostics(DiagnosticSnapshot.Create(sink));
    }
}

public readonly record struct OperationResult<T>
{
    private OperationResult(bool succeeded, T? value)
    {
        Succeeded = succeeded;
        Value = value;
    }

    [MemberNotNullWhen(true, nameof(Value))]
    public bool Succeeded { get; }

    public T? Value { get; }

    public DiagnosticSnapshot Diagnostics { get; init; } = DiagnosticSnapshot.Empty;

    public static OperationResult<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new OperationResult<T>(true, value);
    }

    public static OperationResult<T> Failure()
    {
        return new OperationResult<T>(false, default);
    }

    public OperationResult ToResult()
    {
        return (Succeeded ? OperationResult.Success() : OperationResult.Failure()).WithDiagnostics(Diagnostics);
    }

    public OperationResult<T> WithDiagnostics(DiagnosticSnapshot diagnostics)
    {
        return this with { Diagnostics = diagnostics };
    }

    public OperationResult<T> WithDiagnostics(IDiagnosticSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        return WithDiagnostics(DiagnosticSnapshot.Create(sink));
    }
}
