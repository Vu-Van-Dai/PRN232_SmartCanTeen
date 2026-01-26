using API.Hubs;
using Application.DTOs;
using Application.Payments;
using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace API.Controllers
{
    [ApiController]
    [Route("api/pos/orders")]
    [Authorize(Roles = "Staff,StaffPOS,Manager")]
    public class PosOrderController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly PayosService _payos;
        private readonly IHubContext<ManagementHub> _managementHub;

        public PosOrderController(
            AppDbContext db,
            PayosService payos,
            IHubContext<ManagementHub> managementHub)
        {
            _db = db;
            _payos = payos;
            _managementHub = managementHub;
        }
        //helper
        public async Task<bool> IsDayLocked(DateTime date)
        {
            return await _db.DailyRevenues.AnyAsync(x =>
                x.Date == date.Date
            );
        }

        /// <summary>
        /// Tạo Order OFFLINE + QR
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create(CreateOfflineOrderRequest request)
        {
            if (request.TotalPrice <= 0 || request.Items.Count == 0)
                return BadRequest("Invalid order data");

            if (request.TotalPrice != Math.Floor(request.TotalPrice))
                return BadRequest("Amount must be an integer (VND)");

            var staffId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!
            );

            var today = DateTime.UtcNow.Date;

            var dayLocked = await _db.DailyRevenues.AnyAsync(x =>
                x.Date == today
            );

            if (dayLocked)
                return BadRequest("Day already closed");

            // 1️⃣ LẤY CA ĐANG MỞ
            var shift = await _db.Shifts.FirstOrDefaultAsync(x =>
                x.UserId == staffId &&
                x.Status == ShiftStatus.Open
            );

            if (shift == null)
                return BadRequest("No open shift or shift is locked");

            // 2️⃣ TẠO ORDER
            var order = new Order
            {
                Id = Guid.NewGuid(),
                ShiftId = shift.Id,
                OrderedByUserId = staffId,

                OrderSource = OrderSource.Offline,
                PaymentMethod = PaymentMethod.Qr,
                Status = OrderStatus.Pending,

                PickupTime = null,
                IsUrgent = true,

                SubTotal = request.TotalPrice,
                DiscountAmount = 0,
                TotalPrice = request.TotalPrice,
                CreatedAt = DateTime.UtcNow
            };

            _db.Orders.Add(order);

            // 3️⃣ TẠO ORDER ITEMS (snapshot giá)
            var itemIds = request.Items.Select(x => x.ItemId).ToList();

            var menuItems = await _db.MenuItems
                .Where(x =>
                    itemIds.Contains(x.Id) &&
                    !x.IsDeleted &&
                    x.IsActive
                )
                .ToDictionaryAsync(x => x.Id);

            foreach (var i in request.Items)
            {
                if (!menuItems.TryGetValue(i.ItemId, out var item))
                    return BadRequest($"Item {i.ItemId} not found");

                _db.OrderItems.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ItemId = item.Id,
                    Quantity = i.Quantity,
                    UnitPrice = item.Price
                });
            }

            // 4️⃣ TẠO PAYOS PAYMENT LINK (store mapping in existing table)
            var orderCode = _payos.GenerateOrderCode();
            var payRef = $"PAYOS-{orderCode}";

            _db.PaymentTransactions.Add(new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                PerformedByUserId = staffId,
                PaymentRef = payRef,
                Amount = request.TotalPrice,
                Purpose = PaymentPurpose.OfflineOrder,
                OrderId = order.Id,
                ShiftId = shift.Id,
                IsSuccess = false,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            await _managementHub.Clients
                .All
                .SendAsync("OrderCreated", new
                {
                    orderId = order.Id,
                    shiftId = shift.Id,
                    total = order.TotalPrice,
                    method = PaymentMethod.Qr
                });

            // 5️⃣ TẠO CHECKOUT URL (PayOS)
            var amountInt = checked((int)request.TotalPrice);
            var description = $"SC OFF {orderCode}";
            var origin = Request.Headers.Origin.ToString();
            if (string.IsNullOrWhiteSpace(origin))
            {
                var referer = Request.Headers.Referer.ToString();
                if (Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
                    origin = refererUri.GetLeftPart(UriPartial.Authority);
            }

            var returnUrl = string.IsNullOrWhiteSpace(origin) ? null : $"{origin}/payos/return";
            var cancelUrl = string.IsNullOrWhiteSpace(origin) ? null : $"{origin}/payos/cancel";
            var link = await _payos.CreatePaymentLinkAsync(amountInt, orderCode, description, returnUrl, cancelUrl);

            return Ok(new
            {
                orderId = order.Id,
                qrUrl = link.CheckoutUrl
            });
        }

        /// <summary>
        /// Tạo Order OFFLINE + CASH (thu tiền mặt tại quầy)
        /// </summary>
        [HttpPost("cash")]
        public async Task<IActionResult> CreateCash(CreateOfflineOrderRequest request)
        {
            if (request.TotalPrice <= 0 || request.Items.Count == 0)
                return BadRequest("Invalid order data");

            if (request.TotalPrice != Math.Floor(request.TotalPrice))
                return BadRequest("Amount must be an integer (VND)");

            var staffId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var today = DateTime.UtcNow.Date;
            var dayLocked = await _db.DailyRevenues.AnyAsync(x => x.Date == today);
            if (dayLocked)
                return BadRequest("Day already closed");

            var shift = await _db.Shifts.FirstOrDefaultAsync(x =>
                x.UserId == staffId &&
                x.Status == ShiftStatus.Open);

            if (shift == null)
                return BadRequest("No open shift or shift is locked");

            var order = new Order
            {
                Id = Guid.NewGuid(),
                ShiftId = shift.Id,
                OrderedByUserId = staffId,

                OrderSource = OrderSource.Offline,
                PaymentMethod = PaymentMethod.Cash,
                Status = OrderStatus.Preparing,

                PickupTime = null,
                IsUrgent = true,

                SubTotal = request.TotalPrice,
                DiscountAmount = 0,
                TotalPrice = request.TotalPrice,
                CreatedAt = DateTime.UtcNow
            };

            _db.Orders.Add(order);

            var itemIds = request.Items.Select(x => x.ItemId).ToList();
            var menuItems = await _db.MenuItems
                .Where(x =>
                    itemIds.Contains(x.Id) &&
                    !x.IsDeleted &&
                    x.IsActive)
                .ToDictionaryAsync(x => x.Id);

            foreach (var i in request.Items)
            {
                if (!menuItems.TryGetValue(i.ItemId, out var item))
                    return BadRequest($"Item {i.ItemId} not found");

                _db.OrderItems.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ItemId = item.Id,
                    Quantity = i.Quantity,
                    UnitPrice = item.Price
                });
            }

            // Update shift totals immediately for cash
            shift.SystemCashTotal += request.TotalPrice;

            await _db.SaveChangesAsync();

            await _managementHub.Clients
                .All
                .SendAsync("OrderPaid", new
                {
                    orderId = order.Id,
                    shiftId = shift.Id,
                    amount = order.TotalPrice,
                    method = PaymentMethod.Cash
                });

            await _managementHub.Clients
                .All
                .SendAsync("OrderCreated", new
                {
                    orderId = order.Id,
                    shiftId = shift.Id,
                    total = order.TotalPrice,
                    method = PaymentMethod.Cash
                });

            return Ok(new { orderId = order.Id });
        }
    }
}
