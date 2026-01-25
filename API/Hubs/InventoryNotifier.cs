using Core.Common;
using Microsoft.AspNetCore.SignalR;

namespace API.Hubs
{
    public class InventoryNotifier : IInventoryNotifier
    {
        private readonly IHubContext<ManagementHub> _hub;

        public InventoryNotifier(IHubContext<ManagementHub> hub)
        {
            _hub = hub;
        }

        public async Task MenuItemStockChanged(
            Guid itemId,
            int inventoryQuantity)
        {
            await _hub.Clients.All.SendAsync("MenuItemStockChanged", new
            {
                itemId,
                inventoryQuantity
            });
        }
    }
}
