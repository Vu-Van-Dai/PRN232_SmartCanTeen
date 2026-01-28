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

        public WalletPaymentsController(AppDbContext db)
        {
            _db = db;
        }

        [HttpPost("{orderId}")]
        public async Task<IActionResult> Pay(Guid orderId)
        {
            var userId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!
            );

            using var tx = await _db.Database.BeginTransactionAsync();

            // Online revenue must belong to an active shift for reporting
            var shift = await _db.Shifts
                .OrderByDescending(s => s.OpenedAt)
                .FirstOrDefaultAsync(s => s.Status == ShiftStatus.Open);
            if (shift == null)
                return BadRequest("No active shift");

            var order = await _db.Orders
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x =>
                    x.Id == orderId &&
                    x.OrderSource == OrderSource.Online &&
                    x.Status == OrderStatus.Pending
                );

            if (order == null)
                return BadRequest("Invalid order");

            var wallet = await _db.Wallets.FirstOrDefaultAsync(x =>
                x.UserId == userId &&
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
            if (order.OrderSource == OrderSource.Online)
            {
                if (order.PickupTime == null)
                {
                    order.Status = OrderStatus.Preparing;
                    order.IsUrgent = true;
                }
                else
                {
                    order.Status = OrderStatus.SystemHolding;
                    order.IsUrgent = false;
                }
            }
            order.PaymentMethod = PaymentMethod.Wallet;

            // 3.5️⃣ LINK SHIFT + SYSTEM ONLINE TOTAL
            order.ShiftId = shift.Id;
            shift.SystemOnlineTotal += order.TotalPrice;

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
