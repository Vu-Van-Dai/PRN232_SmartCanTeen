using Core.Common;
using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Application.Orders
{
    public class InventoryService
    {
        private readonly AppDbContext _db;
        private readonly IInventoryNotifier _notifier;

        public InventoryService(AppDbContext db, IInventoryNotifier notifier)
        {
            _db = db;
            _notifier = notifier;
        }
        public async Task DeductInventoryAsync(Order order, Guid performedByUserId)
        {
            var items = await _db.OrderItems
                .Include(x => x.Item)
                .Where(x => x.OrderId == order.Id)
                .ToListAsync();

            foreach (var oi in items)
            {
                if (oi.Item.InventoryQuantity < oi.Quantity)
                    throw new Exception($"Item {oi.Item.Name} out of stock");

                oi.Item.InventoryQuantity -= oi.Quantity;

                _db.InventoryLogs.Add(new InventoryLog
                {
                    Id = Guid.NewGuid(),
                    CampusId = order.CampusId,
                    ItemId = oi.ItemId,
                    ChangeQuantity = -oi.Quantity,
                    Reason = InventoryLogReason.Sale,
                    ReferenceId = order.Id,
                    PerformedByUserId = performedByUserId,
                    CreatedAt = DateTime.UtcNow
                });

                await _notifier.MenuItemStockChanged(
                    order.CampusId,
                    oi.ItemId,
                    oi.Item.InventoryQuantity
                );

                await _db.SaveChangesAsync();
            }
        }
    }
}
