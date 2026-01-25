using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Core.Entities;
using Core.Enums;

namespace API.Controllers
{
    [ApiController]
    [Route("api/wallet")]
    [Authorize(Roles = "Student,Parent")]
    public class WalletMeController : ControllerBase
    {
        private readonly AppDbContext _db;

        public WalletMeController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet("me")]
        public async Task<IActionResult> GetMe()
        {
            var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(rawUserId) || !Guid.TryParse(rawUserId, out var userId))
                return Unauthorized();

            var wallet = await _db.Wallets
                .FirstOrDefaultAsync(x => x.UserId == userId && x.Status == WalletStatus.Active);

            // Backfill: user cũ chưa có ví
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
                    // If a concurrent request created it, ignore.
                }

                wallet = await _db.Wallets
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.UserId == userId && x.Status == WalletStatus.Active);
            }

            if (wallet == null)
                return Problem("Unable to create wallet");

            return Ok(new
            {
                walletId = wallet.Id,
                balance = wallet.Balance
            });
        }
    }
}
