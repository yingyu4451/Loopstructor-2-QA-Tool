using System.Text;
using System.Text.RegularExpressions;
using Loopstructor.AutoPlayer.Manager.Services;

namespace Loopstructor.AutoPlayer.Tests;

public sealed class UnityAssetMetaGuidRepairerTests
{
    [Fact]
    public void Repair_ReplacesOnlyInvalidGuidsAndKeepsMetaContent()
    {
        using TemporaryDirectory temporary = new();
        string projectRoot = Path.Combine(temporary.Root, "Loopstructor2");
        string brokenMeta = Path.Combine(projectRoot, "Assets", "策划配置", "开发记录", "20260916-181000_Codex_A08-010-014.md.meta");
        string validMeta = Path.Combine(projectRoot, "Assets", "策划配置", "开发记录", "valid.md.meta");
        string outsideRootsMeta = Path.Combine(projectRoot, "Library", "ignored.md.meta");
        const string brokenGuid = "99db159e53ca4b6b8e346883205dab2b9";
        WriteMeta(brokenMeta, brokenGuid, withPreamble: true);
        WriteMeta(validMeta, "0123456789abcdef0123456789abcdef", withPreamble: false);
        WriteMeta(outsideRootsMeta, brokenGuid, withPreamble: false);

        UnityAssetMetaGuidRepairResult result = UnityAssetMetaGuidRepairer.Repair(projectRoot);

        Assert.Equal(1, result.RepairedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(brokenMeta)[..3]);
        string repaired = Encoding.UTF8.GetString(File.ReadAllBytes(brokenMeta), 3, (int)new FileInfo(brokenMeta).Length - 3);
        Match guid = Regex.Match(repaired, @"(?m)^guid:[ \t]*([0-9a-fA-F]+)[ \t]*$");
        Assert.True(guid.Success, repaired);
        Assert.Matches("^[0-9a-fA-F]{32}$", guid.Groups[1].Value);
        Assert.Contains("TextScriptImporter:", repaired);
        Assert.Contains("assetBundleVariant:", repaired);
        Assert.Equal("0123456789abcdef0123456789abcdef", ReadGuid(validMeta));
        Assert.Equal(brokenGuid, ReadGuid(outsideRootsMeta));
    }

    [Fact]
    public void Repair_InsertsAGuidWhenTheMetaFileHasNoGuidLine()
    {
        using TemporaryDirectory temporary = new();
        string projectRoot = Path.Combine(temporary.Root, "Loopstructor2");
        string missingGuidMeta = Path.Combine(projectRoot, "Packages", "com.example.embedded", "note.md.meta");
        WriteMeta(missingGuidMeta, guid: null, withPreamble: false);

        UnityAssetMetaGuidRepairResult result = UnityAssetMetaGuidRepairer.Repair(projectRoot);

        Assert.Equal(1, result.RepairedCount);
        Assert.Equal(0, result.FailedCount);
        string repaired = File.ReadAllText(missingGuidMeta);
        string[] lines = repaired.Split('\n');
        Assert.Equal("fileFormatVersion: 2", lines[0].TrimEnd('\r'));
        Assert.StartsWith("guid: ", lines[1], StringComparison.Ordinal);
        Assert.Matches("^[0-9a-fA-F]{32}$", lines[1]["guid: ".Length..].TrimEnd('\r'));
        Assert.Contains("TextScriptImporter:", repaired);
    }

    [Fact]
    public void Repair_IsIdempotentAndLeavesAValidProjectUntouched()
    {
        using TemporaryDirectory temporary = new();
        string projectRoot = Path.Combine(temporary.Root, "Loopstructor2");
        string brokenMeta = Path.Combine(projectRoot, "Assets", "记录.md.meta");
        WriteMeta(brokenMeta, "6768053518dd49c48a88c82f8301b832a", withPreamble: false);

        UnityAssetMetaGuidRepairResult first = UnityAssetMetaGuidRepairer.Repair(projectRoot);
        byte[] afterFirst = File.ReadAllBytes(brokenMeta);
        UnityAssetMetaGuidRepairResult second = UnityAssetMetaGuidRepairer.Repair(projectRoot);

        Assert.Equal(1, first.RepairedCount);
        Assert.Equal(0, second.RepairedCount);
        Assert.Equal(0, second.FailedCount);
        Assert.Equal(afterFirst, File.ReadAllBytes(brokenMeta));
    }

    private static void WriteMeta(string path, string? guid, bool withPreamble)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string content = "fileFormatVersion: 2\n";
        if (guid is not null) content += "guid: " + guid + "\n";
        content += "TextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
        File.WriteAllText(path, content, new UTF8Encoding(withPreamble));
    }

    private static string ReadGuid(string path)
    {
        Match match = Regex.Match(File.ReadAllText(path), @"(?m)^guid:[ \t]*([0-9a-fA-F]+)[ \t]*$");
        Assert.True(match.Success, path);
        return match.Groups[1].Value;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "Loopstructor.MetaRepair.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
