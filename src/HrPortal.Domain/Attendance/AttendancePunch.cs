namespace HrPortal.Domain.Attendance;

public enum PunchDirection
{
    Unknown = 0,
    In = 1,
    Out = 2,
}

/// <summary>One raw clock event read from a biometric device export.</summary>
/// <param name="DeviceEmployeeCode">The employee's enrolment code on the device.</param>
/// <param name="Timestamp">Punch time with the device's UTC offset applied.</param>
public sealed record AttendancePunch(string DeviceEmployeeCode, DateTimeOffset Timestamp, PunchDirection Direction, string? DeviceId);
