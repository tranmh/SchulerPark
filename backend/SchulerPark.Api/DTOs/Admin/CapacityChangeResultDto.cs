namespace SchulerPark.Api.DTOs.Admin;

using SchulerPark.Core.Models;

/// <summary>Impact of a capacity change on existing bookings (WP1 3.4), toasted by the admin UI.</summary>
public record CapacityChangeResultDto(int Affected, int Reassigned, int Waitlisted, int Cancelled)
{
    public static CapacityChangeResultDto From(CapacityChangeResult r) =>
        new(r.Affected, r.Reassigned, r.Waitlisted, r.Cancelled);
}

/// <summary>Response of <c>POST /api/admin/blocked-days</c>: the block plus what it did to bookings.</summary>
public record AdminBlockedDayCreatedDto(AdminBlockedDayDto BlockedDay, CapacityChangeResultDto Impact);
