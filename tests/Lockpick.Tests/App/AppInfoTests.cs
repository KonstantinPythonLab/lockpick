using Lockpick.App.Services;

namespace Lockpick.Tests.App;

public class AppInfoTests
{
    [Theory]
    [InlineData("0.1.3+5e367a0c1d", "0.1.3")]
    [InlineData("0.2.0-beta.1+abc", "0.2.0-beta.1")]
    [InlineData("1.0.0", "1.0.0")]
    public void Uses_the_release_version_without_build_metadata(string informational, string expected) =>
        Assert.Equal(expected, AppInfo.Resolve(informational, new Version(9, 9, 9, 0)));

    [Fact]
    public void Falls_back_to_three_part_assembly_version() =>
        Assert.Equal("0.1.3", AppInfo.Resolve(null, new Version(0, 1, 3, 0)));

    [Fact]
    public void The_app_reports_a_clean_semantic_version() =>
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", AppInfo.Version);
}
