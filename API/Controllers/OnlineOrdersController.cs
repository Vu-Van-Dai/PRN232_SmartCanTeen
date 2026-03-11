using Application.DTOs;
using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using API.Services;

namespace API.Controllers
{
    [ApiController]
    [Route("api/online/orders")]
    [Authorize(Roles = "Student,Parent")]
    public class OnlineOrdersController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly BusinessDayGate _dayGate;
        private readonly BusinessDayClock _clock;
        private readonly PromotionEngine _promoEngine;

        public OnlineOrdersController(AppDbContext db, BusinessDayGate dayGate, BusinessDayClock clock, PromotionEngine promoEngine)
        {
            _db = db;
            _dayGate = dayGate;
            _clock = clock;
            _promoEngine = promoEngine;
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateOnlineOrderRequest request)
        {
            if (request.Items.Count == 0)
                return BadRequest("Empty order");

            if (request.PickupTime != null)
            {
                var pickupUtc = request.PickupTime.Value;
                if (pickupUtc.Kind != DateTimeKind.Utc)
                    pickupUtc = DateTime.SpecifyKind(pickupUtc, DateTimeKind.Utc);

                var minUtc = DateTime.UtcNow.AddMinutes(2);
                if (pickupUtc < minUtc)
                    return BadRequest("Thời gian nhận phải sau hiện tại ít nhất 2 phút.");

                var pickupLocal = _clock.ConvertUtcToLocal(pickupUtc);
                var tod = pickupLocal.TimeOfDay;
                var open = TimeSpan.FromHours(6);
                var close = TimeSpan.FromHours(22);
                if (tod < open || tod > close)
                    return BadRequest("Nhà ăn chỉ nhận đặt trước trong khung 06:00–22:00. Vui lòng chọn lại.");
            }

            var gate = await _dayGate.EnsurePosOperationsAllowedAsync();
            if (!gate.allowed)
                return BadRequest(gate.reason);

            var userId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!
            );

            // LẤY MENU ITEM
            if (request.Items.Any(x => x.Quantity <= 0))
                return BadRequest("Invalid quantity");

            var (quote, quoteError) = await _promoEngine.QuoteAsync(
                request.Items.Select(x => (x.ItemId, x.Quantity)),
                request.PromoCode,
                HttpContext.RequestAborted);

            if (quoteError != null)
                return BadRequest(quoteError);

            var finalLines = quote!.Items;

            var itemIds = finalLines.Select(x => x.itemId).Distinct().ToList();
            var menuItems = await _db.MenuItems
                .Where(x =>
                    itemIds.Contains(x.Id) &&
                    x.IsActive &&
                    !x.IsDeleted)
                .ToDictionaryAsync(x => x.Id);

            const decimal vatRate = 0.08m;
            var grossTotal = quote.GrossTotal;
            var discount = quote.DiscountAmount;

            var order = new Order
            {
                Id = Guid.NewGuid(),
                OrderedByUserId = userId,

                OrderSource = OrderSource.Online,
                PaymentMethod = PaymentMethod.Wallet,
                Status = OrderStatus.Pending,

                PickupTime = request.PickupTime,
                IsUrgent = request.PickupTime == null,

                SubTotal = 0,
                DiscountAmount = 0,
                TotalPrice = 0,

                CreatedAt = DateTime.UtcNow
            };

            _db.Orders.Add(order);

            foreach (var i in finalLines)
            {
                if (!menuItems.TryGetValue(i.itemId, out var item))
                    return BadRequest($"Item {i.itemId} not found");

                _db.OrderItems.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ItemId = item.Id,
                    Quantity = i.quantity,
                    CancelledQuantity = 0,
                    Status = item.ProductType == ProductType.ReadyMade ? OrderItemStatus.Completed : OrderItemStatus.Pending,
                    UnitPrice = item.Price
                });
            }

            var total = quote.Total;

            var vatAmount = decimal.Round(total * (vatRate / (1m + vatRate)), 0, MidpointRounding.AwayFromZero);
            if (vatAmount < 0) vatAmount = 0;
            if (vatAmount > total) vatAmount = total;

            var baseAmount = total - vatAmount;

            order.SubTotal = baseAmount;
            order.DiscountAmount = discount;
            order.TotalPrice = total;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                orderId = order.Id,
                total = order.TotalPrice,
                discountAmount = order.DiscountAmount,
                appliedPromotionCode = quote.AppliedPromotionCode,
                appliedPromotionName = quote.AppliedPromotionName
            });
        }
    }
}
