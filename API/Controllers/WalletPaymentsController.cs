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
    [Route("api/wallet/pay")]
    [Authorize(Roles = "Student,Parent")]
    public class WalletPaymentsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ICurrentCampusService _campus;

        public WalletPaymentsController(
            AppDbContext db,
            ICurrentCampusService campus)
        {
            _db = db;
            _campus = campus;
        }

        [HttpPost("{orderId}")]
        public async Task<IActionResult> Pay(Guid orderId)
        {
            var userId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!
            );

            using var tx = await _db.Database.BeginTransactionAsync();

            var order = await _db.Orders
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x =>
                    x.Id == orderId &&
                    x.OrderSource == OrderSource.Online &&
                    x.Status == OrderStatus.Pending &&
                    x.CampusId == _campus.CampusId
                );

            if (order == null)
                return BadRequest("Invalid order");

            var wallet = await _db.Wallets.FirstOrDefaultAsync(x =>
                x.UserId == userId &&
                x.CampusId == _campus.CampusId &&
                x.Status == WalletStatus.Active
            );

            if (wallet == null)
                return BadRequest("Wallet not found");

            if (wallet.Balance < order.TotalPrice)
                return BadRequest("Insufficient balance");

            // 1️⃣ TRỪ TIỀN VÍ
            wallet.Balance -= order.TotalPrice;

            // 2️⃣ TRANSACTION
            _db.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                CampusId = _campus.CampusId,
                WalletId = wallet.Id,
                OrderId = order.Id,
                Amount = order.TotalPrice,
                Type = TransactionType.Debit,
                Status = TransactionStatus.Success,
                PaymentMethod = PaymentMethod.Wallet,
                PerformedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            });

            // 3️⃣ UPDATE ORDER
            order.Status = OrderStatus.Paid;
            order.PaymentMethod = PaymentMethod.Wallet;

            // 4️⃣ TRỪ KHO + INVENTORY LOG
            foreach (var item in order.Items)
            {
                var menuItem = await _db.MenuItems
                    .FirstAsync(x => x.Id == item.ItemId);

                if (menuItem.InventoryQuantity < item.Quantity)
                    throw new Exception("Out of stock");

                menuItem.InventoryQuantity -= item.Quantity;

                _db.InventoryLogs.Add(new InventoryLog
                {
                    Id = Guid.NewGuid(),
                    CampusId = order.CampusId,
                    ItemId = item.ItemId,
                    ChangeQuantity = -item.Quantity,
                    Reason = InventoryLogReason.Sale,
                    ReferenceId = order.Id,
                    PerformedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok("Payment successful");
        }
    }
}
