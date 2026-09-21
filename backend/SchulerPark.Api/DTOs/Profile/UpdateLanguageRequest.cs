namespace SchulerPark.Api.DTOs.Profile;

using System.ComponentModel.DataAnnotations;

/// <summary>Body of PUT /api/profile/language — "de" or "en".</summary>
public record UpdateLanguageRequest([Required, MaxLength(10)] string Language);
