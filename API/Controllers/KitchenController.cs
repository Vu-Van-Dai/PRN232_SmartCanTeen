using API.Hubs;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace API.Controllers
{
    [ApiController]
    [Route("api/kitchen")]
    [Authorize(Roles = "Staff,StaffKitchen,Manager")]
    public class KitchenController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHubContext<OrderHub> _orderHub;

        public KitchenController(
            AppDbContext db,
            IHubContext<OrderHub> orderHub)
        {
            _db = db;
            _orderHub = orderHub;
        }

        /// <summary>
        /// Danh sách đơn cho màn hình bếp
        /// </summary>
        [HttpGet("orders")]
        public async Task<IActionResult> GetKitchenOrders()
        {
            var now = DateTime.UtcNow;

            var urgent = await _db.Orders
                .Where(x =>
                    x.Status == OrderStatus.Preparing &&
                    x.IsUrgent
                )
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();

            var upcoming = await _db.Orders
                .Where(x =>
                    x.Status == OrderStatus.SystemHolding &&
                    x.PickupTime != null &&
                    x.PickupTime <= now.AddMinutes(30)
                )
                .OrderBy(x => x.PickupTime)
                .ToListAsync();

            return Ok(new { urgent, upcoming });
        }

        /// <summary>
        /// Bếp bấm "Nấu"
        /// </summary>
        [HttpPost("{orderId}/prepare")]
        public async Task<IActionResult> StartCooking(Guid orderId)
        {
            var order = await _db.Orders.FirstOrDefaultAsync(x =>
                x.Id == orderId &&
                (x.Status == OrderStatus.SystemHolding ||
                 x.Status == OrderStatus.Paid)
            );

            if (order == null)
                return BadRequest("Invalid order");

            order.Status = OrderStatus.Preparing;
            order.IsUrgent = true;

            await _db.SaveChangesAsync();
            return Ok();
        }

        /// <summary>
        /// Bếp bấm "Xong"
        /// </summary>
        [HttpPost("{orderId}/ready")]
        public async Task<IActionResult> MarkReady(Guid orderId)
        {
            var order = await _db.Orders.FirstOrDefaultAsync(x =>
                x.Id == orderId &&
                x.Status == OrderStatus.Preparing
            );

            if (order == null)
                return BadRequest("Invalid order");

            order.Status = OrderStatus.Ready;
            await _db.SaveChangesAsync();

            // 🔔 THÔNG BÁO CHO SINH VIÊN
            await _orderHub.Clients
                .User(order.OrderedByUserId.ToString())
                .SendAsync("OrderReady", new
                {
                    orderId = order.Id,
                    pickupTime = order.PickupTime
                });

            return Ok();
        }
    }
}
