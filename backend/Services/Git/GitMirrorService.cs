using System.Collections.Concurrent;
using System.Formats.Tar;
using KiComLibBackend.Models;

namespace KiComLibBackend.Services
{
    public sealed class MirrorSyncResult
    {
        public bool Fetched { get; init; }

        public bool Unchanged { get; init; }

        public string? DefaultBranch { get; init; }

        public Dictionary<string, string> Refs { get; init; } = new(StringComparer.Ordinal);
    }

    public class GitMirrorService
    {
        private readonly GitCli _git;
        private readonly ConcurrentDictionary<int, SemaphoreSlim> _gates = new();

        public GitMirrorService(GitCli git)
        {
            _git = git;
        }

        public string MirrorPath(GitRepository repository) => _git.MirrorPath(repository.Uuid);

        public async Task<T> LockAsync<T>(int repositoryId, Func<Task<T>> action, CancellationToken cancellationToken)
        {
            SemaphoreSlim gate = _gates.GetOrAdd(repositoryId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await action();
            }
            finally
            {
                gate.Release();
            }
        }

        public Task LockAsync(int repositoryId, Func<Task> action, CancellationToken cancellationToken)
        {
            return LockAsync(repositoryId, async () =>
            {
                await action();
                return true;
            }, cancellationToken);
        }

        public async Task<MirrorSyncResult> SyncAsync(
            GitRepository repository,
            GitAuth auth,
            IReadOnlyDictionary<string, string> previousRefs,
            CancellationToken cancellationToken)
        {
            string mirror = MirrorPath(repository);
            await EnsureMirrorAsync(mirror, repository.CloneUrl, auth, cancellationToken);
            LsRemoteResult remote = await _git.LsRemoteAsync(repository.CloneUrl, auth, cancellationToken);
            bool unchanged = RefsEqual(previousRefs, remote.Refs);
            if (!unchanged)
            {
                await FetchAsync(mirror, auth, mirrorHasHistory: previousRefs.Count > 0, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(remote.HeadTarget))
            {
                await GitConfigAsync(
                    mirror,
                    auth,
                    new[] { "symbolic-ref", "HEAD", "refs/heads/" + remote.HeadTarget },
                    cancellationToken,
                    allowFailure: true);
            }

            return new MirrorSyncResult
            {
                Fetched = !unchanged,
                Unchanged = unchanged,
                DefaultBranch = remote.HeadTarget,
                Refs = remote.Refs
            };
        }

        public async Task<IReadOnlyList<string>> ListProjectFilesAsync(GitRepository repository, CancellationToken cancellationToken)
        {
            string mirror = MirrorPath(repository);
            if (!Directory.Exists(mirror))
            {
                return Array.Empty<string>();
            }

            string branch = string.IsNullOrWhiteSpace(repository.DefaultBranch)
                ? "HEAD"
                : "refs/heads/" + repository.DefaultBranch;
            GitInvocation invocation = _git.Prepare(
                new[] { "-C", mirror, "ls-tree", "-r", "--name-only", branch },
                new GitAuth { AllowFile = GitUrlPolicy.FileProtocolAllowed },
                TimeSpan.FromMinutes(2));
            using (invocation)
            {
                ProcessRunResult result = await _git.RunAsync(invocation, cancellationToken);
                if (result.ExitCode != 0)
                {
                    return Array.Empty<string>();
                }

                return result.Stdout
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(path => path.EndsWith(".kicad_pro", StringComparison.OrdinalIgnoreCase))
                    .Select(path => path.Replace('\\', '/'))
                    .ToList();
            }
        }

        public async Task<IReadOnlyList<GitCommitInfo>> LogAsync(
            GitRepository repository,
            string range,
            string? path,
            CancellationToken cancellationToken,
            int? limit = null)
        {
            string mirror = MirrorPath(repository);
            List<string> args = new()
            {
                "-C", mirror, "log", "--first-parent", "--format=%H%x1f%P%x1f%aI%x1f%an%x1f%ae%x1f%s"
            };
            if (limit is > 0)
            {
                args.Add("-n");
                args.Add(limit.Value.ToString());
            }

            args.Add(range);
            if (!string.IsNullOrWhiteSpace(path))
            {
                args.Add("--");
                args.Add(path);
            }

            GitInvocation invocation = _git.Prepare(args, new GitAuth { AllowFile = GitUrlPolicy.FileProtocolAllowed }, GitCli.FetchTimeout);
            using (invocation)
            {
                ProcessRunResult result = await _git.RunAsync(invocation, cancellationToken);
                if (result.ExitCode != 0)
                {
                    throw new GitCommandException(result.Stderr, result.ExitCode);
                }

                return ParseLog(result.Stdout);
            }
        }

        public async Task<GitCommitInfo?> ShowAsync(GitRepository repository, string sha, CancellationToken cancellationToken)
        {
            string mirror = MirrorPath(repository);
            GitInvocation invocation = _git.Prepare(
                new[] { "-C", mirror, "log", "-1", "--format=%H%x1f%P%x1f%aI%x1f%an%x1f%ae%x1f%s", sha },
                new GitAuth { AllowFile = GitUrlPolicy.FileProtocolAllowed },
                GitCli.FetchTimeout);
            using (invocation)
            {
                ProcessRunResult result = await _git.RunAsync(invocation, cancellationToken);
                if (result.ExitCode != 0)
                {
                    throw new GitCommandException(result.Stderr, result.ExitCode);
                }

                IReadOnlyList<GitCommitInfo> commits = ParseLog(result.Stdout);
                return commits.Count == 0 ? null : commits[0];
            }
        }

        public async Task<byte[]> ArchiveAsync(
            GitRepository repository,
            string commitSha,
            string? path,
            int maxMegabytes,
            CancellationToken cancellationToken)
        {
            string mirror = MirrorPath(repository);
            if (!Directory.Exists(mirror))
            {
                throw new GitCommandException("The git mirror is not available.", 1);
            }

            string output = Path.Combine(Path.GetTempPath(), "kicomlib-archive-" + Guid.NewGuid().ToString("N") + ".tar");
            try
            {
                List<string> args = new()
                {
                    "-C", mirror, "archive", "--format=tar", "-o", output, commitSha
                };
                if (!string.IsNullOrWhiteSpace(path))
                {
                    args.Add("--");
                    args.Add(path.Replace('\\', '/'));
                }

                GitInvocation invocation = _git.Prepare(args, new GitAuth { AllowFile = GitUrlPolicy.FileProtocolAllowed }, GitCli.FetchTimeout);
                using (invocation)
                {
                    ProcessRunResult result = await _git.RunAsync(invocation, cancellationToken);
                    if (result.ExitCode != 0 || !File.Exists(output))
                    {
                        throw new GitCommandException(string.IsNullOrWhiteSpace(result.Stderr) ? "git archive failed." : result.Stderr, result.ExitCode);
                    }
                }

                FileInfo info = new(output);
                long maxBytes = Math.Max(1, maxMegabytes) * 1024L * 1024L;
                if (info.Length > maxBytes)
                {
                    throw new GitCommandException("Project archive exceeds " + maxMegabytes.ToString() + " MB.", 1);
                }

                return await File.ReadAllBytesAsync(output, cancellationToken);
            }
            finally
            {
                try
                {
                    if (File.Exists(output))
                    {
                        File.Delete(output);
                    }
                }
                catch (Exception)
                {
                    // The temporary archive is removed on a best-effort basis.
                }
            }
        }

        /// <summary>
        /// git archive, then a best-effort git lfs pull of the same tree when the repository has LFS enabled.
        /// A missing git-lfs binary does not fail the archive. The warning is set only when a model file is still a pointer.
        /// </summary>
        public Task<(byte[] Archive, string? Warning)> ArchiveForOutputsAsync(
            GitRepository repository,
            string commitSha,
            string? path,
            int maxMegabytes,
            CancellationToken cancellationToken)
        {
            return LockAsync(
                repository.Id,
                () => ArchiveForOutputsUnlockedAsync(repository, commitSha, path, maxMegabytes, cancellationToken),
                cancellationToken);
        }

        private async Task<(byte[] Archive, string? Warning)> ArchiveForOutputsUnlockedAsync(
            GitRepository repository,
            string commitSha,
            string? path,
            int maxMegabytes,
            CancellationToken cancellationToken)
        {
            byte[] archived = await ArchiveAsync(repository, commitSha, path, maxMegabytes, cancellationToken);
            if (!repository.LfsEnabled)
            {
                return (archived, null);
            }

            bool pointer = ProjectExport.ContainsLfsModelPointer(archived);
            if (!await _git.LfsAvailableAsync(cancellationToken))
            {
                return (archived, pointer
                    ? "git-lfs is not installed; a 3D model file is still an LFS pointer."
                    : null);
            }

            try
            {
                byte[] enriched = await ArchiveAfterLfsPullAsync(repository, commitSha, path, maxMegabytes, cancellationToken);
                return (enriched, ProjectExport.ContainsLfsModelPointer(enriched)
                    ? "A 3D model file is still an LFS pointer after git lfs pull."
                    : null);
            }
            catch (Exception exception) when (exception is GitCommandException or IOException or InvalidOperationException)
            {
                if (!pointer)
                {
                    return (archived, null);
                }

                string detail = exception.Message.Trim();
                if (detail.Length > 300)
                {
                    detail = detail[..300];
                }

                return (archived, string.IsNullOrWhiteSpace(detail)
                    ? "git lfs pull failed; a 3D model file is still an LFS pointer."
                    : "git lfs pull failed: " + detail);
            }
        }

        private async Task<byte[]> ArchiveAfterLfsPullAsync(
            GitRepository repository,
            string commitSha,
            string? path,
            int maxMegabytes,
            CancellationToken cancellationToken)
        {
            string mirror = MirrorPath(repository);
            string work = Path.Combine(Path.GetTempPath(), "kicomlib-lfs-" + Guid.NewGuid().ToString("N"));
            try
            {
                GitInvocation add = _git.Prepare(
                    new[] { "-C", mirror, "worktree", "add", "--detach", work, commitSha },
                    new GitAuth { AllowFile = GitUrlPolicy.FileProtocolAllowed },
                    GitCli.FetchTimeout);
                using (add)
                {
                    ProcessRunResult added = await _git.RunAsync(add, cancellationToken);
                    if (added.ExitCode != 0)
                    {
                        throw new GitCommandException(string.IsNullOrWhiteSpace(added.Stderr) ? "git worktree add failed." : added.Stderr, added.ExitCode);
                    }
                }

                List<string> lfsArgs = new() { "lfs", "pull" };
                if (!string.IsNullOrWhiteSpace(path))
                {
                    lfsArgs.Add("--include=" + path.Replace('\\', '/').Trim('/') + "/**");
                }

                ProcessRunResult pulled = await _git.RunLfsAsync(work, lfsArgs, cancellationToken);
                if (pulled.ExitCode != 0)
                {
                    throw new GitCommandException(string.IsNullOrWhiteSpace(pulled.Stderr) ? "git lfs pull failed." : pulled.Stderr, pulled.ExitCode);
                }

                return await TarWorktreeAsync(work, path, maxMegabytes, cancellationToken);
            }
            finally
            {
                try
                {
                    GitInvocation remove = _git.Prepare(
                        new[] { "-C", mirror, "worktree", "remove", "--force", work },
                        new GitAuth { AllowFile = GitUrlPolicy.FileProtocolAllowed },
                        TimeSpan.FromMinutes(1));
                    using (remove)
                    {
                        await _git.RunAsync(remove, CancellationToken.None);
                    }
                }
                catch (Exception)
                {
                    // The worktree is removed on a best-effort basis.
                }

                try
                {
                    if (Directory.Exists(work))
                    {
                        Directory.Delete(work, true);
                    }
                }
                catch (Exception)
                {
                    // The temporary checkout is removed on a best-effort basis.
                }
            }
        }

        private static async Task<byte[]> TarWorktreeAsync(string work, string? path, int maxMegabytes, CancellationToken cancellationToken)
        {
            string source = work;
            if (!string.IsNullOrWhiteSpace(path))
            {
                source = Path.Combine(work, path.Replace('\\', '/').Trim('/').Replace('/', Path.DirectorySeparatorChar));
            }

            if (!Directory.Exists(source))
            {
                throw new GitCommandException("The project path was not in the commit.", 1);
            }

            List<string> files = new();
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(work, file).Replace('\\', '/');
                if (relative == ".git" || relative.StartsWith(".git/", StringComparison.Ordinal))
                {
                    continue;
                }

                files.Add(file);
            }

            long total = 0;
            foreach (string file in files)
            {
                total += new FileInfo(file).Length;
            }

            long maxBytes = Math.Max(1, maxMegabytes) * 1024L * 1024L;
            if (total > maxBytes)
            {
                throw new GitCommandException("Project archive exceeds " + maxMegabytes.ToString() + " MB.", 1);
            }

            string output = Path.Combine(Path.GetTempPath(), "kicomlib-lfs-archive-" + Guid.NewGuid().ToString("N") + ".tar");
            try
            {
                await using (FileStream stream = File.Create(output))
                {
                    await using TarWriter writer = new(stream, TarEntryFormat.Pax, leaveOpen: true);
                    foreach (string file in files)
                    {
                        string entryName = Path.GetRelativePath(work, file).Replace('\\', '/');
                        await writer.WriteEntryAsync(file, entryName, cancellationToken);
                    }
                }

                return await File.ReadAllBytesAsync(output, cancellationToken);
            }
            finally
            {
                try
                {
                    if (File.Exists(output))
                    {
                        File.Delete(output);
                    }
                }
                catch (Exception)
                {
                    // The temporary archive is removed on a best-effort basis.
                }
            }
        }

        public async Task<HashSet<string>> ReachableAsync(GitRepository repository, IReadOnlyCollection<string> refs, CancellationToken cancellationToken)
        {
            HashSet<string> shas = new(StringComparer.OrdinalIgnoreCase);
            if (refs.Count == 0 || !Directory.Exists(MirrorPath(repository)))
            {
                return shas;
            }

            List<string> args = new() { "-C", MirrorPath(repository), "rev-list" };
            args.AddRange(refs);
            GitInvocation invocation = _git.Prepare(args, new GitAuth { AllowFile = GitUrlPolicy.FileProtocolAllowed }, GitCli.FetchTimeout);
            using (invocation)
            {
                ProcessRunResult result = await _git.RunAsync(invocation, cancellationToken);
                if (result.ExitCode != 0)
                {
                    return shas;
                }

                foreach (string line in result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    shas.Add(line);
                }
            }

            return shas;
        }

        public static IReadOnlyList<GitCommitInfo> ParseLog(string stdout)
        {
            List<GitCommitInfo> commits = new();
            foreach (string raw in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = raw.Trim().Split('\u001f');
                if (parts.Length < 6 || parts[0].Length < 7)
                {
                    continue;
                }

                DateTime committed = DateTime.UtcNow;
                if (DateTimeOffset.TryParse(parts[2], out DateTimeOffset parsed))
                {
                    committed = parsed.UtcDateTime;
                }

                commits.Add(new GitCommitInfo
                {
                    Sha = parts[0],
                    Parents = string.IsNullOrWhiteSpace(parts[1])
                        ? Array.Empty<string>()
                        : parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                    CommittedAt = committed,
                    AuthorName = string.IsNullOrWhiteSpace(parts[3]) ? "unknown" : parts[3],
                    AuthorEmail = string.IsNullOrWhiteSpace(parts[4]) ? null : parts[4],
                    Message = string.IsNullOrWhiteSpace(parts[5]) ? "(no message)" : parts[5]
                });
            }

            return commits;
        }

        public static bool RefsEqual(IReadOnlyDictionary<string, string> previous, IReadOnlyDictionary<string, string> current)
        {
            if (previous.Count != current.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, string> pair in current)
            {
                if (!previous.TryGetValue(pair.Key, out string? sha) || !string.Equals(sha, pair.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private async Task EnsureMirrorAsync(string mirror, string cloneUrl, GitAuth auth, CancellationToken cancellationToken)
        {
            if (!File.Exists(Path.Combine(mirror, "HEAD")))
            {
                GitInvocation init = _git.Prepare(new[] { "init", "--bare", mirror }, auth, GitCli.CloneTimeout);
                using (init)
                {
                    ProcessRunResult result = await _git.RunAsync(init, cancellationToken);
                    if (result.ExitCode != 0)
                    {
                        throw new GitCommandException(result.Stderr, result.ExitCode);
                    }
                }
            }

            string remoteUrl = GitCli.UrlForGit(cloneUrl, auth);
            ProcessRunResult url = await GitConfigAsync(mirror, auth, new[] { "remote", "get-url", "origin" }, cancellationToken, allowFailure: true);
            if (url.ExitCode != 0)
            {
                await GitConfigAsync(mirror, auth, new[] { "remote", "add", "origin", remoteUrl }, cancellationToken, allowFailure: false);
            }
            else if (!string.Equals(url.Stdout.Trim(), remoteUrl, StringComparison.Ordinal))
            {
                await GitConfigAsync(mirror, auth, new[] { "remote", "set-url", "origin", remoteUrl }, cancellationToken, allowFailure: false);
            }

            await GitConfigAsync(mirror, auth, new[] { "config", "--unset-all", "remote.origin.fetch" }, cancellationToken, allowFailure: true);
            await GitConfigAsync(mirror, auth, new[] { "config", "--add", "remote.origin.fetch", "+refs/heads/*:refs/heads/*" }, cancellationToken, allowFailure: false);
            await GitConfigAsync(mirror, auth, new[] { "config", "--add", "remote.origin.fetch", "+refs/tags/*:refs/tags/*" }, cancellationToken, allowFailure: false);
        }

        private async Task FetchAsync(string mirror, GitAuth auth, bool mirrorHasHistory, CancellationToken cancellationToken)
        {
            GitInvocation fetch = _git.Prepare(
                new[] { "-C", mirror, "fetch", "--prune", "--prune-tags", "origin" },
                auth,
                mirrorHasHistory ? GitCli.FetchTimeout : GitCli.CloneTimeout);
            using (fetch)
            {
                ProcessRunResult result = await _git.RunAsync(fetch, cancellationToken);
                if (result.ExitCode != 0)
                {
                    throw new GitCommandException(string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr, result.ExitCode);
                }
            }
        }

        private async Task<ProcessRunResult> GitConfigAsync(
            string mirror,
            GitAuth auth,
            IReadOnlyList<string> args,
            CancellationToken cancellationToken,
            bool allowFailure)
        {
            List<string> full = new() { "-C", mirror };
            full.AddRange(args);
            GitInvocation invocation = _git.Prepare(full, auth, TimeSpan.FromMinutes(1));
            using (invocation)
            {
                ProcessRunResult result = await _git.RunAsync(invocation, cancellationToken);
                if (!allowFailure && result.ExitCode != 0)
                {
                    throw new GitCommandException(result.Stderr, result.ExitCode);
                }

                return result;
            }
        }
    }
}
