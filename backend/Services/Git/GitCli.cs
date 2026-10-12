using System.Text;

namespace KiComLibBackend.Services
{
    public sealed class GitAuth
    {
        public string? Username { get; init; }

        public string? HttpsToken { get; init; }

        public string? SshPrivateKey { get; init; }

        public string? KnownHosts { get; init; }

        public string? CaCertificate { get; init; }

        public bool AllowHttp { get; init; }

        public bool AllowFile { get; init; }
    }

    public sealed class GitInvocation : IDisposable
    {
        public string FileName { get; init; } = "git";

        public List<string> Arguments { get; } = new();

        public Dictionary<string, string> Environment { get; } = new(StringComparer.Ordinal);

        public List<string> TempFiles { get; } = new();

        public List<string> Secrets { get; } = new();

        public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

        public void Dispose()
        {
            foreach (string path in TempFiles)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception)
                {
                    // Temporary key material is removed on a best-effort basis.
                }
            }
        }
    }

    public sealed class LsRemoteResult
    {
        public string? HeadTarget { get; init; }

        public Dictionary<string, string> Refs { get; init; } = new(StringComparer.Ordinal);
    }

    public sealed class GitCommitInfo
    {
        public string Sha { get; init; } = "";

        public string[] Parents { get; init; } = Array.Empty<string>();

        public DateTime CommittedAt { get; init; }

        public string AuthorName { get; init; } = "";

        public string? AuthorEmail { get; init; }

        public string Message { get; init; } = "";
    }

    /// <summary>
    /// Builds git commands so an HTTPS token is only placed in http.extraHeader via the environment.
    /// </summary>
    public class GitCli
    {
        public static readonly TimeSpan CloneTimeout = TimeSpan.FromMinutes(15);

        public static readonly TimeSpan FetchTimeout = TimeSpan.FromMinutes(5);

        private readonly ProcessRunner _processes;
        private readonly string _reposDirectory;
        private readonly ILogger<GitCli> _logger;

        public GitCli(ProcessRunner processes, IWebHostEnvironment environment, ILogger<GitCli> logger)
        {
            _processes = processes;
            _logger = logger;
            _reposDirectory = DataPaths.Resolve(environment.ContentRootPath, "GIT_REPOS_DIR", "Data", "Repos");
            Directory.CreateDirectory(_reposDirectory);
            Directory.CreateDirectory(HomeDirectory);
        }

        public string ReposDirectory => _reposDirectory;

        public string HomeDirectory => Path.Combine(_reposDirectory, ".home");

        public string MirrorPath(Guid repositoryUuid)
        {
            return Path.Combine(_reposDirectory, repositoryUuid.ToString("D") + ".git");
        }

        public GitInvocation Prepare(IReadOnlyList<string> gitArguments, GitAuth? auth, TimeSpan timeout)
        {
            GitAuth effective = auth ?? new GitAuth();
            if (!string.IsNullOrEmpty(effective.HttpsToken))
            {
                foreach (string argument in gitArguments)
                {
                    if (argument.Contains(effective.HttpsToken, StringComparison.Ordinal))
                    {
                        throw new GitAccessException("Refusing to pass an HTTPS token on the git command line.");
                    }
                }
            }

            GitInvocation invocation = new() { Timeout = timeout };
            invocation.Arguments.Add("-c");
            invocation.Arguments.Add("protocol.allow=never");
            invocation.Arguments.Add("-c");
            invocation.Arguments.Add("protocol.https.allow=always");
            invocation.Arguments.Add("-c");
            invocation.Arguments.Add("protocol.ssh.allow=always");
            if (effective.AllowHttp)
            {
                invocation.Arguments.Add("-c");
                invocation.Arguments.Add("protocol.http.allow=always");
            }

            if (effective.AllowFile || ArgumentsUseFile(gitArguments))
            {
                if (!GitUrlPolicy.FileProtocolAllowed)
                {
                    throw new GitAccessException("file:// clone URLs are disabled.");
                }

                invocation.Arguments.Add("-c");
                invocation.Arguments.Add("protocol.file.allow=always");
            }

            invocation.Arguments.Add("-c");
            invocation.Arguments.Add("core.hooksPath=/dev/null");
            string safeRoot = _reposDirectory.Replace('\\', '/').TrimEnd('/');
            invocation.Arguments.Add("-c");
            invocation.Arguments.Add("safe.directory=" + safeRoot);
            invocation.Arguments.Add("-c");
            invocation.Arguments.Add("safe.directory=" + safeRoot + "/*");
            invocation.Arguments.AddRange(gitArguments);

            invocation.Environment["GIT_TERMINAL_PROMPT"] = "0";
            invocation.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            invocation.Environment["GIT_LFS_SKIP_SMUDGE"] = "1";
            invocation.Environment["HOME"] = HomeDirectory;
            invocation.Environment["GIT_CONFIG_COUNT"] = "0";

            if (!string.IsNullOrEmpty(effective.HttpsToken))
            {
                string username = effective.Username ?? "";
                string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + effective.HttpsToken));
                invocation.Environment["GIT_CONFIG_COUNT"] = "1";
                invocation.Environment["GIT_CONFIG_KEY_0"] = "http.extraHeader";
                invocation.Environment["GIT_CONFIG_VALUE_0"] = "Authorization: Basic " + basic;
                invocation.Secrets.Add(effective.HttpsToken);
                invocation.Secrets.Add(basic);
            }

            if (!string.IsNullOrEmpty(effective.CaCertificate))
            {
                string caPath = NewTempFile("kicomlib-ca-");
                File.WriteAllText(caPath, effective.CaCertificate);
                invocation.TempFiles.Add(caPath);
                invocation.Environment["GIT_SSL_CAINFO"] = caPath;
            }

            if (!string.IsNullOrEmpty(effective.SshPrivateKey))
            {
                string keyPath = NewTempFile("kicomlib-key-");
                File.WriteAllText(keyPath, effective.SshPrivateKey);
                TryRestrict(keyPath);
                invocation.TempFiles.Add(keyPath);
                string knownPath = NewTempFile("kicomlib-known-");
                File.WriteAllText(knownPath, effective.KnownHosts ?? "");
                TryRestrict(knownPath);
                invocation.TempFiles.Add(knownPath);
                invocation.Environment["GIT_SSH_COMMAND"] =
                    "ssh -i \"" + keyPath + "\" -o IdentitiesOnly=yes -o BatchMode=yes -o StrictHostKeyChecking=yes -o UserKnownHostsFile=\"" + knownPath + "\"";
                invocation.Secrets.Add(effective.SshPrivateKey);
            }

            return invocation;
        }

        public async Task<ProcessRunResult> RunAsync(GitInvocation invocation, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Running git {Arguments}", GitText.Scrub(string.Join(" ", invocation.Arguments), invocation.Secrets));
            ProcessRunResult result = await _processes.RunAsync(
                invocation.FileName,
                invocation.Arguments,
                invocation.Environment,
                cancellationToken,
                invocation.Timeout,
                maxOutputChars: 200_000);
            return new ProcessRunResult
            {
                ExitCode = result.ExitCode,
                Stdout = GitText.Scrub(result.Stdout, invocation.Secrets),
                Stderr = GitText.Scrub(result.Stderr, invocation.Secrets)
            };
        }

        public async Task<bool> LfsAvailableAsync(CancellationToken cancellationToken)
        {
            ProcessRunResult result = await _processes.RunAsync(
                "git",
                new[] { "lfs", "version" },
                new Dictionary<string, string> { ["GIT_TERMINAL_PROMPT"] = "0" },
                cancellationToken,
                TimeSpan.FromSeconds(20));
            return result.ExitCode == 0;
        }

        /// <summary>
        /// Runs git-lfs with smudge enabled. GitCli.Prepare always sets GIT_LFS_SKIP_SMUDGE=1.
        /// </summary>
        public Task<ProcessRunResult> RunLfsAsync(string workingDirectory, IReadOnlyList<string> args, CancellationToken cancellationToken)
        {
            Dictionary<string, string> environment = new(StringComparer.Ordinal)
            {
                ["GIT_TERMINAL_PROMPT"] = "0",
                ["GIT_LFS_SKIP_SMUDGE"] = "0",
                ["GIT_CONFIG_NOSYSTEM"] = "1",
                ["HOME"] = HomeDirectory
            };
            return _processes.RunAsync("git", args, environment, cancellationToken, FetchTimeout, workingDirectory: workingDirectory);
        }

        public static string UrlForGit(string cloneUrl, GitAuth auth)
        {
            if (string.IsNullOrEmpty(auth.HttpsToken))
            {
                return cloneUrl;
            }

            string username = string.IsNullOrWhiteSpace(auth.Username) ? "oauth2" : auth.Username;
            return GitUrlPolicy.WithHttpsUser(cloneUrl, username);
        }

        public async Task<LsRemoteResult> LsRemoteAsync(string cloneUrl, GitAuth auth, CancellationToken cancellationToken)
        {
            string url = UrlForGit(cloneUrl, auth);
            GitInvocation head = Prepare(new[] { "ls-remote", "--symref", url, "HEAD" }, auth, FetchTimeout);
            GitInvocation refs;
            ProcessRunResult headResult;
            ProcessRunResult refsResult;
            using (head)
            {
                headResult = await RunAsync(head, cancellationToken);
            }

            if (headResult.ExitCode != 0)
            {
                throw new GitCommandException(headResult.Stderr, headResult.ExitCode);
            }

            refs = Prepare(new[] { "ls-remote", "--heads", "--tags", url }, auth, FetchTimeout);
            using (refs)
            {
                refsResult = await RunAsync(refs, cancellationToken);
            }

            if (refsResult.ExitCode != 0)
            {
                throw new GitCommandException(refsResult.Stderr, refsResult.ExitCode);
            }

            LsRemoteResult parsed = ParseLsRemote(refsResult.Stdout);
            LsRemoteResult headParsed = ParseLsRemote(headResult.Stdout);
            string? branch = headParsed.HeadTarget;
            if (string.IsNullOrWhiteSpace(branch) || !parsed.Refs.ContainsKey("refs/heads/" + branch))
            {
                branch = InferDefaultBranch(parsed.Refs);
            }

            return new LsRemoteResult
            {
                HeadTarget = branch,
                Refs = parsed.Refs
            };
        }

        public static string? InferDefaultBranch(IReadOnlyDictionary<string, string> refs)
        {
            List<string> heads = refs.Keys
                .Where(name => name.StartsWith("refs/heads/", StringComparison.Ordinal))
                .Select(name => name["refs/heads/".Length..])
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            if (heads.Count == 0)
            {
                return null;
            }

            if (heads.Contains("main", StringComparer.Ordinal))
            {
                return "main";
            }

            if (heads.Contains("master", StringComparer.Ordinal))
            {
                return "master";
            }

            return heads[0];
        }

        public static LsRemoteResult ParseLsRemote(string stdout)
        {
            string? head = null;
            Dictionary<string, string> refs = new(StringComparer.Ordinal);
            foreach (string raw in stdout.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith("ref:", StringComparison.Ordinal))
                {
                    int tab = line.IndexOf('\t');
                    string left = tab >= 0 ? line[..tab] : line;
                    string target = left["ref:".Length..].Trim();
                    if (target.StartsWith("refs/heads/", StringComparison.Ordinal))
                    {
                        head = target["refs/heads/".Length..];
                    }

                    continue;
                }

                int split = line.IndexOf('\t');
                if (split <= 0)
                {
                    continue;
                }

                string sha = line[..split].Trim();
                string name = line[(split + 1)..].Trim();
                if (name.Length == 0 || name == "HEAD" || name.EndsWith("^{}", StringComparison.Ordinal))
                {
                    continue;
                }

                refs[name] = sha;
            }

            return new LsRemoteResult { HeadTarget = head, Refs = refs };
        }

        public async Task<(string PrivateKey, string PublicKey)> GenerateEd25519Async(CancellationToken cancellationToken)
        {
            string path = NewTempFile("kicomlib-gen-");
            try
            {
                ProcessRunResult result = await _processes.RunAsync(
                    "ssh-keygen",
                    new[] { "-t", "ed25519", "-N", "", "-C", "kicomlib", "-f", path },
                    null,
                    cancellationToken,
                    TimeSpan.FromSeconds(30));
                if (result.ExitCode != 0)
                {
                    throw new GitCommandException(string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr, result.ExitCode);
                }

                string privateKey = await File.ReadAllTextAsync(path, cancellationToken);
                string publicKey = (await File.ReadAllTextAsync(path + ".pub", cancellationToken)).Trim();
                return (privateKey, publicKey);
            }
            finally
            {
                TryDelete(path);
                TryDelete(path + ".pub");
            }
        }

        public async Task<string> KeyScanAsync(string host, int port, CancellationToken cancellationToken)
        {
            List<string> args = new() { "-T", "5" };
            if (port > 0 && port != 22)
            {
                args.Add("-p");
                args.Add(port.ToString());
            }

            args.Add(host);
            ProcessRunResult result = await _processes.RunAsync(
                "ssh-keyscan",
                args,
                null,
                cancellationToken,
                TimeSpan.FromSeconds(20));
            if (string.IsNullOrWhiteSpace(result.Stdout))
            {
                throw new GitCommandException(string.IsNullOrWhiteSpace(result.Stderr) ? "ssh-keyscan returned no keys." : result.Stderr, result.ExitCode);
            }

            return result.Stdout.Trim();
        }

        public static IReadOnlyList<string> FingerprintsOfKnownHosts(string knownHosts)
        {
            List<string> fingerprints = new();
            foreach (string raw in knownHosts.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('|'))
                {
                    continue;
                }

                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                {
                    continue;
                }

                try
                {
                    fingerprints.Add(parts[0] + " " + parts[1] + " " + GitText.OpenSshFingerprint(parts[1] + " " + parts[2]));
                }
                catch (FormatException)
                {
                    // Skip comment lines that are not keys.
                }
            }

            return fingerprints;
        }

        private static bool ArgumentsUseFile(IReadOnlyList<string> gitArguments)
        {
            foreach (string argument in gitArguments)
            {
                if (argument.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NewTempFile(string prefix)
        {
            return Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        }

        private static void TryRestrict(string path)
        {
            try
            {
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }
            catch (Exception)
            {
                // Windows has no 0600 mode bit. The file is still deleted after the call.
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
            catch (Exception)
            {
            }
        }
    }

    public sealed class GitCommandException : Exception
    {
        public int ExitCode { get; }

        public GitCommandException(string message, int exitCode) : base(message)
        {
            ExitCode = exitCode;
        }
    }
}
