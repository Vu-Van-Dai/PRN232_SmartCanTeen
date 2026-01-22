using API.Hubs;
using Application.Payments;
using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace API.Controllers
{
    [ApiController]
    [Route("api/wallets")]
    [Authorize(Roles = "Parent")]
    public class WalletTopupController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly VnpayService _vnpay;
        private readonly IHubContext<ManagementHub> _managementHub;


        public WalletTopupController(
            AppDbContext db,
            VnpayService vnpay,
            IHubContext<ManagementHub> managementHub)
        {
            _db = db;
            _vnpay = vnpay;
            _managementHub = managementHub;
        }

        /// <summary>
        /// Parent nạp tiền vào ví con
        /// </summary>
        [HttpPost("{walletId}/topup")]
        public async Task<IActionResult> Topup(Guid walletId, decimal amount)
        {
            if (amount <= 0)
                return BadRequest("Invalid amount");

            var parentId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!
            );

            // 1️⃣ Check quyền Parent → ví con
            var access = await _db.WalletAccesses.AnyAsync(x =>
                x.WalletId == walletId &&
                x.UserId == parentId
            );

            if (!access)
                return Forbid();

            // 2️⃣ Tạo VNPAY TRANSACTION
            var txnRef = Guid.NewGuid().ToString("N");

            _db.VnpayTransactions.Add(new VnpayTransaction
            {
                Id = Guid.NewGuid(),
                VnpTxnRef = txnRef,
                WalletId = walletId,
                Amount = amount,
                Purpose = PaymentPurpose.WalletTopup,
                IsSuccess = false,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            // 3️⃣ Trả QR / redirect URL
            var url = _vnpay.CreatePaymentUrl(
                amount,
                txnRef,
                $"Nap tien vao vi {walletId}"
            );

            return Ok(new { qrUrl = url });
        }
    }
}
