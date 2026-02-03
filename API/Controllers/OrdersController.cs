using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace API.Controllers
{
    [ApiController]
    [Route("api/orders")]
    [Authorize(Roles = "Student,Parent")]
    public class OrdersController : ControllerBase
    {
        private readonly AppDbContext _db;

        public OrdersController(AppDbContext db)
        {
            _db = db;
        }

        // Student/Parent: list my orders (online)
        [HttpGet("me")]
        public async Task<IActionResult> GetMyOrders()
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var orders = await _db.Orders
                .AsNoTracking()
                .Where(o => o.OrderedByUserId == userId && o.OrderSource == OrderSource.Online)
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.Item)
                .OrderByDescending(o => o.CreatedAt)
                .Take(50)
                .Select(o => new
                {
                    id = o.Id,
                    createdAt = o.CreatedAt,
                    pickupTime = o.PickupTime,
                    status = (int)o.Status,
                    totalPrice = o.TotalPrice,
                    items = o.Items.Select(i => new
                    {
                        itemId = i.ItemId,
                        name = i.Item.Name,
                        quantity = i.Quantity,
                        unitPrice = i.UnitPrice
                    }),
                    stationTasks = _db.OrderStationTasks
                        .AsNoTracking()
                        .Where(t => t.OrderId == o.Id)
                        .OrderBy(t => t.Screen.Name)
                        .Select(t => new
                        {
                            screenKey = t.Screen.Key,
                            screenName = t.Screen.Name,
                            status = (int)t.Status,
                            startedAt = t.StartedAt,
                            readyAt = t.ReadyAt,
                            completedAt = t.CompletedAt,
                        })
                        .ToList(),
                    pickedAtCounter = (string?)null
                })
                .ToListAsync();

            return Ok(orders);
        }
    }
}
