namespace MemoryIndexer.Models;

/// <summary>
/// The user key under which a user's profile (the facts promoted into <see cref="Interfaces.IArchiveStore"/>) is kept in
/// the memory store.
/// </summary>
/// <remarks>
/// Profile entries are stored as <see cref="MemoryUnit"/>s in the configured <see cref="Interfaces.IMemoryStore"/>, so
/// they persist wherever the memories do. They are stored under <see cref="For"/>(userId) rather than the user's own id:
/// every store partitions by <see cref="MemoryUnit.UserId"/>, so a recall for the user never returns a profile entry, in
/// any store, without the store knowing about profiles. A job that visits every user of a store
/// (<see cref="Interfaces.IMemoryStore.GetUserIdsAsync"/>) sees these keys too and skips them with
/// <see cref="IsArchiveKey"/>; a migration copies them like any other user, which is how it carries the profiles.
/// </remarks>
public static class ArchiveUserKey
{
    /// <summary>
    /// The prefix that marks a user key as a profile key.
    /// </summary>
    public const string Prefix = "__archive__:";

    /// <summary>
    /// The key under which the profile of <paramref name="userId"/> is stored.
    /// </summary>
    /// <param name="userId">The user whose profile it is.</param>
    /// <returns><see cref="Prefix"/> followed by <paramref name="userId"/>.</returns>
    public static string For(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return Prefix + userId;
    }

    /// <summary>
    /// Whether a user key is a profile key rather than a real user.
    /// </summary>
    /// <param name="userId">A user key, such as one listed by <see cref="Interfaces.IMemoryStore.GetUserIdsAsync"/>.</param>
    /// <returns>True when <paramref name="userId"/> starts with <see cref="Prefix"/>.</returns>
    public static bool IsArchiveKey(string? userId)
        => userId is not null && userId.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// The user whose profile a profile key holds.
    /// </summary>
    /// <param name="archiveKey">A user key.</param>
    /// <returns>The owner's user id, or null when <paramref name="archiveKey"/> is not a profile key.</returns>
    public static string? OwnerOf(string? archiveKey)
        => IsArchiveKey(archiveKey) && archiveKey!.Length > Prefix.Length ? archiveKey[Prefix.Length..] : null;
}
