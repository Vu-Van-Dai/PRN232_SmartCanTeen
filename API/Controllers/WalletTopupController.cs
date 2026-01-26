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
        private readonly PayosService _payos;
        private readonly IHubContext<ManagementHub> _managementHub;


        public WalletTopupController(
            AppDbContext db,
            PayosService payos,
            IHubContext<ManagementHub> managementHub)
        {
            _db = db;
            _payos = payos;
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

            if (amount != Math.Floor(amount))
                return BadRequest("Amount must be an integer (VND)");

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

            // 2️⃣ Tạo PAYOS PAYMENT LINK (store mapping in existing table)
            var orderCode = _payos.GenerateOrderCode();
            var payRef = $"PAYOS-{orderCode}";

            _db.PaymentTransactions.Add(new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                PerformedByUserId = parentId,
                PaymentRef = payRef,
                WalletId = walletId,
                Amount = amount,
                Purpose = PaymentPurpose.WalletTopup,
                IsSuccess = false,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            // 3️⃣ Trả checkout URL
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

            return Ok(new { qrUrl = link.CheckoutUrl });
        }
    }
}
