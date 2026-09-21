namespace SchulerPark.Api.DTOs.Push;

/// <summary>Result of <c>POST /api/push/test</c>: how many of the caller's devices were reached.</summary>
public record PushTestResponse(int Subscriptions, int Delivered, int Removed, int Failed);
