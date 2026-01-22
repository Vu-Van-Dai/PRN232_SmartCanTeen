using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Text.RegularExpressions;

namespace API.Hubs
{
    [Authorize(Roles = "Staff")]
    public class KitchenHub : Hub
    {
        public async Task JoinCampus(string campusId)
        {
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                $"campus-{campusId}"
            );
        }
    }
}
