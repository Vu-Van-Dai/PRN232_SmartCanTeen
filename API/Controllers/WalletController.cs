using Application.Payments;
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
    public class WalletController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly VnpayService _vnpay;

        public WalletController(AppDbContext db, VnpayService vnpay)
        {
            _db = db;
            _vnpay = vnpay;
        }

        [HttpPost("topup")]
        public async Task<IActionResult> TopupWallet([FromQuery] decimal amount)
        {
            if (amount <= 0)
                return BadRequest("Invalid amount");

            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var wallet = await _db.Wallets
                .FirstOrDefaultAsync(x => x.UserId == userId && x.Status == WalletStatus.Active);

            if (wallet == null)
            {
                wallet = new Wallet
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Balance = 0m,
                    Status = WalletStatus.Active,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Wallets.Add(wallet);
                await _db.SaveChangesAsync();
            }

            var txnRef = Guid.NewGuid().ToString("N");

            _db.VnpayTransactions.Add(new VnpayTransaction
            {
                Id = Guid.NewGuid(),
                VnpTxnRef = txnRef,
                Amount = amount,
                Purpose = PaymentPurpose.WalletTopup,
                WalletId = wallet.Id
            });

            await _db.SaveChangesAsync();

            var url = _vnpay.CreatePaymentUrl(amount, txnRef, "Nap tien vi");

            return Ok(new { paymentUrl = url });
        }
    }
}
