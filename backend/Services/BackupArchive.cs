using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KiComLibBackend.Services
{
    public readonly record struct BackupKeyFile(string Name, string SourcePath);

    public sealed class BackupManifest
    {
        public int Format { get; set; } = BackupArchive.InnerFormat;

        public string Product { get; set; } = "KiComLib";

        public string ProductVersion { get; set; } = "";

        public int SchemaVersion { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// A portable backup file. The on-disk layout is little-endian:
    /// magic "KICOMLIBBK" (10 bytes), version uint16, PBKDF2 iterations uint32, salt 16, IV 16,
    /// AES-256-CBC ciphertext, HMAC-SHA256 of the header and ciphertext.
    /// The passphrase is stretched with PBKDF2-SHA256 and split with HKDF into
    /// an encryption key and a MAC key. The MAC is checked before any byte is decrypted.
    /// The plaintext is a length-prefixed manifest, PostgreSQL custom dump, and key files.
    /// </summary>
    public static class BackupArchive
    {
        public const string MagicText = "KICOMLIBBK";
        public const ushort FormatVersion = 1;
        public const int InnerFormat = 1;
        public const int DefaultIterations = 600_000;
        public const int MaxIterations = 1_000_000;
        public const int MinPassphraseLength = 12;
        public const int HeaderLength = 48;
        public const int MacLength = 32;
        public const int MaxManifestBytes = 1024 * 1024;
        public const int MaxKeyFileBytes = 1024 * 1024;
        public const int MaxKeyFiles = 64;

        public const string WrongPassphraseMessage = "Wrong passphrase or damaged backup";
        public const string NotBackupMessage = "The file is not a KiComLib backup";
        public const string UnsupportedVersionMessage = "Unsupported backup version";
        public const string UnsupportedParametersMessage = "Unsupported backup parameters";
        public const string DamagedMessage = "The backup file is damaged";
        public const string NotDumpMessage = "The backup does not contain a PostgreSQL dump";
        public const string InvalidKeyRingMessage = "The backup key ring is invalid";

        private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly byte[] EncryptionInfo = Encoding.UTF8.GetBytes("KiComLib.Backup.Enc.v1");
        private static readonly byte[] MacInfo = Encoding.UTF8.GetBytes("KiComLib.Backup.Mac.v1");

        public static string NormalizePassphrase(string? passphrase)
        {
            string value = (passphrase ?? "").Trim();
            if (value.Length < MinPassphraseLength)
            {
                throw new InvalidOperationException($"Passphrase must be at least {MinPassphraseLength} characters");
            }

            return value;
        }

        public static bool IsSafeKeyName(string name)
        {
            if (name.Length is < 1 or > 80 || name is "." or "..")
            {
                return false;
            }

            foreach (char character in name)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-'))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool HasMagic(string path)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);
                byte[] magic = new byte[MagicText.Length];
                int read = stream.Read(magic, 0, magic.Length);
                return read == magic.Length && Encoding.ASCII.GetString(magic) == MagicText;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public static void Inspect(string path, long maxBytes)
        {
            FileInfo info = new(path);
            if (!info.Exists || info.Length < HeaderLength + MacLength + 16)
            {
                throw new InvalidOperationException(info.Exists && HasMagic(path) ? DamagedMessage : NotBackupMessage);
            }

            if (maxBytes > 0 && info.Length > maxBytes)
            {
                throw new InvalidOperationException($"Backup is larger than {maxBytes / (1024 * 1024)} MB");
            }

            byte[] header = new byte[HeaderLength];
            using (FileStream stream = File.OpenRead(path))
            {
                ReadExact(stream, header);
            }

            ParseHeader(header, out _, out _, out _, out _);
            long cipherLength = info.Length - HeaderLength - MacLength;
            if (cipherLength <= 0 || cipherLength % 16 != 0)
            {
                throw new InvalidOperationException(DamagedMessage);
            }
        }

        public static async Task CreateAsync(
            string destinationPath,
            string dumpPath,
            IReadOnlyList<BackupKeyFile> keyFiles,
            BackupManifest manifest,
            string passphrase,
            CancellationToken cancellationToken,
            int iterations = DefaultIterations)
        {
            if (manifest.Format != InnerFormat)
            {
                throw new InvalidOperationException(UnsupportedVersionMessage);
            }

            ValidateKeyFiles(keyFiles);
            long dumpLength = new FileInfo(dumpPath).Length;
            if (dumpLength < 5)
            {
                throw new InvalidOperationException(NotDumpMessage);
            }

            string normalized = NormalizePassphrase(passphrase);
            try
            {
                await EncryptAsync(destinationPath, normalized, iterations, (output, token) => WritePlaintextAsync(output, dumpPath, dumpLength, keyFiles, manifest, token), cancellationToken);
            }
            catch
            {
                TryDelete(destinationPath);
                throw;
            }
        }

        public static async Task<OpenedBackup> OpenAsync(
            string archivePath,
            string passphrase,
            long maxBytes,
            CancellationToken cancellationToken)
        {
            Inspect(archivePath, maxBytes);
            string normalized = NormalizePassphrase(passphrase);
            byte[] header = new byte[HeaderLength];
            using (FileStream stream = File.OpenRead(archivePath))
            {
                ReadExact(stream, header);
            }

            ParseHeader(header, out _, out int iterations, out byte[] salt, out byte[] iv);
            using DerivedKeys keys = DeriveKeys(normalized, salt, iterations);
            await VerifyMacAsync(archivePath, header, keys.Mac, cancellationToken);

            string work = Path.Combine(Path.GetTempPath(), "kicomlib-restore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                return await ReadPlaintextAsync(archivePath, header, iv, keys.Encryption, work, maxBytes, cancellationToken);
            }
            catch
            {
                TryDeleteDirectory(work);
                throw;
            }
        }

        internal static Task EncryptRawAsync(
            string plaintextPath,
            string destinationPath,
            string passphrase,
            int iterations,
            CancellationToken cancellationToken)
        {
            string normalized = NormalizePassphrase(passphrase);
            return EncryptAsync(
                destinationPath,
                normalized,
                iterations,
                async (output, token) =>
                {
                    await using FileStream plain = File.OpenRead(plaintextPath);
                    await plain.CopyToAsync(output, token);
                },
                cancellationToken);
        }

        private static void ValidateKeyFiles(IReadOnlyList<BackupKeyFile> keyFiles)
        {
            if (keyFiles.Count > MaxKeyFiles)
            {
                throw new InvalidOperationException(InvalidKeyRingMessage);
            }

            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (BackupKeyFile keyFile in keyFiles)
            {
                if (!IsSafeKeyName(keyFile.Name) || !names.Add(keyFile.Name))
                {
                    throw new InvalidOperationException(InvalidKeyRingMessage);
                }

                if (!File.Exists(keyFile.SourcePath) || new FileInfo(keyFile.SourcePath).Length > MaxKeyFileBytes)
                {
                    throw new InvalidOperationException(InvalidKeyRingMessage);
                }
            }
        }

        private static async Task EncryptAsync(
            string destinationPath,
            string passphrase,
            int iterations,
            Func<Stream, CancellationToken, Task> writePlaintext,
            CancellationToken cancellationToken)
        {
            if (iterations < 1 || iterations > MaxIterations)
            {
                throw new InvalidOperationException(UnsupportedParametersMessage);
            }

            byte[] salt = new byte[16];
            byte[] iv = new byte[16];
            RandomNumberGenerator.Fill(salt);
            RandomNumberGenerator.Fill(iv);
            byte[] header = BuildHeader(iterations, salt, iv);
            using DerivedKeys keys = DeriveKeys(passphrase, salt, iterations);
            using HMACSHA256 hmac = new(keys.Mac);

            await using FileStream file = File.Create(destinationPath);
            hmac.TransformBlock(header, 0, header.Length, null, 0);
            await file.WriteAsync(header, cancellationToken);
            using HmacWriteStream hashing = new(file, hmac);
            using Aes aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = keys.Encryption;
            aes.IV = iv;
            using ICryptoTransform encryptor = aes.CreateEncryptor();
            await using (CryptoStream crypto = new(hashing, encryptor, CryptoStreamMode.Write, leaveOpen: true))
            {
                await writePlaintext(crypto, cancellationToken);
            }

            hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            byte[] mac = hmac.Hash ?? throw new InvalidOperationException(DamagedMessage);
            await file.WriteAsync(mac, cancellationToken);
        }

        private static async Task WritePlaintextAsync(
            Stream output,
            string dumpPath,
            long dumpLength,
            IReadOnlyList<BackupKeyFile> keyFiles,
            BackupManifest manifest,
            CancellationToken cancellationToken)
        {
            byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJson);
            if (manifestBytes.Length == 0 || manifestBytes.Length > MaxManifestBytes)
            {
                throw new InvalidOperationException(DamagedMessage);
            }

            await WriteInt32Async(output, manifestBytes.Length, cancellationToken);
            await output.WriteAsync(manifestBytes, cancellationToken);
            await WriteInt64Async(output, dumpLength, cancellationToken);
            await using (FileStream dump = File.OpenRead(dumpPath))
            {
                await CopyExactAsync(dump, output, dumpLength, cancellationToken);
            }

            await WriteInt32Async(output, keyFiles.Count, cancellationToken);
            foreach (BackupKeyFile keyFile in keyFiles)
            {
                byte[] name = Encoding.UTF8.GetBytes(keyFile.Name);
                byte[] data = await File.ReadAllBytesAsync(keyFile.SourcePath, cancellationToken);
                if (name.Length == 0 || name.Length > 80 || data.Length > MaxKeyFileBytes)
                {
                    throw new InvalidOperationException(InvalidKeyRingMessage);
                }

                await WriteUInt16Async(output, (ushort)name.Length, cancellationToken);
                await output.WriteAsync(name, cancellationToken);
                await WriteInt64Async(output, data.Length, cancellationToken);
                await output.WriteAsync(data, cancellationToken);
            }
        }

        private static async Task VerifyMacAsync(string archivePath, byte[] header, byte[] macKey, CancellationToken cancellationToken)
        {
            long length = new FileInfo(archivePath).Length;
            long cipherLength = length - HeaderLength - MacLength;
            byte[] actual = new byte[MacLength];
            using HMACSHA256 hmac = new(macKey);
            hmac.TransformBlock(header, 0, header.Length, null, 0);
            using (FileStream stream = File.OpenRead(archivePath))
            {
                stream.Seek(HeaderLength, SeekOrigin.Begin);
                byte[] buffer = new byte[81920];
                long remaining = cipherLength;
                while (remaining > 0)
                {
                    int requested = (int)Math.Min(buffer.Length, remaining);
                    int read = await stream.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
                    if (read == 0)
                    {
                        throw new InvalidOperationException(DamagedMessage);
                    }

                    hmac.TransformBlock(buffer, 0, read, null, 0);
                    remaining -= read;
                }

                ReadExact(stream, actual);
            }

            hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            if (hmac.Hash == null || !CryptographicOperations.FixedTimeEquals(hmac.Hash, actual))
            {
                throw new InvalidOperationException(WrongPassphraseMessage);
            }
        }

        private static async Task<OpenedBackup> ReadPlaintextAsync(
            string archivePath,
            byte[] header,
            byte[] iv,
            byte[] encryptionKey,
            string work,
            long maxBytes,
            CancellationToken cancellationToken)
        {
            long cipherLength = new FileInfo(archivePath).Length - HeaderLength - MacLength;
            await using FileStream source = File.OpenRead(archivePath);
            source.Seek(HeaderLength, SeekOrigin.Begin);
            using BoundedReadStream bounded = new(source, cipherLength);
            using Aes aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = encryptionKey;
            aes.IV = iv;
            using ICryptoTransform decryptor = aes.CreateDecryptor();
            await using CryptoStream crypto = new(bounded, decryptor, CryptoStreamMode.Read, leaveOpen: true);
            try
            {
                byte[] manifestBytes = await ReadChunkAsync(crypto, await ReadInt32Async(crypto, cancellationToken), MaxManifestBytes, cancellationToken);
                BackupManifest? manifest;
                try
                {
                    manifest = JsonSerializer.Deserialize<BackupManifest>(manifestBytes, ManifestJson);
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException(DamagedMessage);
                }

                if (manifest == null || manifest.Format != InnerFormat)
                {
                    throw new InvalidOperationException(UnsupportedVersionMessage);
                }

                long dumpLength = await ReadInt64Async(crypto, cancellationToken);
                if (dumpLength < 5 || dumpLength > maxBytes)
                {
                    throw new InvalidOperationException(dumpLength > maxBytes ? $"Backup is larger than {maxBytes / (1024 * 1024)} MB" : NotDumpMessage);
                }

                string dumpPath = Path.Combine(work, "database.dump");
                await using (FileStream dump = File.Create(dumpPath))
                {
                    await CopyExactAsync(crypto, dump, dumpLength, cancellationToken);
                }

                if (!HasDumpMagic(dumpPath))
                {
                    throw new InvalidOperationException(NotDumpMessage);
                }

                int keyCount = await ReadInt32Async(crypto, cancellationToken);
                if (keyCount < 0 || keyCount > MaxKeyFiles)
                {
                    throw new InvalidOperationException(InvalidKeyRingMessage);
                }

                string keysDirectory = Path.Combine(work, "keys");
                Directory.CreateDirectory(keysDirectory);
                List<string> keyPaths = new();
                HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
                for (int index = 0; index < keyCount; index++)
                {
                    int nameLength = await ReadUInt16Async(crypto, cancellationToken);
                    byte[] nameBytes = await ReadChunkAsync(crypto, nameLength, 80, cancellationToken);
                    string name = Encoding.UTF8.GetString(nameBytes);
                    if (!IsSafeKeyName(name) || !names.Add(name))
                    {
                        throw new InvalidOperationException(InvalidKeyRingMessage);
                    }

                    long dataLength = await ReadInt64Async(crypto, cancellationToken);
                    if (dataLength < 0 || dataLength > MaxKeyFileBytes)
                    {
                        throw new InvalidOperationException(InvalidKeyRingMessage);
                    }

                    string keyPath = Path.Combine(keysDirectory, name);
                    await using (FileStream key = File.Create(keyPath))
                    {
                        await CopyExactAsync(crypto, key, dataLength, cancellationToken);
                    }

                    keyPaths.Add(keyPath);
                }

                byte[] extra = new byte[1];
                if (await crypto.ReadAsync(extra.AsMemory(0, 1), cancellationToken) != 0)
                {
                    throw new InvalidOperationException(DamagedMessage);
                }

                return new OpenedBackup(work, dumpPath, keyPaths, manifest);
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (CryptographicException)
            {
                throw new InvalidOperationException(DamagedMessage);
            }
            catch (EndOfStreamException)
            {
                throw new InvalidOperationException(DamagedMessage);
            }
        }

        private static bool HasDumpMagic(string path)
        {
            using FileStream stream = File.OpenRead(path);
            byte[] magic = new byte[5];
            return stream.Read(magic, 0, magic.Length) == 5 && Encoding.ASCII.GetString(magic) == "PGDMP";
        }

        private static byte[] BuildHeader(int iterations, byte[] salt, byte[] iv)
        {
            byte[] header = new byte[HeaderLength];
            Encoding.ASCII.GetBytes(MagicText).CopyTo(header, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(MagicText.Length), FormatVersion);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(MagicText.Length + 2), (uint)iterations);
            salt.CopyTo(header.AsSpan(MagicText.Length + 6));
            iv.CopyTo(header.AsSpan(MagicText.Length + 22));
            return header;
        }

        private static void ParseHeader(byte[] header, out ushort version, out int iterations, out byte[] salt, out byte[] iv)
        {
            if (header.Length < HeaderLength || Encoding.ASCII.GetString(header, 0, MagicText.Length) != MagicText)
            {
                throw new InvalidOperationException(NotBackupMessage);
            }

            version = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(MagicText.Length));
            if (version != FormatVersion)
            {
                throw new InvalidOperationException(UnsupportedVersionMessage);
            }

            iterations = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(MagicText.Length + 2));
            if (iterations < 1 || iterations > MaxIterations)
            {
                throw new InvalidOperationException(UnsupportedParametersMessage);
            }

            salt = header.AsSpan(MagicText.Length + 6, 16).ToArray();
            iv = header.AsSpan(MagicText.Length + 22, 16).ToArray();
        }

        private static DerivedKeys DeriveKeys(string passphrase, byte[] salt, int iterations)
        {
            byte[] master = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, 32);
            byte[] encryption = HKDF.DeriveKey(HashAlgorithmName.SHA256, master, 32, info: EncryptionInfo);
            byte[] mac = HKDF.DeriveKey(HashAlgorithmName.SHA256, master, 32, info: MacInfo);
            return new DerivedKeys(master, encryption, mac);
        }

        private static async Task<byte[]> ReadChunkAsync(Stream stream, int length, int maxLength, CancellationToken cancellationToken)
        {
            if (length <= 0 || length > maxLength)
            {
                throw new InvalidOperationException(DamagedMessage);
            }

            byte[] buffer = new byte[length];
            await ReadExactAsync(stream, buffer, cancellationToken);
            return buffer;
        }

        private static async Task<int> ReadInt32Async(Stream stream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[4];
            await ReadExactAsync(stream, buffer, cancellationToken);
            return BinaryPrimitives.ReadInt32LittleEndian(buffer);
        }

        private static async Task<long> ReadInt64Async(Stream stream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8];
            await ReadExactAsync(stream, buffer, cancellationToken);
            return BinaryPrimitives.ReadInt64LittleEndian(buffer);
        }

        private static async Task<ushort> ReadUInt16Async(Stream stream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[2];
            await ReadExactAsync(stream, buffer, cancellationToken);
            return BinaryPrimitives.ReadUInt16LittleEndian(buffer);
        }

        private static Task WriteInt32Async(Stream stream, int value, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            return stream.WriteAsync(buffer, cancellationToken).AsTask();
        }

        private static Task WriteInt64Async(Stream stream, long value, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
            return stream.WriteAsync(buffer, cancellationToken).AsTask();
        }

        private static Task WriteUInt16Async(Stream stream, ushort value, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
            return stream.WriteAsync(buffer, cancellationToken).AsTask();
        }

        private static async Task CopyExactAsync(Stream source, Stream destination, long count, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[81920];
            long remaining = count;
            while (remaining > 0)
            {
                int requested = (int)Math.Min(buffer.Length, remaining);
                int read = await source.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                remaining -= read;
            }
        }

        private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }

                offset += read;
            }
        }

        private static void ReadExact(Stream stream, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }

                offset += read;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // The caller reports the original failure.
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (IOException)
            {
                // The caller reports the original failure.
            }
        }

        private readonly struct DerivedKeys : IDisposable
        {
            public DerivedKeys(byte[] master, byte[] encryption, byte[] mac)
            {
                Master = master;
                Encryption = encryption;
                Mac = mac;
            }

            public byte[] Master { get; }

            public byte[] Encryption { get; }

            public byte[] Mac { get; }

            public void Dispose()
            {
                CryptographicOperations.ZeroMemory(Master);
                CryptographicOperations.ZeroMemory(Encryption);
                CryptographicOperations.ZeroMemory(Mac);
            }
        }

        private sealed class HmacWriteStream : Stream
        {
            private readonly Stream _inner;
            private readonly HMACSHA256 _hmac;

            public HmacWriteStream(Stream inner, HMACSHA256 hmac)
            {
                _inner = inner;
                _hmac = hmac;
            }

            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() => _inner.Flush();

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                Accept(buffer);
                _inner.Write(buffer);
            }

            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                Accept(buffer.Span);
                await _inner.WriteAsync(buffer, cancellationToken);
            }

            protected override void Dispose(bool disposing)
            {
                // The file and HMAC are owned by the caller.
            }

            private void Accept(ReadOnlySpan<byte> buffer)
            {
                if (buffer.IsEmpty)
                {
                    return;
                }

                byte[] copy = ArrayPool<byte>.Shared.Rent(buffer.Length);
                try
                {
                    buffer.CopyTo(copy);
                    _hmac.TransformBlock(copy, 0, buffer.Length, null, 0);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(copy.AsSpan(0, buffer.Length));
                    ArrayPool<byte>.Shared.Return(copy);
                }
            }
        }

        private sealed class BoundedReadStream : Stream
        {
            private readonly Stream _inner;
            private long _remaining;

            public BoundedReadStream(Stream inner, long length)
            {
                _inner = inner;
                _remaining = length;
            }

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

            public override int Read(Span<byte> buffer)
            {
                if (_remaining <= 0)
                {
                    return 0;
                }

                int allowed = (int)Math.Min(buffer.Length, _remaining);
                int read = _inner.Read(buffer[..allowed]);
                if (read > 0)
                {
                    _remaining -= read;
                }

                return read;
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (_remaining <= 0)
                {
                    return 0;
                }

                int allowed = (int)Math.Min(buffer.Length, _remaining);
                int read = await _inner.ReadAsync(buffer[..allowed], cancellationToken);
                if (read > 0)
                {
                    _remaining -= read;
                }

                return read;
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

    public sealed class OpenedBackup : IDisposable
    {
        private readonly string _workDirectory;
        private bool _disposed;

        internal OpenedBackup(string workDirectory, string dumpPath, IReadOnlyList<string> keyFiles, BackupManifest manifest)
        {
            _workDirectory = workDirectory;
            DumpPath = dumpPath;
            KeyFiles = keyFiles;
            Manifest = manifest;
        }

        public string DumpPath { get; }

        public IReadOnlyList<string> KeyFiles { get; }

        public BackupManifest Manifest { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                if (Directory.Exists(_workDirectory))
                {
                    Directory.Delete(_workDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
                // Temporary extract directory.
            }
        }
    }
}
