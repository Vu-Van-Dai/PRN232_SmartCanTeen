using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Orders
{
public class OrderSchedulerService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public OrderSchedulerService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task<List<Guid>> ProcessOrders()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var now = DateTime.UtcNow;

            const int autoStartMinutes = 10;

            var orders = await db.Orders
                .Where(x =>
                    x.Status == OrderStatus.SystemHolding &&
                    x.PickupTime != null &&
                    x.PickupTime.Value.AddMinutes(-autoStartMinutes) <= now
                )
                .ToListAsync();

            foreach (var order in orders)
            {
                order.Status = OrderStatus.Preparing;

                // Pre-orders are not urgent; urgent is reserved for immediate orders (PickupTime == null).
                order.IsUrgent = order.PickupTime == null;

                // Keep station tasks consistent so boards & student progress update immediately.
                var orderCategoryIds = await db.OrderItems
                    .AsNoTracking()
                    .Where(oi => oi.OrderId == order.Id)
                    .Select(oi => oi.Item.CategoryId)
                    .Distinct()
                    .ToListAsync();

                if (orderCategoryIds.Count == 0)
                    continue;

                var screenIds = await db.DisplayScreenCategories
                    .AsNoTracking()
                    .Where(sc => orderCategoryIds.Contains(sc.CategoryId))
                    .Select(sc => sc.ScreenId)
                    .Distinct()
                    .ToListAsync();

                if (screenIds.Count == 0)
                    continue;

                var existingTasks = await db.OrderStationTasks
                    .Where(t => t.OrderId == order.Id)
                    .ToListAsync();

                foreach (var task in existingTasks)
                {
                    if (task.Status == StationTaskStatus.Pending)
                    {
                        task.Status = StationTaskStatus.Preparing;
                        task.StartedAt ??= now;
                    }
                }

                var existingScreenIds = existingTasks.Select(t => t.ScreenId).ToHashSet();
                var missingScreenIds = screenIds.Where(id => !existingScreenIds.Contains(id)).ToList();
                if (missingScreenIds.Count == 0)
                    continue;

                var activeScreens = await db.DisplayScreens
                    .AsNoTracking()
                    .Where(s => missingScreenIds.Contains(s.Id) && s.IsActive)
                    .Select(s => s.Id)
                    .ToListAsync();

                foreach (var screenId in activeScreens)
                {
                    db.OrderStationTasks.Add(new OrderStationTask
                    {
                        OrderId = order.Id,
                        ScreenId = screenId,
                        Status = StationTaskStatus.Preparing,
                        StartedAt = now,
                    });
                }
            }

            await db.SaveChangesAsync();

            // ✅ CHỈ TRẢ ID RA NGOÀI
            return orders.Select(x => x.Id).ToList();
        }
    }

}
