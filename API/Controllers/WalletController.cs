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
        private readonly PayosService _payos;

        public WalletController(AppDbContext db, PayosService payos)
        {
            _db = db;
            _payos = payos;
        }

        [HttpPost("topup")]
        public async Task<IActionResult> TopupWallet([FromQuery] decimal amount)
        {
            if (amount <= 0)
                return BadRequest("Invalid amount");

            if (amount != Math.Floor(amount))
                return BadRequest("Amount must be an integer (VND)");

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

            var orderCode = _payos.GenerateOrderCode();
            var payRef = $"PAYOS-{orderCode}";

            _db.PaymentTransactions.Add(new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                PerformedByUserId = userId,
                PaymentRef = payRef,
                Amount = amount,
                Purpose = PaymentPurpose.WalletTopup,
                WalletId = wallet.Id
            });

            await _db.SaveChangesAsync();

            var amountInt = checked((int)amount);
            var description = $"SC TOPUP {orderCode}";
            var origin = Request.Headers.Origin.ToString();
            if (string.IsNullOrWhiteSpace(origin))
            {
                var referer = Request.Headers.Referer.ToString();
                if (Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
                    origin = refererUri.GetLeftPart(UriPartial.Authority);
            }

            var returnUrl = string.IsNullOrWhiteSpace(origin) ? null : $"{origin}/payos/return";
            var cancelUrl = string.IsNullOrWhiteSpace(origin) ? null : $"{origin}/payos/cancel";
            var link = await _payos.CreatePaymentLinkAsync(amountInt, orderCode, description, returnUrl, cancelUrl);

            return Ok(new { paymentUrl = link.CheckoutUrl });
        }
    }
}
