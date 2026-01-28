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
    [Authorize(Roles = "Staff,StaffKitchen,StaffCoordination,Manager,AdminSystem")]
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

            var pending = await _db.Orders
                .AsNoTracking()
                .Where(x => x.Status == OrderStatus.SystemHolding || x.Status == OrderStatus.Paid)
                .Include(x => x.OrderedByUser)
                .Include(x => x.Items)
                    .ThenInclude(i => i.Item)
                .OrderBy(x => x.PickupTime ?? x.CreatedAt)
                .Select(x => new
                {
                    id = x.Id,
                    createdAt = x.CreatedAt,
                    pickupTime = x.PickupTime,
                    status = x.Status.ToString(),
                    isUrgent = x.IsUrgent,
                    totalPrice = x.TotalPrice,
                    orderedBy = x.OrderedByUser.FullName ?? x.OrderedByUser.Email,
                    items = x.Items.Select(i => new
                    {
                        itemId = i.ItemId,
                        name = i.Item.Name,
                        quantity = i.Quantity,
                        unitPrice = i.UnitPrice,
                    })
                })
                .ToListAsync();

            var preparing = await _db.Orders
                .AsNoTracking()
                .Where(x => x.Status == OrderStatus.Preparing)
                .Include(x => x.OrderedByUser)
                .Include(x => x.Items)
                    .ThenInclude(i => i.Item)
                .OrderBy(x => x.CreatedAt)
                .Select(x => new
                {
                    id = x.Id,
                    createdAt = x.CreatedAt,
                    pickupTime = x.PickupTime,
                    status = x.Status.ToString(),
                    isUrgent = x.IsUrgent,
                    totalPrice = x.TotalPrice,
                    orderedBy = x.OrderedByUser.FullName ?? x.OrderedByUser.Email,
                    items = x.Items.Select(i => new
                    {
                        itemId = i.ItemId,
                        name = i.Item.Name,
                        quantity = i.Quantity,
                        unitPrice = i.UnitPrice,
                    })
                })
                .ToListAsync();

            var ready = await _db.Orders
                .AsNoTracking()
                .Where(x => x.Status == OrderStatus.Ready)
                .Include(x => x.OrderedByUser)
                .Include(x => x.Items)
                    .ThenInclude(i => i.Item)
                .OrderBy(x => x.CreatedAt)
                .Select(x => new
                {
                    id = x.Id,
                    createdAt = x.CreatedAt,
                    pickupTime = x.PickupTime,
                    status = x.Status.ToString(),
                    isUrgent = x.IsUrgent,
                    totalPrice = x.TotalPrice,
                    orderedBy = x.OrderedByUser.FullName ?? x.OrderedByUser.Email,
                    items = x.Items.Select(i => new
                    {
                        itemId = i.ItemId,
                        name = i.Item.Name,
                        quantity = i.Quantity,
                        unitPrice = i.UnitPrice,
                    })
                })
                .ToListAsync();

            var urgent = preparing
                .Where(x => x.isUrgent)
                .OrderBy(x => x.createdAt)
                .ToList();

            var upcoming = pending
                .Where(x =>
                    x.pickupTime != null &&
                    x.pickupTime > now &&
                    x.pickupTime <= now.AddMinutes(60)
                )
                .OrderBy(x => x.pickupTime)
                .ToList();

            return Ok(new { pending, preparing, ready, urgent, upcoming });
        }

        /// <summary>
        /// Bếp bấm "Nấu"
        /// </summary>
        [HttpPost("{orderId}/prepare")]
        [Authorize(Roles = "StaffKitchen,Manager,AdminSystem")]
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
        [Authorize(Roles = "StaffKitchen,Manager,AdminSystem")]
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

        /// <summary>
        /// Nhân viên điều phối bấm "Đã giao" (đơn biến mất khỏi board)
        /// </summary>
        [HttpPost("{orderId}/complete")]
        [Authorize(Roles = "StaffCoordination,Manager,AdminSystem")]
        public async Task<IActionResult> Complete(Guid orderId)
        {
            var order = await _db.Orders.FirstOrDefaultAsync(x =>
                x.Id == orderId &&
                x.Status == OrderStatus.Ready
            );

            if (order == null)
                return BadRequest("Invalid order");

            order.Status = OrderStatus.Completed;
            await _db.SaveChangesAsync();
            return Ok();
        }
    }
}
