using MemoryIndexer.Models;
using Xunit;

namespace MemoryIndexer.Tests.Models;

/// <summary>
/// The profile key of a user is recognised as one and names its owner; a real user id is not a profile key.
/// </summary>
public class ArchiveUserKeyTests
{
    [Fact]
    public void For_PrefixesTheUserId()
        => Assert.Equal("__archive__:u", ArchiveUserKey.For("u"));

    [Fact]
    public void AProfileKey_IsRecognised_AndNamesItsOwner()
    {
        var key = ArchiveUserKey.For("user:42");

        Assert.True(ArchiveUserKey.IsArchiveKey(key));
        Assert.Equal("user:42", ArchiveUserKey.OwnerOf(key));
    }

    [Theory]
    [InlineData("u")]
    [InlineData("archive:u")]
    [InlineData("__ARCHIVE__:u")]
    [InlineData(null)]
    public void ARealUserId_IsNotAProfileKey(string? userId)
    {
        Assert.False(ArchiveUserKey.IsArchiveKey(userId));
        Assert.Null(ArchiveUserKey.OwnerOf(userId));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void For_RefusesABlankUserId(string userId)
        => Assert.ThrowsAny<ArgumentException>(() => ArchiveUserKey.For(userId));
}
