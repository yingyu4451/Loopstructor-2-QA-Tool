using System.Security.Cryptography;
using System.Text.Json;
using Loopstructor.AutoPlayer.Updater.Models;
using Loopstructor.AutoPlayer.Updater.Services;

namespace Loopstructor.AutoPlayer.Tests;

public sealed class TransactionalInstallerRecoveryTests
{
    [Fact]
    public void RecoverIncomplete_ReportsCleanupPendingInsteadOfRawFileLockFailure()
    {
        using InstallerFixture fixture = new();
        string target = fixture.CreateRelease("release", "0.6.26");
        string backup = Path.Combine(
            fixture.Root,
            ".LoopstructorAutoPlayer-rollback-20260824-120000-12345678");
        string staging = Path.Combine(
            fixture.Root,
            ".LoopstructorAutoPlayer-staging-0123456789abcdef0123456789abcdef");
        Directory.CreateDirectory(backup);
        Directory.CreateDirectory(staging);
        string journalPath = fixture.WriteJournal(target, backup, staging, "0.6.26");
        using FileStream locked = new(
            Path.Combine(backup, "locked.dat"),
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        locked.WriteByte(0x01);
        locked.Flush();

        TransactionalInstaller installer = new(journalPath: journalPath);
        IOException exception = Assert.Throws<IOException>(() => installer.RecoverIncomplete(target));

        Assert.Contains("临时回滚目录尚未清理完成", exception.Message, StringComparison.Ordinal);
        using JsonDocument journal = JsonDocument.Parse(File.ReadAllText(journalPath));
        JsonElement phase = journal.RootElement
            .EnumerateObject()
            .Single(property => string.Equals(property.Name, "phase", StringComparison.OrdinalIgnoreCase))
            .Value;
        Assert.Equal("cleanup-pending", phase.GetString());
        Assert.True(Directory.Exists(backup));
    }

    [Fact]
    public void Apply_RemovesGeneratedStagingDirectoryWhenTheStagedReleaseIsRejected()
    {
        using InstallerFixture fixture = new();
        string target = fixture.CreateRelease("release", "0.6.26");
        TransactionalInstaller installer = new(journalPath: Path.Combine(fixture.Root, "transaction.json"));
        string staging = installer.CreateStagingRoot(target);
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "left-over.txt"), "incomplete release");

        Assert.Throws<InvalidDataException>(() => installer.Apply(staging, target, "0.6.27"));

        Assert.False(Directory.Exists(staging));
        Assert.Equal("0.6.26", ReleasePackageValidator.ReadMarker(target).Version);
        Assert.False(File.Exists(installer.JournalPath));
    }

    [Fact]
    public void RecoverIncomplete_RemovesAbandonedStagingDirectoryForAnUnchangedTarget()
    {
        using InstallerFixture fixture = new();
        string target = fixture.CreateRelease("release", "0.6.26");
        string backup = Path.Combine(
            fixture.Root,
            ".LoopstructorAutoPlayer-rollback-20260824-120001-87654321");
        string staging = Path.Combine(
            fixture.Root,
            ".LoopstructorAutoPlayer-staging-fedcba9876543210fedcba9876543210");
        Directory.CreateDirectory(Path.Combine(staging, "obsolete"));
        File.WriteAllText(Path.Combine(staging, "obsolete", "payload.txt"), "stale release");
        string journalPath = fixture.WriteJournal(target, backup, staging, "0.6.27");

        string message = new TransactionalInstaller(journalPath: journalPath).RecoverIncomplete(target);

        Assert.Contains("已清理在替换开始前", message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(staging));
        Assert.True(Directory.Exists(target));
        Assert.False(File.Exists(journalPath));
    }

    private sealed class InstallerFixture : IDisposable
    {
        public InstallerFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "autoplayer-installer-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string CreateRelease(string name, string version)
        {
            string root = Path.Combine(Root, name);
            WriteFile(root, "Loopstructor-2-QA-Tool.exe", "launcher-" + version);
            WriteFile(root, "manager", "Loopstructor-2-QA-Tool.exe", "manager-" + version);
            WriteFile(root, "manager", "Loopstructor.AutoPlayer.Updater.exe", "updater-" + version);
            WriteFile(root, "payload", "bepinex", "winhttp.dll", "loader");
            WriteFile(root, "payload", "bepinex", "doorstop_config.ini", "enabled=true");
            WriteFile(root, "payload", "bepinex", "BepInEx", "core", "BepInEx.dll", "5.4.23.5");
            WriteFile(root, "payload", "bepinex", "BepInEx", "core", "BepInEx.Preloader.dll", "5.4.23.5");
            WriteFile(root, "payload", "plugin", "Loopstructor.AutoPlayer.Plugin.dll", "plugin-" + version);
            WriteFile(root, "payload", "plugin", "Loopstructor.AutoPlayer.Core.dll", "core-" + version);
            File.WriteAllText(
                Path.Combine(root, "autoplayer-release.json"),
                JsonSerializer.Serialize(new ReleaseMarker
                {
                    SchemaVersion = 2,
                    Version = version,
                    BepInExVersion = "5.4.23.5",
                    ManagerPath = "Loopstructor-2-QA-Tool.exe",
                    UpdaterPath = "manager/Loopstructor.AutoPlayer.Updater.exe",
                    BepInExPayloadPath = "payload/bepinex",
                    PluginPayloadPath = "payload/plugin"
                }));
            WriteChecksums(root);
            return root;
        }

        public string WriteJournal(string target, string backup, string staging, string version)
        {
            string journalPath = Path.Combine(Root, "transaction-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(
                journalPath,
                JsonSerializer.Serialize(new UpdateTransactionJournal
                {
                    TransactionId = Guid.NewGuid().ToString("N"),
                    TargetRoot = target,
                    BackupRoot = backup,
                    StagingRoot = staging,
                    Version = version,
                    Phase = "prepared",
                    UpdatedAtUtc = DateTime.UtcNow
                }));
            return journalPath;
        }

        public void WriteFile(string root, params string[] pathAndContent)
        {
            string content = pathAndContent[^1];
            string[] path = pathAndContent[..^1];
            string file = Path.Combine(new[] { root }.Concat(path).ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort test cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort test cleanup.
            }
        }

        private static void WriteChecksums(string root)
        {
            string checksumPath = Path.Combine(root, "checksums.sha256");
            if (File.Exists(checksumPath)) File.Delete(checksumPath);
            string[] lines = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path =>
                {
                    string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
                    return hash + "  " + Path.GetRelativePath(root, path).Replace('\\', '/');
                })
                .ToArray();
            File.WriteAllText(checksumPath, string.Join('\n', lines) + "\n");
        }
    }
}
