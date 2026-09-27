using System.ComponentModel;
using Microsoft.Data.Sqlite;
using MhwModManager.Core;

namespace MhwModManager.Diagnostics;

public sealed record ExceptionAssessment(FailureCategory Category,string UserSummary,int? NativeCode=null);

public static class ExceptionPolicy
{
    public static ExceptionAssessment Assess(Exception exception)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var ex = Unwrap(exception);
        return ex switch
        {
            OperationCanceledException => new(FailureCategory.ExpectedTransient,"The operation was cancelled."),
            UnauthorizedAccessException => new(FailureCategory.UserActionRequired,"Windows denied access to a required file or folder."),
            FileNotFoundException or DirectoryNotFoundException => new(FailureCategory.UserActionRequired,"A required file or folder is missing."),
            InvalidDataException => new(FailureCategory.DataIntegrityFailure,"Stored or imported data failed an integrity/safety check."),
            SqliteException sql when sql.SqliteErrorCode is 5 or 6 => new(FailureCategory.ExpectedTransient,"The state database is busy or locked.",sql.SqliteErrorCode),
            SqliteException sql when sql.SqliteErrorCode is 11 or 26 => new(FailureCategory.DataIntegrityFailure,"SQLite reported database corruption or invalid database content.",sql.SqliteErrorCode),
            Win32Exception win32 when win32.NativeErrorCode is 5 or 32 or 33 => new(FailureCategory.UserActionRequired,"A Windows process is holding or denying access to a required file.",win32.NativeErrorCode),
            IOException io when io.HResult == unchecked((int)0x80070020) => new(FailureCategory.UserActionRequired,"A required file is in use by another process.",32),
            IOException => new(FailureCategory.RecoverableOperationFailure,"A filesystem operation failed. The transaction should be rolled back before retrying."),
            OutOfMemoryException or AccessViolationException => new(FailureCategory.FatalProcessState,"The process entered an unsafe runtime state."),
            ArgumentException or NullReferenceException or IndexOutOfRangeException => new(FailureCategory.ProgrammingBug,"The application hit an internal programming error."),
            _ => new(FailureCategory.RecoverableOperationFailure,"The operation failed unexpectedly. Diagnostic details were recorded.")
        };
    }

    private static Exception Unwrap(Exception ex)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        while (ex is AggregateException { InnerExceptions.Count: 1 } a) ex = a.InnerExceptions[0];
        return ex;
    }
}
