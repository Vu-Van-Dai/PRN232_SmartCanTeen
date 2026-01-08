using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Text.RegularExpressions;

namespace API.Hubs
{
    [Authorize(Roles = "Manager")]
    public class ManagementHub : Hub
    {
        public async Task JoinCampus(string campusId)
        {
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                $"campus-{campusId}"
            );
        }

        public async Task LeaveCampus(string campusId)
        {
            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId,
                $"campus-{campusId}"
            );
        }
    }
}
