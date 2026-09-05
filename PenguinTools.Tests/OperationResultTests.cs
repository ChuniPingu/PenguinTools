using PenguinTools.Core;
using Xunit;

namespace PenguinTools.Tests;

public sealed class OperationResultTests
{
    [Fact]
    public void Success_RequiresValueButAcceptsDefaultValueTypes()
    {
        Assert.Throws<ArgumentNullException>(() => OperationResult<string>.Success(null!));

        var result = OperationResult<int>.Success(0);
        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Value);
    }
}
