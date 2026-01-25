using Application.Orders;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace API.Hubs
{
    public class OrderSchedulerHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHubContext<ManagementHub> _hub;

        public OrderSchedulerHostedService(
            IServiceScopeFactory scopeFactory,
            IHubContext<ManagementHub> hub)
        {
            _scopeFactory = scopeFactory;
            _hub = hub;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Process();
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task Process()
        {
            using var scope = _scopeFactory.CreateScope();

            var scheduler = scope.ServiceProvider
                .GetRequiredService<OrderSchedulerService>();

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var orderIds = await scheduler.ProcessOrders();

            if (!orderIds.Any())
                return;

            var orders = await db.Orders
                .Where(x => orderIds.Contains(x.Id))
                .ToListAsync();

            foreach (var order in orders)
            {
                await _hub.Clients.All.SendAsync("NewKitchenOrder", new
                {
                    orderId = order.Id,
                    pickupTime = order.PickupTime
                });
            }
        }
    }
}
