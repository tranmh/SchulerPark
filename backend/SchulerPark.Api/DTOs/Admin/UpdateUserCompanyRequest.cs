namespace SchulerPark.Api.DTOs.Admin;

using System.ComponentModel.DataAnnotations;

/// <summary>Phase 21: SuperAdmin sets or clears (null/blank) a user's company.</summary>
public record UpdateUserCompanyRequest([MaxLength(200)] string? Company);
