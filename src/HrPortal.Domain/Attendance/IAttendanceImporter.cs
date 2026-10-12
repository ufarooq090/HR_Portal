namespace HrPortal.Domain.Attendance;

/// <summary>
/// Parses one biometric export format. The device export format is not confirmed yet,
/// so each format is a pluggable parser selected by <see cref="Format"/>.
/// </summary>
public interface IAttendanceFileParser
{
    string Format { get; }

    IAsyncEnumerable<AttendancePunch> ParseAsync(Stream file, TimeZoneInfo deviceTimeZone, CancellationToken cancellationToken = default);
}

public sealed record AttendanceImportResult(int PunchesRead, int PunchesImported, IReadOnlyList<string> Errors);

/// <summary>Imports a biometric export file using the parser registered for its format.</summary>
public interface IAttendanceImporter
{
    Task<AttendanceImportResult> ImportAsync(string format, Stream file, TimeZoneInfo deviceTimeZone, CancellationToken cancellationToken = default);
}
