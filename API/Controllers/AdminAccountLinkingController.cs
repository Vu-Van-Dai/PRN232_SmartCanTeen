using Core.Entities;
using Core.Enums;
using API.Services.Email;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace API.Controllers
{
    [ApiController]
    [Route("api/admin/account-linking")]
    [Authorize(Roles = "AdminSystem")]
    public class AdminAccountLinkingController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IEmailSender _email;
        private readonly ILogger<AdminAccountLinkingController> _logger;

        public AdminAccountLinkingController(AppDbContext db, IEmailSender email, ILogger<AdminAccountLinkingController> logger)
        {
            _db = db;
            _email = email;
            _logger = logger;
        }

        private static IQueryable<User> ApplyUserSearch(IQueryable<User> q, string? query)
        {
            query = (query ?? "").Trim();
            if (string.IsNullOrWhiteSpace(query)) return q;

            var like = $"%{query}%";
            return q.Where(u =>
                EF.Functions.ILike(u.Email, like) ||
                (u.FullName != null && EF.Functions.ILike(u.FullName, like)) ||
                (u.StudentCode != null && EF.Functions.ILike(u.StudentCode, like))
            );
        }

        [HttpGet("students")]
        public async Task<IActionResult> SearchStudents([FromQuery] string? query = null)
        {
            var students = _db.Users
                .AsNoTracking()
                .Where(u => u.UserRoles.Any(ur => ur.Role.Name == "Student"));

            students = ApplyUserSearch(students, query);

            var res = await students
                .OrderBy(u => u.FullName ?? u.Email)
                .Take(100)
                .Select(u => new
                {
                    id = u.Id,
                    name = u.FullName,
                    email = u.Email,
                    studentCode = u.StudentCode,
                    linkedParentsCount = _db.UserRelations.Count(r => r.StudentId == u.Id && r.IsActive),
                })
                .ToListAsync();

            return Ok(res);
        }

        [HttpGet("parents")]
        public async Task<IActionResult> SearchParents([FromQuery] string? query = null)
        {
            var parents = _db.Users
                .AsNoTracking()
                .Where(u => u.UserRoles.Any(ur => ur.Role.Name == "Parent"));

            parents = ApplyUserSearch(parents, query);

            var res = await parents
                .OrderBy(u => u.FullName ?? u.Email)
                .Take(100)
                .Select(u => new
                {
                    id = u.Id,
                    name = u.FullName,
                    email = u.Email,
                })
                .ToListAsync();

            return Ok(res);
        }

        [HttpGet("students/{studentId:guid}")]
        public async Task<IActionResult> GetStudentLinks(Guid studentId)
        {
            var student = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == studentId)
                .Select(u => new
                {
                    id = u.Id,
                    name = u.FullName,
                    email = u.Email,
                    studentCode = u.StudentCode,
                    isStudent = u.UserRoles.Any(ur => ur.Role.Name == "Student"),
                })
                .FirstOrDefaultAsync();

            if (student == null) return NotFound();
            if (!student.isStudent) return BadRequest(new { message = "Selected user is not a Student." });

            var parents = await _db.UserRelations
                .AsNoTracking()
                .Where(r => r.StudentId == studentId && r.IsActive)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    id = r.ParentId,
                    name = r.Parent.FullName,
                    email = r.Parent.Email,
                    relationType = r.RelationType.ToString(),
                    createdAt = r.CreatedAt,
                })
                .ToListAsync();

            return Ok(new
            {
                student,
                parents,
            });
        }

        [HttpPost("students/{studentId:guid}/parents/{parentId:guid}")]
        public async Task<IActionResult> LinkParent(Guid studentId, Guid parentId, CancellationToken ct)
        {
            if (studentId == parentId) return BadRequest(new { message = "Cannot link user to self." });

            var student = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == studentId && u.UserRoles.Any(ur => ur.Role.Name == "Student"))
                .Select(u => new
                {
                    id = u.Id,
                    name = u.FullName,
                    email = u.Email,
                    studentCode = u.StudentCode,
                })
                .FirstOrDefaultAsync(ct);
            if (student == null) return NotFound(new { message = "Student not found." });

            var parent = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == parentId && u.UserRoles.Any(ur => ur.Role.Name == "Parent"))
                .Select(u => new
                {
                    id = u.Id,
                    name = u.FullName,
                    email = u.Email,
                })
                .FirstOrDefaultAsync(ct);
            if (parent == null) return NotFound(new { message = "Parent not found." });

            var relation = await _db.UserRelations.FindAsync(parentId, studentId);
            var shouldNotify = relation == null || !relation.IsActive;
            if (relation == null)
            {
                relation = new UserRelation
                {
                    ParentId = parentId,
                    StudentId = studentId,
                    RelationType = RelationType.Guardian,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                };
                _db.UserRelations.Add(relation);
            }
            else
            {
                relation.RelationType = RelationType.Guardian;
                relation.IsActive = true;
            }

            // Ensure wallet access (Parent -> Student wallet)
            var wallet = await _db.Wallets
                .FirstOrDefaultAsync(w => w.UserId == studentId && w.Status == WalletStatus.Active, ct);
            if (wallet == null)
            {
                wallet = new Wallet
                {
                    Id = Guid.NewGuid(),
                    UserId = studentId,
                    Balance = 0,
                    Status = WalletStatus.Active,
                    CreatedAt = DateTime.UtcNow,
                };
                _db.Wallets.Add(wallet);
            }

            var hasAccess = await _db.WalletAccesses.AnyAsync(a => a.WalletId == wallet.Id && a.UserId == parentId, ct);
            if (!hasAccess)
            {
                _db.WalletAccesses.Add(new WalletAccess
                {
                    WalletId = wallet.Id,
                    UserId = parentId,
                    AccessType = WalletAccessType.Shared,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            await _db.SaveChangesAsync(ct);

            if (shouldNotify)
            {
                await TrySendLinkedEmailsAsync(
                    studentEmail: student.email,
                    studentName: student.name,
                    studentCode: student.studentCode,
                    parentEmail: parent.email,
                    parentName: parent.name,
                    linkedAtUtc: DateTime.UtcNow,
                    ct: ct
                );
            }
            return Ok(new { message = "Linked." });
        }

        private async Task TrySendLinkedEmailsAsync(
            string studentEmail,
            string? studentName,
            string? studentCode,
            string parentEmail,
            string? parentName,
            DateTime linkedAtUtc,
            CancellationToken ct)
        {
            try
            {
                var linkedAtLocal = linkedAtUtc.ToLocalTime();
                var actor = User.FindFirstValue(ClaimTypes.Name)
                    ?? User.FindFirstValue(ClaimTypes.Email)
                    ?? "Admin System";

                var subject = "[SmartCanteen] Thông báo liên kết tài khoản";

                var studentDisplay = string.IsNullOrWhiteSpace(studentName) ? studentEmail : studentName;
                var parentDisplay = string.IsNullOrWhiteSpace(parentName) ? parentEmail : parentName;
                var studentCodeLine = string.IsNullOrWhiteSpace(studentCode) ? "" : $"<p><b>Mã học sinh:</b> {studentCode}</p>";
                var timeLine = $"<p><b>Thời gian:</b> {linkedAtLocal:dd/MM/yyyy HH:mm}</p>";
                var actorLine = $"<p><b>Thực hiện bởi:</b> {actor}</p>";

                // Email to Parent
                if (!string.IsNullOrWhiteSpace(parentEmail))
                {
                    var htmlToParent = $@"
<p>Xin chào <b>{parentDisplay}</b>,</p>
<p>Tài khoản của bạn vừa được <b>liên kết</b> với học sinh:</p>
<p><b>{studentDisplay}</b> ({studentEmail})</p>
{studentCodeLine}
{timeLine}
{actorLine}
<p>Nếu bạn không mong đợi thay đổi này, vui lòng liên hệ quản trị hệ thống.</p>";

                    await _email.SendAsync(parentEmail, subject, htmlToParent, ct: ct);
                }

                // Email to Student
                if (!string.IsNullOrWhiteSpace(studentEmail))
                {
                    var htmlToStudent = $@"
<p>Xin chào <b>{studentDisplay}</b>,</p>
<p>Tài khoản của bạn vừa được <b>liên kết</b> với phụ huynh:</p>
<p><b>{parentDisplay}</b> ({parentEmail})</p>
{studentCodeLine}
{timeLine}
{actorLine}
<p>Nếu bạn không mong đợi thay đổi này, vui lòng liên hệ quản trị hệ thống.</p>";

                    await _email.SendAsync(studentEmail, subject, htmlToStudent, ct: ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send account-linking notification emails.");
            }
        }

        [HttpDelete("students/{studentId:guid}/parents/{parentId:guid}")]
        public async Task<IActionResult> UnlinkParent(Guid studentId, Guid parentId)
        {
            var relation = await _db.UserRelations.FindAsync(parentId, studentId);
            if (relation == null) return NotFound(new { message = "Link not found." });

            relation.IsActive = false;

            // Remove wallet access for all wallets of the student (safety)
            var walletIds = await _db.Wallets
                .AsNoTracking()
                .Where(w => w.UserId == studentId)
                .Select(w => w.Id)
                .ToListAsync();

            if (walletIds.Count > 0)
            {
                var accesses = await _db.WalletAccesses
                    .Where(a => walletIds.Contains(a.WalletId) && a.UserId == parentId)
                    .ToListAsync();
                if (accesses.Count > 0) _db.WalletAccesses.RemoveRange(accesses);
            }

            await _db.SaveChangesAsync();
            return Ok(new { message = "Unlinked." });
        }
    }
}
