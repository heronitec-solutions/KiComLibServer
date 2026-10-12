using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Services
{
    public class ProjectAccessService
    {
        private readonly ApplicationDbContext _db;

        public ProjectAccessService(ApplicationDbContext db)
        {
            _db = db;
        }

        public bool CanAdmin(CallerContext caller)
        {
            return caller.IsAuthenticated && caller.IsAdmin;
        }

        public async Task<bool> CanReadAsync(CallerContext caller, int projectId, CancellationToken cancellationToken)
        {
            if (!caller.IsAuthenticated)
            {
                return false;
            }

            if (caller.IsAdmin)
            {
                return true;
            }

            List<int> groupIds = caller.Groups.Select(g => g.Id).ToList();
            if (groupIds.Count == 0)
            {
                return false;
            }

            return await _db.ProjectGroupAccess.AnyAsync(
                a => a.ProjectId == projectId && groupIds.Contains(a.GroupId),
                cancellationToken);
        }

        /// <summary>
        /// Write requires the caller's own write right, then either an administrator or a project group with can_write.
        /// A token created with CanWrite false stays read-only, including for administrators.
        /// </summary>
        public async Task<bool> CanWriteAsync(CallerContext caller, int projectId, CancellationToken cancellationToken)
        {
            if (!caller.IsAuthenticated || !caller.CanWrite)
            {
                return false;
            }

            if (caller.IsAdmin)
            {
                return true;
            }

            List<int> groupIds = caller.Groups.Select(g => g.Id).ToList();
            if (groupIds.Count == 0)
            {
                return false;
            }

            return await _db.ProjectGroupAccess.AnyAsync(
                a => a.ProjectId == projectId && a.CanWrite && groupIds.Contains(a.GroupId),
                cancellationToken);
        }

        public IQueryable<Project> Readable(CallerContext caller)
        {
            if (!caller.IsAuthenticated)
            {
                return _db.Projects.Where(_ => false);
            }

            if (caller.IsAdmin)
            {
                return _db.Projects;
            }

            List<int> groupIds = caller.Groups.Select(g => g.Id).ToList();
            return _db.Projects.Where(p => p.GroupAccess.Any(a => groupIds.Contains(a.GroupId)));
        }
    }
}
