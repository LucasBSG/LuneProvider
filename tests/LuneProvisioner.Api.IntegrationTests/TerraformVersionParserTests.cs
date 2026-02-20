using FluentAssertions;
using LuneProvisioner.Api.Infrastructure.Terraform;

namespace LuneProvisioner.Api.IntegrationTests;

public sealed class TerraformVersionParserTests
{
    [Theory]
    [InlineData("1.8.0", 1, 8, 0)]
    [InlineData("v1.10.3", 1, 10, 3)]
    [InlineData("1.9", 1, 9, 0)]
    [InlineData("1", 1, 0, 0)]
    [InlineData("1.11.0-beta1", 1, 11, 0)]
    public void Should_parse_supported_terraform_versions(
        string versionText,
        int expectedMajor,
        int expectedMinor,
        int expectedPatch)
    {
        var success = TerraformVersionParser.TryParse(versionText, out var parsedVersion);

        success.Should().BeTrue();
        parsedVersion.Major.Should().Be(expectedMajor);
        parsedVersion.Minor.Should().Be(expectedMinor);
        parsedVersion.Build.Should().Be(expectedPatch);
    }

    [Theory]
    [InlineData("")]
    [InlineData("foo")]
    [InlineData("version=1.8.0")]
    public void Should_reject_invalid_terraform_versions(string versionText)
    {
        var success = TerraformVersionParser.TryParse(versionText, out _);

        success.Should().BeFalse();
    }
}
