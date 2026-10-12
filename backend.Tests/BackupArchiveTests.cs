using System.Buffers.Binary;
using System.Text;
using KiComLibBackend.Services;

namespace KiComLibBackend.Tests;

public class BackupArchiveTests
{
    private const string Passphrase = "test-backup-pass";

    [Fact]
    public void Passphrase_MustBeAtLeast12Characters()
    {
        Assert.Equal(600_000, BackupArchive.DefaultIterations);
        Assert.Throws<InvalidOperationException>(() => BackupArchive.NormalizePassphrase("short"));
        Assert.Equal(Passphrase, BackupArchive.NormalizePassphrase("  " + Passphrase + "  "));
    }

    [Fact]
    public async Task RoundTrip_RestoresDumpManifestAndKeys()
    {
        string directory = TempDirectory();
        try
        {
            string dumpPath = Path.Combine(directory, "database.dump");
            string keyPath = Path.Combine(directory, "key-test.xml");
            string archivePath = Path.Combine(directory, "backup.kibkp");
            await File.WriteAllTextAsync(dumpPath, "PGDMP-hello");
            await File.WriteAllTextAsync(keyPath, "secret-key");
            BackupManifest manifest = new()
            {
                Format = BackupArchive.InnerFormat,
                Product = "KiComLib",
                ProductVersion = "test",
                SchemaVersion = 7,
                CreatedAt = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc)
            };

            await BackupArchive.CreateAsync(
                archivePath,
                dumpPath,
                new[] { new BackupKeyFile("key-test.xml", keyPath) },
                manifest,
                "  " + Passphrase + "  ",
                CancellationToken.None,
                iterations: 10_000);

            Assert.True(BackupArchive.HasMagic(archivePath));
            BackupArchive.Inspect(archivePath, 1024 * 1024);
            using OpenedBackup opened = await BackupArchive.OpenAsync(archivePath, Passphrase, 1024 * 1024, CancellationToken.None);
            Assert.Equal("PGDMP-hello", await File.ReadAllTextAsync(opened.DumpPath));
            Assert.Equal(7, opened.Manifest.SchemaVersion);
            Assert.Equal("test", opened.Manifest.ProductVersion);
            string restoredKey = Assert.Single(opened.KeyFiles);
            Assert.Equal("key-test.xml", Path.GetFileName(restoredKey));
            Assert.Equal("secret-key", await File.ReadAllTextAsync(restoredKey));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Open_RejectsWrongPassphraseTamperingAndUnsafeKeyNames()
    {
        string directory = TempDirectory();
        try
        {
            string dumpPath = Path.Combine(directory, "database.dump");
            string keyPath = Path.Combine(directory, "key-test.xml");
            string archivePath = Path.Combine(directory, "backup.kibkp");
            await File.WriteAllTextAsync(dumpPath, "PGDMP-hello");
            await File.WriteAllTextAsync(keyPath, "secret-key");
            await BackupArchive.CreateAsync(
                archivePath,
                dumpPath,
                new[] { new BackupKeyFile("key-test.xml", keyPath) },
                new BackupManifest { Format = BackupArchive.InnerFormat },
                Passphrase,
                CancellationToken.None,
                iterations: 10_000);

            InvalidOperationException wrong = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                BackupArchive.OpenAsync(archivePath, "another-passphrase", 1024 * 1024, CancellationToken.None));
            Assert.Equal(BackupArchive.WrongPassphraseMessage, wrong.Message);

            byte[] tampered = await File.ReadAllBytesAsync(archivePath);
            tampered[60] ^= 0x01;
            string tamperedPath = Path.Combine(directory, "tampered.kibkp");
            await File.WriteAllBytesAsync(tamperedPath, tampered);
            InvalidOperationException damaged = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                BackupArchive.OpenAsync(tamperedPath, Passphrase, 1024 * 1024, CancellationToken.None));
            Assert.Equal(BackupArchive.WrongPassphraseMessage, damaged.Message);

            tampered[BackupArchive.MagicText.Length] = 2;
            string versionPath = Path.Combine(directory, "version.kibkp");
            await File.WriteAllBytesAsync(versionPath, tampered);
            InvalidOperationException version = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                BackupArchive.OpenAsync(versionPath, Passphrase, 1024 * 1024, CancellationToken.None));
            Assert.Equal(BackupArchive.UnsupportedVersionMessage, version.Message);

            string evilName = "evil-" + Guid.NewGuid().ToString("N") + ".xml";
            string hostilePath = Path.Combine(directory, "hostile.kibkp");
            await BackupArchive.EncryptRawAsync(
                await HostilePlaintextAsync(directory, "../" + evilName),
                hostilePath,
                Passphrase,
                10_000,
                CancellationToken.None);
            InvalidOperationException hostile = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                BackupArchive.OpenAsync(hostilePath, Passphrase, 1024 * 1024, CancellationToken.None));
            Assert.Equal(BackupArchive.InvalidKeyRingMessage, hostile.Message);
            Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), evilName)));

            await Assert.ThrowsAsync<InvalidOperationException>(() => BackupArchive.CreateAsync(
                Path.Combine(directory, "rejected.kibkp"),
                dumpPath,
                new[] { new BackupKeyFile("../" + evilName, keyPath) },
                new BackupManifest { Format = BackupArchive.InnerFormat },
                Passphrase,
                CancellationToken.None,
                iterations: 10_000));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<string> HostilePlaintextAsync(string directory, string keyName)
    {
        string path = Path.Combine(directory, "hostile.bin");
        byte[] manifest = Encoding.UTF8.GetBytes("{\"format\":1}");
        byte[] dump = Encoding.ASCII.GetBytes("PGDMP-hello");
        byte[] name = Encoding.UTF8.GetBytes(keyName);
        byte[] data = Encoding.UTF8.GetBytes("nope");
        await using FileStream stream = File.Create(path);
        await WriteInt32Async(stream, manifest.Length);
        await stream.WriteAsync(manifest);
        await WriteInt64Async(stream, dump.Length);
        await stream.WriteAsync(dump);
        await WriteInt32Async(stream, 1);
        await WriteUInt16Async(stream, (ushort)name.Length);
        await stream.WriteAsync(name);
        await WriteInt64Async(stream, data.Length);
        await stream.WriteAsync(data);
        return path;
    }

    private static Task WriteInt32Async(Stream stream, int value)
    {
        byte[] buffer = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        return stream.WriteAsync(buffer).AsTask();
    }

    private static Task WriteInt64Async(Stream stream, long value)
    {
        byte[] buffer = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        return stream.WriteAsync(buffer).AsTask();
    }

    private static Task WriteUInt16Async(Stream stream, ushort value)
    {
        byte[] buffer = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        return stream.WriteAsync(buffer).AsTask();
    }

    private static string TempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "kicomlib-archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
