using Application.DTOs;
using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace API.Controllers
{
    [ApiController]
    [Route("api/online/orders")]
    [Authorize(Roles = "Student,Parent")]
    public class OnlineOrdersController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ICurrentCampusService _campus;

        public OnlineOrdersController(
            AppDbContext db,
            ICurrentCampusService campus)
        {
            _db = db;
            _campus = campus;
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateOnlineOrderRequest request)
        {
            if (request.Items.Count == 0)
                return BadRequest("Empty order");

            var userId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!
            );

            // 🔒 CHẶN NẾU NGÀY ĐÃ CHỐT
            var today = DateTime.UtcNow.Date;
            var dayLocked = await _db.DailyRevenues.AnyAsync(x =>
                x.CampusId == _campus.CampusId &&
                x.Date == today
            );
            if (dayLocked)
                return BadRequest("Ordering is closed today");

            // LẤY MENU ITEM
            var itemIds = request.Items.Select(x => x.ItemId).ToList();
            var menuItems = await _db.MenuItems
                .Where(x =>
                    itemIds.Contains(x.Id) &&
                    x.CampusId == _campus.CampusId &&
                    x.IsActive &&
                    !x.IsDeleted)
                .ToDictionaryAsync(x => x.Id);

            decimal total = 0;

            var order = new Order
            {
                Id = Guid.NewGuid(),
                CampusId = _campus.CampusId,
                OrderedByUserId = userId,

                OrderSource = OrderSource.Online,
                PaymentMethod = PaymentMethod.Wallet,
                Status = OrderStatus.Pending,

                PickupTime = request.PickupTime,
                IsUrgent = request.PickupTime == null,

                SubTotal = total,
                TotalPrice = total,

                CreatedAt = DateTime.UtcNow
            };

            _db.Orders.Add(order);

            foreach (var i in request.Items)
            {
                if (!menuItems.TryGetValue(i.ItemId, out var item))
                    return BadRequest($"Item {i.ItemId} not found");

                total += item.Price * i.Quantity;

                _db.OrderItems.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ItemId = item.Id,
                    Quantity = i.Quantity,
                    UnitPrice = item.Price
                });
            }

            order.SubTotal = total;
            order.DiscountAmount = 0;
            order.TotalPrice = total;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                orderId = order.Id,
                total = order.TotalPrice
            });
        }
    }
}
