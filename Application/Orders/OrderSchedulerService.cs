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

            var orders = await db.Orders
                .Where(x =>
                    x.Status == OrderStatus.SystemHolding &&
                    x.PickupTime != null &&
                    x.PickupTime.Value.AddMinutes(-15) <= now
                )
                .ToListAsync();

            foreach (var order in orders)
            {
                order.Status = OrderStatus.Preparing;
                order.IsUrgent = true;
            }

            await db.SaveChangesAsync();

            // ✅ CHỈ TRẢ ID RA NGOÀI
            return orders.Select(x => x.Id).ToList();
        }
    }

}
