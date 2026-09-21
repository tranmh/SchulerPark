namespace SchulerPark.Core.Models;

/// <summary>
/// Outcome of fanning one push payload out to every subscription of a user.
/// <see cref="Subscriptions"/> is the number of registered devices before the send;
/// <see cref="Removed"/> counts subscriptions the push service reported as gone
/// (410) and that were deleted as a result.
/// </summary>
public record PushSendResult(int Subscriptions, int Delivered, int Removed, int Failed)
{
    public static readonly PushSendResult NotConfigured = new(0, 0, 0, 0);
    public static readonly PushSendResult NoSubscriptions = new(0, 0, 0, 0);
}
