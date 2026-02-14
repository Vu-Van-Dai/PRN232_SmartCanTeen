using Application.DTOs.Wallet;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace API.Controllers
{
    [ApiController]
    [Route("api/parent/children")]
    [Authorize(Roles = "Parent")]
    public class ParentChildrenController : ControllerBase
    {
        private readonly AppDbContext _db;

        public ParentChildrenController(AppDbContext db)
        {
            _db = db;
        }

        private bool TryGetCurrentUserId(out Guid userId)
        {
            userId = default;
            var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return !string.IsNullOrWhiteSpace(rawUserId) && Guid.TryParse(rawUserId, out userId);
        }

        private async Task<bool> HasActiveLinkAsync(Guid parentId, Guid studentId, CancellationToken ct)
        {
            return await _db.UserRelations
                .AsNoTracking()
                .AnyAsync(r => r.ParentId == parentId && r.StudentId == studentId && r.IsActive, ct);
        }

        [HttpGet("{studentId:guid}/wallet")]
        public async Task<IActionResult> GetChildWallet(Guid studentId, CancellationToken ct)
        {
            if (!TryGetCurrentUserId(out var parentId)) return Unauthorized();
            if (!await HasActiveLinkAsync(parentId, studentId, ct)) return Forbid();

            var isStudent = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == studentId)
                .AnyAsync(u => u.UserRoles.Any(ur => ur.Role.Name == "Student"), ct);
            if (!isStudent) return BadRequest(new { message = "Selected user is not a Student." });

            var wallet = await _db.Wallets
                .FirstOrDefaultAsync(x => x.UserId == studentId && x.Status == WalletStatus.Active, ct);

            // Backfill: student chưa có ví
            if (wallet == null)
            {
                _db.Wallets.Add(new Core.Entities.Wallet
                {
                    Id = Guid.NewGuid(),
                    UserId = studentId,
                    Balance = 0m,
                    Status = WalletStatus.Active,
                    CreatedAt = DateTime.UtcNow,
                });

                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    // ignore if created concurrently
                }

                wallet = await _db.Wallets
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.UserId == studentId && x.Status == WalletStatus.Active, ct);
            }

            if (wallet == null)
                return Problem("Unable to create wallet");

            // Extra guard: ensure parent has wallet access entry
            var hasWalletAccess = await _db.WalletAccesses
                .AsNoTracking()
                .AnyAsync(a => a.WalletId == wallet.Id && a.UserId == parentId, ct);
            if (!hasWalletAccess) return Forbid();

            return Ok(new
            {
                walletId = wallet.Id,
                balance = wallet.Balance,
            });
        }

        [HttpGet("{studentId:guid}/wallet/transactions")]
        public async Task<IActionResult> GetChildWalletTransactions(
            Guid studentId,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 20,
            CancellationToken ct = default)
        {
            if (skip < 0) skip = 0;
            if (take <= 0) take = 20;
            if (take > 100) take = 100;

            if (!TryGetCurrentUserId(out var parentId)) return Unauthorized();
            if (!await HasActiveLinkAsync(parentId, studentId, ct)) return Forbid();

            var wallet = await _db.Wallets
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == studentId && x.Status == WalletStatus.Active, ct);
            if (wallet == null) return NotFound(new { message = "Wallet not found." });

            var hasWalletAccess = await _db.WalletAccesses
                .AsNoTracking()
                .AnyAsync(a => a.WalletId == wallet.Id && a.UserId == parentId, ct);
            if (!hasWalletAccess) return Forbid();

            var baseQuery = _db.Transactions
                .AsNoTracking()
                .Where(x => x.WalletId == wallet.Id);

            var total = await baseQuery.CountAsync(ct);
            var items = await baseQuery
                .OrderByDescending(x => x.CreatedAt)
                .Skip(skip)
                .Take(take)
                .Select(x => new WalletTransactionItem
                {
                    Id = x.Id,
                    CreatedAt = x.CreatedAt,
                    Amount = x.Amount,
                    Type = x.Type,
                    Status = x.Status,
                    PaymentMethod = x.PaymentMethod,
                    OrderId = x.OrderId,
                })
                .ToListAsync(ct);

            return Ok(new WalletTransactionsResponse
            {
                Total = total,
                Items = items,
            });
        }

        [HttpGet("{studentId:guid}/orders")]
        public async Task<IActionResult> GetChildOrders(Guid studentId, CancellationToken ct)
        {
            if (!TryGetCurrentUserId(out var parentId)) return Unauthorized();
            if (!await HasActiveLinkAsync(parentId, studentId, ct)) return Forbid();

            var isStudent = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == studentId)
                .AnyAsync(u => u.UserRoles.Any(ur => ur.Role.Name == "Student"), ct);
            if (!isStudent) return BadRequest(new { message = "Selected user is not a Student." });

            var orders = await _db.Orders
                .AsNoTracking()
                .Where(o => o.OrderedByUserId == studentId && o.OrderSource == OrderSource.Online)
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.Item)
                .OrderByDescending(o => o.CreatedAt)
                .Take(50)
                .Select(o => new
                {
                    id = o.Id,
                    createdAt = o.CreatedAt,
                    pickupTime = o.PickupTime,
                    status = (int)o.Status,
                    totalPrice = o.TotalPrice,
                    items = o.Items.Select(i => new
                    {
                        itemId = i.ItemId,
                        name = i.Item.Name,
                        quantity = i.Quantity,
                        unitPrice = i.UnitPrice
                    }),
                    stationTasks = _db.OrderStationTasks
                        .AsNoTracking()
                        .Where(t => t.OrderId == o.Id)
                        .OrderBy(t => t.Screen.Name)
                        .Select(t => new
                        {
                            screenKey = t.Screen.Key,
                            screenName = t.Screen.Name,
                            status = (int)t.Status,
                            startedAt = t.StartedAt,
                            readyAt = t.ReadyAt,
                            completedAt = t.CompletedAt,
                        })
                        .ToList(),
                    pickedAtCounter = (string?)null
                })
                .ToListAsync(ct);

            return Ok(orders);
        }
    }
}
