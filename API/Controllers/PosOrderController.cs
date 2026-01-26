using API.Hubs;
using Application.DTOs;
using Application.Orders;
using Application.Payments;
using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using API.Services;

namespace API.Controllers
{
    [ApiController]
    [Route("api/pos/orders")]
    [Authorize(Roles = "Staff,StaffPOS,Manager")]
    public class PosOrderController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly PayosService _payos;
        private readonly InventoryService _inventoryService;
        private readonly IHubContext<ManagementHub> _managementHub;
        private readonly PayosPaymentProcessor _processor;
        private readonly IMemoryCache _cache;

        public PosOrderController(
            AppDbContext db,
            PayosService payos,
            InventoryService inventoryService,
            IHubContext<ManagementHub> managementHub,
            PayosPaymentProcessor processor,
            IMemoryCache cache)
        {
            _db = db;
            _payos = payos;
            _inventoryService = inventoryService;
            _managementHub = managementHub;
            _processor = processor;
            _cache = cache;
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
                // Backward-compatible field (older FE used this to redirect to PayOS)
                qrUrl = link.CheckoutUrl,

                // New fields for in-app POS QR modal
                checkoutUrl = link.CheckoutUrl,
                qrCode = link.QrCode,
                orderCode = link.OrderCode
            });
        }

        /// <summary>
        /// Poll payment status for a POS order (used by in-app QR modal).
        /// </summary>
        [HttpGet("{orderId:guid}/payment-status")]
        public async Task<IActionResult> GetPaymentStatus(Guid orderId)
        {
            var staffId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null)
                return NotFound("Order not found");

            if (order.OrderSource != OrderSource.Offline)
                return BadRequest("Order is not offline");

            if (order.OrderedByUserId != staffId)
                return Forbid();

            // For QR payments, the PaymentTransaction will be marked success by webhook/confirm.
            var txn = await _db.PaymentTransactions
                .Where(x => x.OrderId == orderId && x.Purpose == PaymentPurpose.OfflineOrder)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

            var isPaid = order.Status != OrderStatus.Pending && order.Status != OrderStatus.Cancelled;
            if (txn != null && txn.IsSuccess)
                isPaid = true;

            // If webhook/return-callback didn't happen (common when user scans QR in a banking app),
            // we can query PayOS status on-demand and mark the transaction paid.
            if (!isPaid && txn != null && !txn.IsSuccess && !string.IsNullOrWhiteSpace(txn.PaymentRef))
            {
                var orderCode = TryParsePayosOrderCode(txn.PaymentRef);
                if (orderCode != null)
                {
                    var cacheKey = $"payos:last-check:{orderCode.Value}";
                    var now = DateTimeOffset.UtcNow;
                    var last = _cache.Get<DateTimeOffset?>(cacheKey);

                    // Throttle external calls (PayOS rate limit protection)
                    if (last == null || (now - last.Value) >= TimeSpan.FromSeconds(6))
                    {
                        _cache.Set(cacheKey, now, new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
                        });

                        var info = await _payos.GetPaymentLinkInfoAsync(orderCode.Value, HttpContext.RequestAborted);
                        if (info?.Status != null && string.Equals(info.Status, "PAID", StringComparison.OrdinalIgnoreCase))
                        {
                            var processed = await _processor.TryMarkPaidAsync(orderCode.Value, HttpContext.RequestAborted);
                            if (processed)
                                isPaid = true;
                        }
                    }
                }
            }

            return Ok(new
            {
                orderId = order.Id,
                status = order.Status.ToString(),
                paymentMethod = order.PaymentMethod.ToString(),
                isPaid
            });
        }

        private static int? TryParsePayosOrderCode(string paymentRef)
        {
            // Stored as: PAYOS-{orderCode}
            const string prefix = "PAYOS-";
            if (!paymentRef.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;

            var raw = paymentRef.Substring(prefix.Length);
            if (int.TryParse(raw, out var code) && code > 0)
                return code;

            return null;
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

            using var dbTx = await _db.Database.BeginTransactionAsync();

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

            try
            {
                await _db.SaveChangesAsync();
                await _inventoryService.DeductInventoryAsync(order, staffId);
                await dbTx.CommitAsync();
            }
            catch (Exception ex)
            {
                await dbTx.RollbackAsync();
                return BadRequest(ex.Message);
            }

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

        /// <summary>
        /// Chuyển 1 order POS đang Pending (đã tạo khi bấm QR) sang thanh toán CASH.
        /// Tránh tạo order mới khi khách hủy QR và đổi sang tiền mặt.
        /// </summary>
        [HttpPost("{orderId:guid}/cash")]
        public async Task<IActionResult> PayExistingOrderByCash(Guid orderId)
        {
            var staffId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null)
                return NotFound("Order not found");

            if (order.OrderSource != OrderSource.Offline)
                return BadRequest("Order is not offline");

            if (order.Status != OrderStatus.Pending)
                return BadRequest("Order is not pending");

            if (order.ShiftId == null)
                return BadRequest("Order has no shift");

            if (order.OrderedByUserId != staffId)
                return Forbid();

            var shift = await _db.Shifts.FirstOrDefaultAsync(s => s.Id == order.ShiftId && s.Status == ShiftStatus.Open);
            if (shift == null)
                return BadRequest("Shift is not open");

            using var dbTx = await _db.Database.BeginTransactionAsync();

            try
            {
                order.PaymentMethod = PaymentMethod.Cash;
                order.Status = OrderStatus.Preparing;
                order.IsUrgent = true;

                shift.SystemCashTotal += order.TotalPrice;

                await _db.SaveChangesAsync();
                await _inventoryService.DeductInventoryAsync(order, staffId);

                await dbTx.CommitAsync();
            }
            catch (Exception ex)
            {
                await dbTx.RollbackAsync();
                return BadRequest(ex.Message);
            }

            await _managementHub.Clients.All.SendAsync("OrderPaid", new
            {
                orderId = order.Id,
                shiftId = order.ShiftId,
                amount = order.TotalPrice,
                method = PaymentMethod.Cash
            });

            return Ok();
        }

        /// <summary>
        /// Hủy 1 order POS đang Pending (void) nếu khách không thanh toán.
        /// </summary>
        [HttpPost("{orderId:guid}/cancel")]
        public async Task<IActionResult> CancelExistingOrder(Guid orderId)
        {
            var staffId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null)
                return NotFound("Order not found");

            if (order.OrderSource != OrderSource.Offline)
                return BadRequest("Order is not offline");

            if (order.Status != OrderStatus.Pending)
                return BadRequest("Order is not pending");

            if (order.OrderedByUserId != staffId)
                return Forbid();

            order.Status = OrderStatus.Cancelled;
            await _db.SaveChangesAsync();

            return Ok();
        }
    }
}
