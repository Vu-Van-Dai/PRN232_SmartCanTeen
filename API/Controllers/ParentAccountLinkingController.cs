using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace API.Controllers
{
    [ApiController]
    [Route("api/parent/account-linking")]
    [Authorize(Roles = "Parent")]
    public class ParentAccountLinkingController : ControllerBase
    {
        private readonly AppDbContext _db;

        public ParentAccountLinkingController(AppDbContext db)
        {
            _db = db;
        }

        public class LinkChildRequest
        {
            public string Query { get; set; } = string.Empty;
            public RelationType? RelationType { get; set; }
        }

        private bool TryGetCurrentUserId(out Guid userId)
        {
            userId = default;
            var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return !string.IsNullOrWhiteSpace(rawUserId) && Guid.TryParse(rawUserId, out userId);
        }

        [HttpGet("children")]
        public async Task<IActionResult> GetMyLinkedChildren(CancellationToken ct)
        {
            if (!TryGetCurrentUserId(out var parentId)) return Unauthorized();

            var children = await _db.UserRelations
                .AsNoTracking()
                .Where(r => r.ParentId == parentId && r.IsActive)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    id = r.StudentId,
                    name = r.Student.FullName,
                    email = r.Student.Email,
                    studentCode = r.Student.StudentCode,
                    relationType = r.RelationType.ToString(),
                    createdAt = r.CreatedAt,
                })
                .ToListAsync(ct);

            return Ok(children);
        }

        [HttpPost("link")]
        public async Task<IActionResult> LinkChild([FromBody] LinkChildRequest req, CancellationToken ct)
        {
            if (!TryGetCurrentUserId(out var parentId)) return Unauthorized();

            var query = (req?.Query ?? "").Trim();
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest(new { message = "Vui lòng nhập mã học sinh hoặc email." });

            query = query.ToLowerInvariant();
            var student = await _db.Users
                .AsNoTracking()
                .Where(u => !u.IsDeleted && u.IsActive)
                .Where(u => u.UserRoles.Any(ur => ur.Role.Name == "Student"))
                .Where(u => (u.StudentCode != null && u.StudentCode.ToLower() == query) || u.Email.ToLower() == query)
                .Select(u => new
                {
                    id = u.Id,
                    name = u.FullName,
                    email = u.Email,
                    studentCode = u.StudentCode,
                })
                .FirstOrDefaultAsync(ct);

            if (student == null)
                return NotFound(new { message = "Không tìm thấy học sinh phù hợp." });
            if (student.id == parentId)
                return BadRequest(new { message = "Không thể liên kết với chính bạn." });

            var relation = await _db.UserRelations.FindAsync([parentId, student.id], ct);
            if (relation == null)
            {
                relation = new UserRelation
                {
                    ParentId = parentId,
                    StudentId = student.id,
                    RelationType = req?.RelationType ?? RelationType.Guardian,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                };
                _db.UserRelations.Add(relation);
            }
            else
            {
                relation.RelationType = req?.RelationType ?? relation.RelationType;
                relation.IsActive = true;
            }

            // Ensure wallet access (Parent -> Student wallet)
            var wallet = await _db.Wallets
                .FirstOrDefaultAsync(w => w.UserId == student.id && w.Status == WalletStatus.Active, ct);
            if (wallet == null)
            {
                wallet = new Wallet
                {
                    Id = Guid.NewGuid(),
                    UserId = student.id,
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

            return Ok(new
            {
                message = "Đã liên kết.",
                student,
            });
        }

        [HttpDelete("children/{studentId:guid}")]
        public async Task<IActionResult> UnlinkChild(Guid studentId, CancellationToken ct)
        {
            if (!TryGetCurrentUserId(out var parentId)) return Unauthorized();

            var relation = await _db.UserRelations.FindAsync([parentId, studentId], ct);
            if (relation == null) return NotFound(new { message = "Liên kết không tồn tại." });

            relation.IsActive = false;

            // Remove wallet access for all wallets of the student (safety)
            var walletIds = await _db.Wallets
                .AsNoTracking()
                .Where(w => w.UserId == studentId)
                .Select(w => w.Id)
                .ToListAsync(ct);

            if (walletIds.Count > 0)
            {
                var accesses = await _db.WalletAccesses
                    .Where(a => walletIds.Contains(a.WalletId) && a.UserId == parentId)
                    .ToListAsync(ct);
                if (accesses.Count > 0) _db.WalletAccesses.RemoveRange(accesses);
            }

            await _db.SaveChangesAsync(ct);
            return Ok(new { message = "Đã huỷ liên kết." });
        }
    }
}
