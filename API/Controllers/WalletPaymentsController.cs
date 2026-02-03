using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using API.Services;

namespace API.Controllers
{
    [ApiController]
    [Route("api/wallet/pay")]
    [Authorize(Roles = "Student,Parent")]
    public class WalletPaymentsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly BusinessDayGate _dayGate;

        public WalletPaymentsController(AppDbContext db, BusinessDayGate dayGate)
        {
            _db = db;
            _dayGate = dayGate;
        }

        private async Task EnsureStationTasksForOrderAsync(Guid orderId, OrderStatus orderStatus)
        {
            // Find categories in this order
            var orderCategoryIds = await _db.OrderItems
                .AsNoTracking()
                .Where(oi => oi.OrderId == orderId)
                .Select(oi => oi.Item.CategoryId)
                .Distinct()
                .ToListAsync();

            if (orderCategoryIds.Count == 0) return;

            // Screens whose configured categories intersect the order's categories
            var screenIds = await _db.DisplayScreenCategories
                .AsNoTracking()
                .Where(sc => orderCategoryIds.Contains(sc.CategoryId))
                .Select(sc => sc.ScreenId)
                .Distinct()
                .ToListAsync();

            if (screenIds.Count == 0) return;

            var existing = await _db.OrderStationTasks
                .AsNoTracking()
                .Where(t => t.OrderId == orderId)
                .Select(t => t.ScreenId)
                .ToListAsync();

            var existingSet = existing.ToHashSet();
            var missingScreenIds = screenIds.Where(id => !existingSet.Contains(id)).ToList();
            if (missingScreenIds.Count == 0) return;

            var screens = await _db.DisplayScreens
                .AsNoTracking()
                .Where(s => missingScreenIds.Contains(s.Id) && s.IsActive)
                .Select(s => new { s.Id, s.Key })
                .ToListAsync();

            foreach (var s in screens)
            {
                var isDrink = string.Equals(s.Key, "drink", StringComparison.OrdinalIgnoreCase);

                // For pre-orders, keep tasks Pending until the order is in active preparation.
                var initial = StationTaskStatus.Pending;
                DateTime? startedAt = null;

                // Immediate orders should show up as Preparing for all stations.
                // Only pre-orders (SystemHolding) stay Pending.
                if (orderStatus == OrderStatus.Preparing)
                {
                    initial = StationTaskStatus.Preparing;
                    startedAt = DateTime.UtcNow;
                }
                else if (orderStatus != OrderStatus.SystemHolding && isDrink)
                {
                    // Legacy behavior: drink station starts immediately when the order is active.
                    initial = StationTaskStatus.Preparing;
                    startedAt = DateTime.UtcNow;
                }

                _db.OrderStationTasks.Add(new OrderStationTask
                {
                    OrderId = orderId,
                    ScreenId = s.Id,
                    Status = initial,
                    StartedAt = startedAt,
                });
            }
        }

        [HttpPost("{orderId}")]
        public async Task<IActionResult> Pay(Guid orderId)
        {
            var gate = await _dayGate.EnsurePosOperationsAllowedAsync();
            if (!gate.allowed)
                return BadRequest(gate.reason);

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

            // 3.2️⃣ ENSURE STATION TASKS (so KDS & Student progress work immediately)
            await EnsureStationTasksForOrderAsync(order.Id, order.Status);

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
