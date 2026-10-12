using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Services
{
    public class RevisionDiscovery
    {
        private readonly GitMirrorService _mirror;

        public RevisionDiscovery(GitMirrorService mirror)
        {
            _mirror = mirror;
        }

        public async Task ImportAsync(
            ApplicationDbContext db,
            GitRepository repository,
            IReadOnlyDictionary<string, string> previousRefs,
            IReadOnlyDictionary<string, string> currentRefs,
            ProjectJobQueue queue,
            int maxCommits,
            int initialTags,
            CancellationToken cancellationToken)
        {
            List<Project> projects = await db.Projects
                .Include(p => p.Revisions)
                .Where(p => p.RepositoryId == repository.Id)
                .ToListAsync(cancellationToken);
            Dictionary<string, string[]> refsByCommit = await RefsByCommitAsync(repository, currentRefs, cancellationToken);

            foreach (Project project in projects)
            {
                List<GitCommitInfo> commits = project.Revisions.Count == 0
                    ? await InitialAsync(repository, project, currentRefs, maxCommits, initialTags, cancellationToken)
                    : await IncrementalAsync(repository, project, previousRefs, currentRefs, maxCommits, cancellationToken);

                foreach (GitCommitInfo commit in commits.GroupBy(c => c.Sha, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
                {
                    if (project.Revisions.Any(r => r.CommitSha.Equals(commit.Sha, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    ProjectRevision revision = new()
                    {
                        ProjectId = project.Id,
                        CommitSha = commit.Sha,
                        ParentShas = commit.Parents,
                        CommittedAt = commit.CommittedAt,
                        AuthorName = commit.AuthorName,
                        AuthorEmail = commit.AuthorEmail,
                        Message = commit.Message,
                        Refs = RefsForCommit(refsByCommit, commit.Sha),
                        Reachable = true,
                        AnalysisStatus = "pending",
                        CreatedAt = DateTime.UtcNow
                    };
                    db.ProjectRevisions.Add(revision);
                    project.Revisions.Add(revision);
                    await db.SaveChangesAsync(cancellationToken);
                    await queue.EnqueueAsync(
                        "analyze",
                        ProjectJobQueue.AnalyzeKey(project.Id, commit.Sha),
                        repository.Id,
                        project.Id,
                        "{\"revisionId\":" + revision.Id.ToString() + ",\"commitSha\":\"" + commit.Sha + "\"}",
                        cancellationToken);
                }
            }

            HashSet<string> reachable = await _mirror.ReachableAsync(repository, currentRefs.Keys.ToList(), cancellationToken);
            foreach (Project project in projects)
            {
                foreach (ProjectRevision revision in project.Revisions)
                {
                    revision.Reachable = reachable.Contains(revision.CommitSha);
                    revision.Refs = RefsForCommit(refsByCommit, revision.CommitSha);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        private async Task<List<GitCommitInfo>> InitialAsync(
            GitRepository repository,
            Project project,
            IReadOnlyDictionary<string, string> currentRefs,
            int maxCommits,
            int initialTags,
            CancellationToken cancellationToken)
        {
            List<GitCommitInfo> commits = new();
            foreach (KeyValuePair<string, string> branch in TrackedBranches(repository, currentRefs))
            {
                GitCommitInfo? info = await _mirror.ShowAsync(repository, branch.Value, cancellationToken);
                if (info != null)
                {
                    commits.Add(info);
                }
            }

            await AppendRecentCommitsAsync(commits, repository, project, currentRefs, maxCommits, cancellationToken);

            foreach (string sha in await NewestTagShasAsync(repository, currentRefs, initialTags, cancellationToken))
            {
                GitCommitInfo? info = await _mirror.ShowAsync(repository, sha, cancellationToken);
                if (info != null)
                {
                    commits.Add(info);
                }
            }

            return commits;
        }

        private async Task<List<GitCommitInfo>> IncrementalAsync(
            GitRepository repository,
            Project project,
            IReadOnlyDictionary<string, string> previousRefs,
            IReadOnlyDictionary<string, string> currentRefs,
            int maxCommits,
            CancellationToken cancellationToken)
        {
            List<GitCommitInfo> commits = new();
            string? path = project.ExportScope == "repository" ? null : ProjectDirectory(project.ProjectFile);
            foreach (KeyValuePair<string, string> branch in TrackedBranches(repository, currentRefs))
            {
                if (!previousRefs.TryGetValue(branch.Key, out string? previous) || string.Equals(previous, branch.Value, StringComparison.OrdinalIgnoreCase))
                {
                    if (previous == null)
                    {
                        GitCommitInfo? tip = await _mirror.ShowAsync(repository, branch.Value, cancellationToken);
                        if (tip != null)
                        {
                            commits.Add(tip);
                        }
                    }

                    continue;
                }

                IReadOnlyList<GitCommitInfo> range = await _mirror.LogAsync(repository, previous + ".." + branch.Value, path, cancellationToken);
                if (range.Count > maxCommits)
                {
                    if (range.Count > 0)
                    {
                        commits.Add(range[0]);
                    }
                }
                else
                {
                    commits.AddRange(range);
                }
            }

            foreach (KeyValuePair<string, string> tag in currentRefs.Where(pair => IsTrackedTag(repository, pair.Key)))
            {
                bool unchanged = previousRefs.TryGetValue(tag.Key, out string? previous)
                    && string.Equals(previous, tag.Value, StringComparison.OrdinalIgnoreCase);
                bool alreadyKnown = project.Revisions.Any(revision =>
                    revision.CommitSha.Equals(tag.Value, StringComparison.OrdinalIgnoreCase)
                    || revision.Refs.Contains(tag.Key));
                if (unchanged && alreadyKnown)
                {
                    continue;
                }

                GitCommitInfo? info = await _mirror.ShowAsync(repository, tag.Value, cancellationToken);
                if (info != null)
                {
                    commits.Add(info);
                }
            }

            await AppendRecentCommitsAsync(commits, repository, project, currentRefs, maxCommits, cancellationToken);
            return commits;
        }

        private async Task AppendRecentCommitsAsync(
            List<GitCommitInfo> commits,
            GitRepository repository,
            Project project,
            IReadOnlyDictionary<string, string> currentRefs,
            int maxCommits,
            CancellationToken cancellationToken)
        {
            if (maxCommits <= 0)
            {
                return;
            }

            string? path = project.ExportScope == "repository" ? null : ProjectDirectory(project.ProjectFile);
            foreach (KeyValuePair<string, string> branch in TrackedBranches(repository, currentRefs))
            {
                IReadOnlyList<GitCommitInfo> recent = await _mirror.LogAsync(
                    repository, branch.Value, path, cancellationToken, maxCommits);
                commits.AddRange(recent);
            }
        }

        private async Task<IReadOnlyList<string>> NewestTagShasAsync(
            GitRepository repository,
            IReadOnlyDictionary<string, string> currentRefs,
            int limit,
            CancellationToken cancellationToken)
        {
            if (limit <= 0)
            {
                return Array.Empty<string>();
            }

            List<string> matching = currentRefs
                .Where(pair => IsTrackedTag(repository, pair.Key))
                .Select(pair => pair.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (matching.Count <= limit)
            {
                return matching;
            }

            List<(DateTime When, string Sha)> dated = new();
            foreach (string sha in matching)
            {
                GitCommitInfo? info = await _mirror.ShowAsync(repository, sha, cancellationToken);
                if (info != null)
                {
                    dated.Add((info.CommittedAt, info.Sha));
                }
            }

            return dated
                .OrderByDescending(item => item.When)
                .Take(limit)
                .Select(item => item.Sha)
                .ToList();
        }

        public static IEnumerable<KeyValuePair<string, string>> TrackedBranches(GitRepository repository, IReadOnlyDictionary<string, string> refs)
        {
            foreach (KeyValuePair<string, string> pair in refs)
            {
                if (!pair.Key.StartsWith("refs/heads/", StringComparison.Ordinal))
                {
                    continue;
                }

                string shortName = pair.Key["refs/heads/".Length..];
                if (!string.IsNullOrEmpty(repository.DefaultBranch) && shortName.Equals(repository.DefaultBranch, StringComparison.Ordinal))
                {
                    yield return pair;
                    continue;
                }

                if (repository.TrackedBranches.Any(pattern => GitText.GlobMatch(pattern, shortName)))
                {
                    yield return pair;
                }
            }
        }

        public static bool IsTrackedTag(GitRepository repository, string refName)
        {
            if (!refName.StartsWith("refs/tags/", StringComparison.Ordinal) || refName.EndsWith("^{}", StringComparison.Ordinal))
            {
                return false;
            }

            return GitText.GlobMatch(string.IsNullOrWhiteSpace(repository.TagPattern) ? "*" : repository.TagPattern, refName["refs/tags/".Length..]);
        }

        public static string? ProjectDirectory(string projectFile)
        {
            string normalized = projectFile.Replace('\\', '/');
            int slash = normalized.LastIndexOf('/');
            if (slash < 0)
            {
                return null;
            }

            return normalized[..slash];
        }

        private async Task<Dictionary<string, string[]>> RefsByCommitAsync(
            GitRepository repository,
            IReadOnlyDictionary<string, string> refs,
            CancellationToken cancellationToken)
        {
            Dictionary<string, List<string>> map = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> pair in refs)
            {
                if (pair.Key.EndsWith("^{}", StringComparison.Ordinal))
                {
                    continue;
                }

                string sha = pair.Value;
                if (pair.Key.StartsWith("refs/tags/", StringComparison.Ordinal))
                {
                    GitCommitInfo? peeled = await _mirror.ShowAsync(repository, pair.Value, cancellationToken);
                    if (peeled != null)
                    {
                        sha = peeled.Sha;
                    }
                }

                if (!map.TryGetValue(sha, out List<string>? names))
                {
                    names = new List<string>();
                    map[sha] = names;
                }

                names.Add(pair.Key);
            }

            return map.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        }

        private static string[] RefsForCommit(Dictionary<string, string[]> refsByCommit, string sha)
        {
            return refsByCommit.TryGetValue(sha, out string[]? names) ? names : Array.Empty<string>();
        }
    }
}
