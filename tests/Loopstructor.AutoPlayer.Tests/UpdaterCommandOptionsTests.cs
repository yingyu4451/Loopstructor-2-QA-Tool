using Loopstructor.AutoPlayer.Updater.Models;

namespace Loopstructor.AutoPlayer.Tests;

public sealed class UpdaterCommandOptionsTests
{
    [Fact]
    public void Parse_DefaultWaitTimeoutIsBounded()
    {
        UpdateCommandOptions options = UpdateCommandOptions.Parse(new[]
        {
            "apply", "--target", ".", "--current-version", "0.6.78"
        });

        Assert.Equal(180, options.WaitTimeoutSeconds);
    }

    [Fact]
    public void Parse_ReadsDesktopProcessIdSeparatelyFromWaitPids()
    {
        UpdateCommandOptions options = UpdateCommandOptions.Parse(new[]
        {
            "apply", "--target", ".", "--current-version", "0.6.78",
            "--wait-pid", "1111", "--desktop-pid", "4321"
        });

        Assert.Equal(4321, options.DesktopProcessId);
        Assert.Equal(new[] { 1111 }, options.WaitProcessIds);
    }

    [Fact]
    public void Parse_ExplicitWaitTimeoutOverridesDefault()
    {
        UpdateCommandOptions options = UpdateCommandOptions.Parse(new[]
        {
            "apply", "--target", ".", "--current-version", "0.6.78",
            "--wait-timeout-seconds", "600"
        });

        Assert.Equal(600, options.WaitTimeoutSeconds);
    }

    [Fact]
    public void Parse_RejectsInvalidDesktopProcessId()
    {
        Assert.Throws<ArgumentException>(() => UpdateCommandOptions.Parse(new[]
        {
            "apply", "--target", ".", "--current-version", "0.6.78",
            "--desktop-pid", "0"
        }));
    }
}
