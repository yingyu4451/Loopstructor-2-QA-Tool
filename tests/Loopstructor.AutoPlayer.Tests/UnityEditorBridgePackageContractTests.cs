using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Loopstructor.AutoPlayer.Tests;

public sealed class UnityEditorBridgePackageContractTests
{
    [Fact]
    public void EmbeddedPackage_IsEditorOnlyAndDoesNotInjectPlayerLoaderDependencies()
    {
        string root = FindRepositoryRoot();
        string packageRoot = Path.Combine(root, "resources", "unity-package", "com.loopstructor.qa-editor-bridge");
        JObject manifest = JObject.Parse(File.ReadAllText(Path.Combine(packageRoot, "package.json")));
        JObject assembly = JObject.Parse(File.ReadAllText(Path.Combine(packageRoot, "Editor", "Loopstructor.QA.EditorBridge.asmdef")));
        string source = string.Join(
            "\n",
            Directory.EnumerateFiles(packageRoot, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));

        Assert.Equal("com.loopstructor.qa-editor-bridge", manifest.Value<string>("name"));
        Assert.Equal(new[] { "Editor" }, assembly["includePlatforms"]?.Values<string>());
        Assert.DoesNotContain("BepInEx", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Harmony", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UnityEngine.Debug", source, StringComparison.Ordinal);
        Assert.Contains("/api/command", source, StringComparison.Ordinal);
        Assert.Contains("Authorization", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedPackage_ReportsPlayModeSaveStatusForAutoBackup()
    {
        string root = FindRepositoryRoot();
        string packageRoot = Path.Combine(root, "resources", "unity-package", "com.loopstructor.qa-editor-bridge");
        string server = File.ReadAllText(Path.Combine(packageRoot, "Editor", "QaBridgeServer.cs"));
        string bridge = File.ReadAllText(Path.Combine(
            root, "src", "Loopstructor.AutoPlayer.EditorBridge.Runtime", "UnityEditorCheatBridge.cs"));
        string reader = File.ReadAllText(Path.Combine(
            root, "src", "Loopstructor.AutoPlayer.EditorBridge.Runtime", "EditorRunStatusReader.cs"));
        string host = File.ReadAllText(Path.Combine(root, "src", "Loopstructor.AutoPlayer.Host", "DesktopHostEngine.cs"));

        // /api/status 在 Play Mode 下必须带回存档根与「章-关」，Host 轮询写入状态后自动备份才会触发。
        Assert.Contains("UnityEditorCheatBridge.BuildRunStatus()", server, StringComparison.Ordinal);
        Assert.Contains("BuildRunStatus", bridge, StringComparison.Ordinal);
        Assert.Contains("runStatusReady", reader, StringComparison.Ordinal);
        Assert.Contains("activeSaveRoot", reader, StringComparison.Ordinal);
        Assert.Contains("FlowSaveSlotResolver", reader, StringComparison.Ordinal);
        Assert.Contains("SaveRootSelection", reader, StringComparison.Ordinal);
        Assert.Contains("connection.ApplyRunStatus(_status)", host, StringComparison.Ordinal);
        // Editor 下读档会覆盖真实存档且无法自动重启 Play Mode，必须明确拒绝。
        Assert.Contains("Unity Editor 连接下不能一键读档", host, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedPackageVersion_MatchesEveryProductVersionSurface()
    {
        string root = FindRepositoryRoot();
        string version = XDocument.Load(Path.Combine(root, "Directory.Build.props"))
            .Descendants("VersionPrefix")
            .Single()
            .Value;
        string pluginInfo = File.ReadAllText(Path.Combine(root, "src", "Loopstructor.AutoPlayer.Plugin", "PluginInfo.cs"));
        string pluginVersion = Regex.Match(pluginInfo, "Version = \\\"(?<value>[^\\\"]+)\\\"").Groups["value"].Value;
        string desktopVersion = JObject.Parse(File.ReadAllText(Path.Combine(root, "desktop", "package.json"))).Value<string>("version")!;
        string bridgeVersion = JObject.Parse(File.ReadAllText(Path.Combine(
            root,
            "resources",
            "unity-package",
            "com.loopstructor.qa-editor-bridge",
            "package.json"))).Value<string>("version")!;

        Assert.Equal("0.6.122", version);
        Assert.Equal(version, pluginVersion);
        Assert.Equal(version, desktopVersion);
        Assert.Equal(version, bridgeVersion);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? cursor = new(AppContext.BaseDirectory);
        while (cursor != null)
        {
            if (File.Exists(Path.Combine(cursor.FullName, "Loopstructor.AutoPlayer.sln"))) return cursor.FullName;
            cursor = cursor.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
