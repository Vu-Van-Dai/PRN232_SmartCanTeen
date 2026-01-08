using Microsoft.AspNetCore.SignalR;
using System.Text.RegularExpressions;

namespace API.Hubs
{
    public class OrderHub : Hub
    {
        public async Task JoinCampus(string campusId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, campusId);
        }

        public async Task JoinShift(string shiftId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, shiftId);
        }
        public async Task LeaveShift(string shiftId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, shiftId);
        }
    }
}
