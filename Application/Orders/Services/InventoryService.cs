using Core.Entities;
using Core.Enums;
using Microsoft.EntityFrameworkCore;
using Infrastructure.Persistence;

namespace Application.Orders.Services
{
    public class InventoryService
    {
        private readonly AppDbContext _db;

        public InventoryService(AppDbContext db)
        {
            _db = db;
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
            }
        }
    }
}
