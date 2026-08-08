using GameSaveHub.Core;

namespace GameSaveHub.UnitTests;

public sealed class FileSafetyTests
{
    [Fact]
    public void GetSafeRelativePathReturnsPortableSeparators()
    {
        var root = Path.Combine(Path.GetTempPath(), "gsh-root");
        var path = Path.Combine(root, "container", "blob");

        Assert.Equal("container/blob", FileSafety.GetSafeRelativePath(root, path));
    }

    [Fact]
    public void GetSafeRelativePathRejectsTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "gsh-root");
        var outside = Path.Combine(Path.GetTempPath(), "outside", "blob");

        Assert.Throws<InvalidOperationException>(() => FileSafety.GetSafeRelativePath(root, outside));
    }

    [Fact]
    public void IsSameOrDescendantDoesNotAcceptSiblingPrefix()
    {
        var root = Path.Combine(Path.GetTempPath(), "save");
        var sibling = Path.Combine(Path.GetTempPath(), "save-old");

        Assert.False(FileSafety.IsSameOrDescendant(sibling, root));
    }
}
