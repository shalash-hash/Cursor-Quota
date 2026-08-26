using Quota.Services;
using Xunit;

namespace Quota.Tests;

public sealed class UiSettingsServiceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(double.NaN)]
    public void SanitizeWindowSize_InvalidValues_ReturnNull(double value)
    {
        var result = UiSettingsService.SanitizeWindowSize(value, UiSettingsService.MinWindowWidth);

        Assert.Null(result);
    }

    [Fact]
    public void SanitizeWindowSize_ValidValue_ReturnsValue()
    {
        var result = UiSettingsService.SanitizeWindowSize(900, UiSettingsService.MinWindowWidth);

        Assert.Equal(900, result);
    }
}
