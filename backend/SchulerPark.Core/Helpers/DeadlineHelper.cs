namespace SchulerPark.Core.Helpers;

using SchulerPark.Core.Enums;

public static class DeadlineHelper
{
    private static readonly TimeZoneInfo BerlinTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    /// <summary>
    /// Returns the confirmation deadline in UTC.
    /// 1 hour before slot start: Morning 06:00, Afternoon 13:00 Europe/Berlin.
    /// </summary>
    public static DateTime GetConfirmationDeadline(DateOnly date, TimeSlot timeSlot)
    {
        var hour = timeSlot == TimeSlot.Morning ? 6 : 13;
        var berlinTime = new DateTime(date.Year, date.Month, date.Day, hour, 0, 0);
        return TimeZoneInfo.ConvertTimeToUtc(berlinTime, BerlinTz);
    }

    public static bool IsDeadlinePassed(DateOnly date, TimeSlot timeSlot)
    {
        return DateTime.UtcNow >= GetConfirmationDeadline(date, timeSlot);
    }
}
