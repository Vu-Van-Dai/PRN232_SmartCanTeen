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
    public class WalletController : ControllerBase
    {
        [Authorize]
        [HttpPost("topup")]
        public async Task<IActionResult> TopupWallet(
    decimal amount,
    AppDbContext db,
    VnpayService vnpay)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var wallet = await db.Wallets.FirstAsync(x => x.UserId == userId && x.Status == WalletStatus.Active);

            var txnRef = Guid.NewGuid().ToString("N");

            db.VnpayTransactions.Add(new VnpayTransaction
            {
                Id = Guid.NewGuid(),
                VnpTxnRef = txnRef,
                Amount = amount,
                Purpose = PaymentPurpose.WalletTopup,
                WalletId = wallet.Id
            });

            await db.SaveChangesAsync();

            var url = vnpay.CreatePaymentUrl(amount, txnRef, "Nap tien vi");

            return Ok(new { paymentUrl = url });
        }
    }
}
