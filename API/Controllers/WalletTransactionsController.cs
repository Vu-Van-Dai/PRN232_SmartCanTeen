using Application.DTOs.Wallet;
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
    [Route("api/wallet")]
    [Authorize(Roles = "Student,Parent")]
    public class WalletTransactionsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public WalletTransactionsController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet("transactions")]
        public async Task<IActionResult> GetMyTransactions([FromQuery] int skip = 0, [FromQuery] int take = 20)
        {
            if (skip < 0) skip = 0;
            if (take <= 0) take = 20;
            if (take > 100) take = 100;

            var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(rawUserId) || !Guid.TryParse(rawUserId, out var userId))
                return Unauthorized();

            var wallet = await _db.Wallets
                .FirstOrDefaultAsync(x => x.UserId == userId && x.Status == WalletStatus.Active);

            // Backfill for legacy users
            if (wallet == null)
            {
                _db.Wallets.Add(new Wallet
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Balance = 0m,
                    Status = WalletStatus.Active,
                    CreatedAt = DateTime.UtcNow
                });

                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // ignore if created concurrently
                }

                wallet = await _db.Wallets
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.UserId == userId && x.Status == WalletStatus.Active);
            }

            if (wallet == null)
                return Problem("Unable to create wallet");

            var baseQuery = _db.Transactions
                .AsNoTracking()
                .Where(x => x.WalletId == wallet.Id);

            var total = await baseQuery.CountAsync();

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
                    OrderId = x.OrderId
                })
                .ToListAsync();

            return Ok(new WalletTransactionsResponse
            {
                Total = total,
                Items = items
            });
        }
    }
}
