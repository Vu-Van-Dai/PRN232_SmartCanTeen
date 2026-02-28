using API.Hubs;
using Application.DTOs;
using Application.Orders;
using Application.Payments;
using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
        private readonly BusinessDayGate _dayGate;

        public PosOrderController(
            AppDbContext db,
            PayosService payos,
            InventoryService inventoryService,
            IHubContext<ManagementHub> managementHub,
            PayosPaymentProcessor processor,
            IMemoryCache cache,
            BusinessDayGate dayGate)
        {
            _db = db;
            _payos = payos;
            _inventoryService = inventoryService;
            _managementHub = managementHub;
            _processor = processor;
            _cache = cache;
            _dayGate = dayGate;
        }


        /// <summary>
        /// Tạo Order OFFLINE + QR
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create(CreateOfflineOrderRequest request)
        {
            var gate = await _dayGate.EnsurePosOperationsAllowedAsync();
            if (!gate.allowed)
                return BadRequest(gate.reason);

            if (request.Items.Count == 0)
                return BadRequest("Invalid order data");

            if (request.Items.Any(x => x.Quantity <= 0))
                return BadRequest("Invalid quantity");

            var staffId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!
            );

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

                SubTotal = 0,
                DiscountAmount = 0,
                TotalPrice = 0,
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

                if (i.Quantity <= 0)
                    return BadRequest("Invalid quantity");

                _db.OrderItems.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ItemId = item.Id,
                    Quantity = i.Quantity,
                    CancelledQuantity = 0,
                    Status = item.ProductType == ProductType.ReadyMade ? OrderItemStatus.Completed : OrderItemStatus.Pending,
                    UnitPrice = item.Price
                });
            }

            // 3.5️⃣ Compute totals (VAT included in MenuItem.Price)
            const decimal vatRate = 0.08m;
            var grossTotal = request.Items.Sum(x => menuItems[x.ItemId].Price * x.Quantity);
            var discount = 0m;
            var total = decimal.Round(grossTotal - discount, 0, MidpointRounding.AwayFromZero);
            if (total < 0) total = 0;

            // VAT portion derived from final total (already VAT-inclusive)
            var vatAmount = decimal.Round(total * (vatRate / (1m + vatRate)), 0, MidpointRounding.AwayFromZero);
            if (vatAmount < 0) vatAmount = 0;
            if (vatAmount > total) vatAmount = total;

            var baseAmount = total - vatAmount;

            order.SubTotal = baseAmount;
            order.DiscountAmount = discount;
            order.TotalPrice = total;

            // QR: customer pays exact amount; no change.
            order.AmountReceived = order.TotalPrice;
            order.ChangeAmount = 0;

            // 4️⃣ TẠO PAYOS PAYMENT LINK (store mapping in existing table)
            var orderCode = _payos.GenerateOrderCode();
            var payRef = $"PAYOS-{orderCode}";

            _db.PaymentTransactions.Add(new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                PerformedByUserId = staffId,
                PaymentRef = payRef,
                Amount = order.TotalPrice,
                PaymentMethod = PaymentMethod.Qr,
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
            var amountInt = checked((int)order.TotalPrice);
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
            var gate = await _dayGate.EnsurePosOperationsAllowedAsync();
            if (!gate.allowed)
                return BadRequest(gate.reason);

            if (request.Items.Count == 0)
                return BadRequest("Invalid order data");

            if (request.Items.Any(x => x.Quantity <= 0))
                return BadRequest("Invalid quantity");

            var staffId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

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

                SubTotal = 0,
                DiscountAmount = 0,
                TotalPrice = 0,
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

                if (i.Quantity <= 0)
                    return BadRequest("Invalid quantity");

                _db.OrderItems.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ItemId = item.Id,
                    Quantity = i.Quantity,
                    CancelledQuantity = 0,
                    Status = item.ProductType == ProductType.ReadyMade ? OrderItemStatus.Completed : OrderItemStatus.Pending,
                    UnitPrice = item.Price
                });
            }

            // If this order has no Prepared items, it should not go to kitchen.
            var hasPreparedItems = request.Items.Any(x => menuItems[x.ItemId].ProductType == ProductType.Prepared);
            if (!hasPreparedItems)
            {
                order.Status = OrderStatus.Completed;
                order.IsUrgent = false;
            }

            // Compute totals (VAT included in MenuItem.Price)
            const decimal vatRate = 0.08m;
            var grossTotal = request.Items.Sum(x => menuItems[x.ItemId].Price * x.Quantity);
            var discount = 0m;
            var total = decimal.Round(grossTotal - discount, 0, MidpointRounding.AwayFromZero);
            if (total < 0) total = 0;

            var vatAmount = decimal.Round(total * (vatRate / (1m + vatRate)), 0, MidpointRounding.AwayFromZero);
            if (vatAmount < 0) vatAmount = 0;
            if (vatAmount > total) vatAmount = total;

            var baseAmount = total - vatAmount;

            order.SubTotal = baseAmount;
            order.DiscountAmount = discount;
            order.TotalPrice = total;

            // Cash: store tendered + change for receipt.
            if (request.AmountReceived != null)
            {
                if (request.AmountReceived < order.TotalPrice)
                    return BadRequest("AmountReceived must be >= total");

                order.AmountReceived = request.AmountReceived;
                order.ChangeAmount = request.ChangeAmount ?? (request.AmountReceived - order.TotalPrice);
            }
            else if (request.ChangeAmount != null)
            {
                if (request.ChangeAmount < 0)
                    return BadRequest("ChangeAmount must be >= 0");

                order.ChangeAmount = request.ChangeAmount;
                order.AmountReceived = order.TotalPrice + request.ChangeAmount;
            }
            else
            {
                order.AmountReceived = order.TotalPrice;
                order.ChangeAmount = 0;
            }

            // Update shift totals immediately for cash
            shift.SystemCashTotal += order.TotalPrice;

            try
            {
                await _db.SaveChangesAsync();

                // Cash payment transaction (for revenue ledger).
                _db.PaymentTransactions.Add(new PaymentTransaction
                {
                    Id = Guid.NewGuid(),
                    PerformedByUserId = staffId,
                    PaymentRef = $"CASH-{order.Id}",
                    Amount = order.TotalPrice,
                    PaymentMethod = PaymentMethod.Cash,
                    Purpose = PaymentPurpose.OfflineOrder,
                    OrderId = order.Id,
                    ShiftId = shift.Id,
                    IsSuccess = true,
                    CreatedAt = DateTime.UtcNow
                });

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
        public async Task<IActionResult> PayExistingOrderByCash(
            Guid orderId,
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PayPosOrderByCashRequest? request)
        {
            var gate = await _dayGate.EnsurePosOperationsAllowedAsync();
            if (!gate.allowed)
                return BadRequest(gate.reason);

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

                if (request?.AmountReceived != null)
                {
                    if (request.AmountReceived < order.TotalPrice)
                        return BadRequest("AmountReceived must be >= total");

                    order.AmountReceived = request.AmountReceived;
                    order.ChangeAmount = request.ChangeAmount ?? (request.AmountReceived - order.TotalPrice);
                }
                else if (request?.ChangeAmount != null)
                {
                    if (request.ChangeAmount < 0)
                        return BadRequest("ChangeAmount must be >= 0");

                    order.ChangeAmount = request.ChangeAmount;
                    order.AmountReceived = order.TotalPrice + request.ChangeAmount;
                }

                // Store receipt fields if missing (fallback values).
                order.AmountReceived ??= order.TotalPrice;
                order.ChangeAmount ??= 0;

                shift.SystemCashTotal += order.TotalPrice;

                // Cash payment transaction (for revenue ledger).
                _db.PaymentTransactions.Add(new PaymentTransaction
                {
                    Id = Guid.NewGuid(),
                    PerformedByUserId = staffId,
                    PaymentRef = $"CASH-CONVERT-{order.Id}",
                    Amount = order.TotalPrice,
                    PaymentMethod = PaymentMethod.Cash,
                    Purpose = PaymentPurpose.OfflineOrder,
                    OrderId = order.Id,
                    ShiftId = shift.Id,
                    IsSuccess = true,
                    CreatedAt = DateTime.UtcNow
                });

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
        /// Get order + refund summary for doing after-sale refunds at POS.
        /// </summary>
        [HttpGet("refund-info/{orderKey}")]
        public async Task<IActionResult> GetRefundInfoByKey(string orderKey)
        {
            orderKey = (orderKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(orderKey))
                return BadRequest("Order key is required");

            Order? order;
            if (Guid.TryParse(orderKey, out var parsedId))
            {
                order = await _db.Orders
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Id == parsedId);
            }
            else
            {
                // Allow matching by the first N chars of the UUID (hex-only), ignoring dashes.
                // This supports the 8-char code shown on receipts (e.g. 44d2a80d).
                var normalized = new string(orderKey
                    .Where(c => c != '-' && !char.IsWhiteSpace(c))
                    .ToArray())
                    .ToLowerInvariant();

                if (normalized.Length < 6 || normalized.Length > 32)
                    return BadRequest("Invalid order key");

                if (!normalized.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                    return BadRequest("Invalid order key");

                var len = normalized.Length;

                var matches = await _db.Orders
                    .FromSqlInterpolated($@"SELECT * FROM ""Orders"" WHERE LEFT(REPLACE(CAST(""Id"" AS text), '-', ''), {len}) = {normalized}")
                    .AsNoTracking()
                    .ToListAsync();

                if (matches.Count == 0)
                    order = null;
                else if (matches.Count > 1)
                    return BadRequest("Ambiguous order key; please enter full GUID");
                else
                    order = matches[0];
            }

            if (order == null)
                return NotFound("Order not found");

            if (order.OrderSource != OrderSource.Offline)
                return BadRequest("Only offline orders can be refunded at POS");

            var orderItems = await _db.OrderItems
                .AsNoTracking()
                .Include(x => x.Item)
                .Where(x => x.OrderId == order.Id)
                .Select(x => new
                {
                    orderItemId = x.Id,
                    itemId = x.ItemId,
                    name = x.Item.Name,
                    unitPrice = x.UnitPrice,
                    quantity = x.Quantity
                })
                .ToListAsync();

            var refundedQtyByItem = await _db.RefundReceiptItems
                .AsNoTracking()
                .Where(x => x.RefundReceipt.OriginalOrderId == order.Id)
                .GroupBy(x => x.OrderItemId)
                .Select(g => new { orderItemId = g.Key, refundedQuantity = g.Sum(v => v.Quantity) })
                .ToListAsync();

            var refundedQtyMap = refundedQtyByItem.ToDictionary(x => x.orderItemId, x => x.refundedQuantity);

            var items = orderItems.Select(x =>
            {
                var refundedQty = refundedQtyMap.TryGetValue(x.orderItemId, out var q) ? q : 0;
                var refundableQty = Math.Max(0, x.quantity - refundedQty);
                return new
                {
                    x.orderItemId,
                    x.itemId,
                    x.name,
                    x.unitPrice,
                    x.quantity,
                    refundedQuantity = refundedQty,
                    refundableQuantity = refundableQty
                };
            }).ToList();

            var refunds = await _db.RefundReceipts
                .AsNoTracking()
                .Where(r => r.OriginalOrderId == order.Id)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    refundReceiptId = r.Id,
                    refundAmount = r.RefundAmount,
                    amountReturned = r.AmountReturned,
                    refundMethod = r.RefundMethod.ToString(),
                    performedByUserId = r.PerformedByUserId,
                    createdAt = r.CreatedAt,
                    reason = r.Reason,
                })
                .ToListAsync();

            var refundedTotal = refunds.Sum(x => x.refundAmount);
            var refundableRemaining = Math.Max(0m, order.TotalPrice - refundedTotal);

            return Ok(new
            {
                orderId = order.Id,
                createdAt = order.CreatedAt,
                totalPrice = order.TotalPrice,
                paymentMethod = order.PaymentMethod.ToString(),
                status = order.Status.ToString(),
                amountReceived = order.AmountReceived,
                changeAmount = order.ChangeAmount,
                refundedTotal,
                refundableRemaining,
                refunds,
                items
            });
        }

        [HttpGet("{orderId:guid}/refund-info")]
        public async Task<IActionResult> GetRefundInfo(Guid orderId)
        {
            var order = await _db.Orders
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
                return NotFound("Order not found");

            if (order.OrderSource != OrderSource.Offline)
                return BadRequest("Only offline orders can be refunded at POS");

            var orderItems = await _db.OrderItems
                .AsNoTracking()
                .Include(x => x.Item)
                .Where(x => x.OrderId == orderId)
                .Select(x => new
                {
                    orderItemId = x.Id,
                    itemId = x.ItemId,
                    name = x.Item.Name,
                    unitPrice = x.UnitPrice,
                    quantity = x.Quantity
                })
                .ToListAsync();

            var refundedQtyByItem = await _db.RefundReceiptItems
                .AsNoTracking()
                .Where(x => x.RefundReceipt.OriginalOrderId == orderId)
                .GroupBy(x => x.OrderItemId)
                .Select(g => new { orderItemId = g.Key, refundedQuantity = g.Sum(v => v.Quantity) })
                .ToListAsync();

            var refundedQtyMap = refundedQtyByItem.ToDictionary(x => x.orderItemId, x => x.refundedQuantity);

            var items = orderItems.Select(x =>
            {
                var refundedQty = refundedQtyMap.TryGetValue(x.orderItemId, out var q) ? q : 0;
                var refundableQty = Math.Max(0, x.quantity - refundedQty);
                return new
                {
                    x.orderItemId,
                    x.itemId,
                    x.name,
                    x.unitPrice,
                    x.quantity,
                    refundedQuantity = refundedQty,
                    refundableQuantity = refundableQty
                };
            }).ToList();

            var refunds = await _db.RefundReceipts
                .AsNoTracking()
                .Where(r => r.OriginalOrderId == orderId)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    refundReceiptId = r.Id,
                    refundAmount = r.RefundAmount,
                    amountReturned = r.AmountReturned,
                    refundMethod = r.RefundMethod.ToString(),
                    performedByUserId = r.PerformedByUserId,
                    createdAt = r.CreatedAt,
                    reason = r.Reason,
                })
                .ToListAsync();

            var refundedTotal = refunds.Sum(x => x.refundAmount);
            var refundableRemaining = Math.Max(0m, order.TotalPrice - refundedTotal);

            return Ok(new
            {
                orderId = order.Id,
                createdAt = order.CreatedAt,
                totalPrice = order.TotalPrice,
                paymentMethod = order.PaymentMethod.ToString(),
                status = order.Status.ToString(),
                amountReceived = order.AmountReceived,
                changeAmount = order.ChangeAmount,
                refundedTotal,
                refundableRemaining,
                refunds,
                items
            });
        }

        /// <summary>
        /// Refund (after-sale) for a paid offline POS order. Creates a RefundReceipt + a negative PaymentTransaction.
        /// </summary>
        [HttpPost("{orderId:guid}/refund")]
        public async Task<IActionResult> RefundPaidOrder(Guid orderId, RefundPosOrderRequest request)
        {
            var gate = await _dayGate.EnsurePosOperationsAllowedAsync();
            if (!gate.allowed)
                return BadRequest(gate.reason);

            if (request.RefundAmount <= 0)
                return BadRequest("RefundAmount must be > 0");

            var staffId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var shift = await _db.Shifts.FirstOrDefaultAsync(x =>
                x.UserId == staffId &&
                x.Status == ShiftStatus.Open);

            if (shift == null)
                return BadRequest("No open shift or shift is locked");

            var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null)
                return NotFound("Order not found");

            if (order.OrderSource != OrderSource.Offline)
                return BadRequest("Only offline orders can be refunded at POS");

            if (order.Status == OrderStatus.Pending || order.Status == OrderStatus.Cancelled)
                return BadRequest("Order is not paid");

            // Do not allow refund when the whole order is already Ready (to avoid losing it from kitchen flow).
            if (order.Status == OrderStatus.Ready)
                return BadRequest("Order is ready; refund is not allowed");

            // Refund method must match the original payment method (per requirement)
            var refundMethod = order.PaymentMethod;

            var amountReturned = request.AmountReturned ?? request.RefundAmount;
            if (amountReturned < 0)
                return BadRequest("AmountReturned must be >= 0");
            if (amountReturned > request.RefundAmount)
                return BadRequest("AmountReturned must be <= RefundAmount");

            using var dbTx = await _db.Database.BeginTransactionAsync();
            try
            {
                // Re-check refundable remaining inside the transaction to avoid over-refunds.
                var refundedSoFar = await _db.RefundReceipts
                    .Where(r => r.OriginalOrderId == orderId)
                    .SumAsync(r => (decimal?)r.RefundAmount) ?? 0m;

                var remaining = order.TotalPrice - refundedSoFar;
                if (remaining <= 0)
                    return BadRequest("Nothing left to refund");

                if (request.RefundAmount > remaining)
                    return BadRequest("RefundAmount exceeds refundable remaining");

                // IMPORTANT: partial refund must not change Order.Status (otherwise KDS loses the order).
                // Only full refund sets OrderStatus = Refunded.
                var refundedTotalAfter = refundedSoFar + request.RefundAmount;
                if (refundedTotalAfter >= order.TotalPrice)
                    order.Status = OrderStatus.Refunded;

                var receipt = new RefundReceipt
                {
                    Id = Guid.NewGuid(),
                    OriginalOrderId = order.Id,
                    ShiftId = shift.Id,
                    RefundAmount = request.RefundAmount,
                    RefundMethod = refundMethod,
                    AmountReturned = amountReturned,
                    PerformedByUserId = staffId,
                    Reason = request.Reason?.Trim() ?? string.Empty,
                    CreatedAt = DateTime.UtcNow
                };
                _db.RefundReceipts.Add(receipt);

                _db.PaymentTransactions.Add(new PaymentTransaction
                {
                    Id = Guid.NewGuid(),
                    PerformedByUserId = staffId,
                    PaymentRef = $"REFUND-{receipt.Id}",
                    Amount = -request.RefundAmount,
                    PaymentMethod = refundMethod,
                    Purpose = PaymentPurpose.OfflineOrderRefund,
                    OrderId = order.Id,
                    ShiftId = shift.Id,
                    IsSuccess = true,
                    CreatedAt = DateTime.UtcNow
                });

                // Adjust shift totals for legacy reporting fields
                if (refundMethod == PaymentMethod.Cash)
                    shift.SystemCashTotal -= request.RefundAmount;
                else if (refundMethod == PaymentMethod.Qr)
                    shift.SystemQrTotal -= request.RefundAmount;
                else if (refundMethod == PaymentMethod.Wallet)
                    shift.SystemOnlineTotal -= request.RefundAmount;

                await _db.SaveChangesAsync();
                await dbTx.CommitAsync();

                return Ok(new
                {
                    refundReceiptId = receipt.Id,
                    orderId = order.Id,
                    refundAmount = receipt.RefundAmount,
                    amountReturned = receipt.AmountReturned,
                    refundMethod = receipt.RefundMethod.ToString(),
                    performedByUserId = receipt.PerformedByUserId,
                    createdAt = receipt.CreatedAt,
                    reason = receipt.Reason,
                });
            }
            catch (Exception ex)
            {
                await dbTx.RollbackAsync();
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Item-based refund (after-sale) for a paid offline POS order.
        /// Refund amount is computed from selected OrderItems (Quantity * UnitPrice).
        /// Creates a RefundReceipt + RefundReceiptItems + a negative PaymentTransaction.
        /// </summary>
        [HttpPost("{orderId:guid}/refund-items")]
        public async Task<IActionResult> RefundPaidOrderByItems(Guid orderId, RefundPosOrderItemsRequest request)
        {
            var gate = await _dayGate.EnsurePosOperationsAllowedAsync();
            if (!gate.allowed)
                return BadRequest(gate.reason);

            if (request.Items == null || request.Items.Count == 0)
                return BadRequest("Items are required");

            var staffId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var shift = await _db.Shifts.FirstOrDefaultAsync(x =>
                x.UserId == staffId &&
                x.Status == ShiftStatus.Open);

            if (shift == null)
                return BadRequest("No open shift or shift is locked");

            var order = await _db.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
                return NotFound("Order not found");

            if (order.OrderSource != OrderSource.Offline)
                return BadRequest("Only offline orders can be refunded at POS");

            if (order.Status == OrderStatus.Pending || order.Status == OrderStatus.Cancelled)
                return BadRequest("Order is not paid");

            // Do not allow refund when the whole order is already Ready (to avoid losing it from kitchen flow).
            if (order.Status == OrderStatus.Ready)
                return BadRequest("Order is ready; refund is not allowed");

            var refundMethod = order.PaymentMethod;

            using var dbTx = await _db.Database.BeginTransactionAsync();
            try
            {
                var refundedSoFar = await _db.RefundReceipts
                    .Where(r => r.OriginalOrderId == orderId)
                    .SumAsync(r => (decimal?)r.RefundAmount) ?? 0m;

                var remainingMoney = order.TotalPrice - refundedSoFar;
                if (remainingMoney <= 0)
                    return BadRequest("Nothing left to refund");

                var refundedQtyByItem = await _db.RefundReceiptItems
                    .Where(x => x.RefundReceipt.OriginalOrderId == orderId)
                    .GroupBy(x => x.OrderItemId)
                    .Select(g => new { OrderItemId = g.Key, RefundedQuantity = g.Sum(v => v.Quantity) })
                    .ToListAsync();

                var refundedQtyMap = refundedQtyByItem.ToDictionary(x => x.OrderItemId, x => x.RefundedQuantity);

                var requested = request.Items
                    .GroupBy(x => x.OrderItemId)
                    .Select(g => new { OrderItemId = g.Key, Quantity = g.Sum(v => v.Quantity) })
                    .ToList();

                if (requested.Count == 0)
                    return BadRequest("Items are required");

                decimal refundBaseGross = 0m;
                var receiptItems = new List<RefundReceiptItem>();

                var requestedQtyMap = requested.ToDictionary(x => x.OrderItemId, x => x.Quantity);

                foreach (var line in requested)
                {
                    if (line.Quantity <= 0)
                        return BadRequest("Quantity must be > 0");

                    var orderItem = order.Items.FirstOrDefault(i => i.Id == line.OrderItemId);
                    if (orderItem == null)
                        return BadRequest("Invalid OrderItemId");

                    var refundedQty = refundedQtyMap.TryGetValue(orderItem.Id, out var q) ? q : 0;
                    var remainingQty = orderItem.Quantity - refundedQty;
                    if (remainingQty <= 0)
                        return BadRequest("Item has no refundable quantity");
                    if (line.Quantity > remainingQty)
                        return BadRequest("Quantity exceeds refundable quantity");

                    refundBaseGross += (decimal)line.Quantity * orderItem.UnitPrice;

                    receiptItems.Add(new RefundReceiptItem
                    {
                        Id = Guid.NewGuid(),
                        OrderItemId = orderItem.Id,
                        Quantity = line.Quantity,
                        UnitPrice = orderItem.UnitPrice,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                if (refundBaseGross <= 0)
                    return BadRequest("RefundAmount must be > 0");

                // VAT is already included in OrderItem.UnitPrice; refund amount is sum of selected line totals.
                // When refunding all remaining quantities, refund the exact remainingMoney to avoid rounding leftovers.
                var refundAmount = refundBaseGross;
                if (refundAmount <= 0)
                    return BadRequest("RefundAmount must be > 0");

                var isAllItemsFullyRefunded = order.Items.All(oi =>
                {
                    var already = refundedQtyMap.TryGetValue(oi.Id, out var aq) ? aq : 0;
                    var req = requestedQtyMap.TryGetValue(oi.Id, out var rq) ? rq : 0;
                    return (already + req) >= oi.Quantity;
                });

                if (isAllItemsFullyRefunded)
                {
                    // Ensure VAT (and any remaining rounding diff) is fully reversed.
                    refundAmount = remainingMoney;
                }

                if (refundAmount > remainingMoney)
                    refundAmount = remainingMoney;

                var amountReturned = request.AmountReturned ?? refundAmount;
                if (amountReturned < 0)
                    return BadRequest("AmountReturned must be >= 0");
                if (amountReturned > refundAmount)
                    return BadRequest("AmountReturned must be <= RefundAmount");

                // Apply cancellation quantities for KDS display / audit.
                foreach (var line in requested)
                {
                    var orderItem = order.Items.First(i => i.Id == line.OrderItemId);
                    orderItem.CancelledQuantity += line.Quantity;
                    if (orderItem.CancelledQuantity > orderItem.Quantity)
                        orderItem.CancelledQuantity = orderItem.Quantity;

                    if (orderItem.CancelledQuantity >= orderItem.Quantity)
                        orderItem.Status = OrderItemStatus.Cancelled;
                }

                // IMPORTANT: partial refund must not change Order.Status (otherwise KDS loses the order).
                // Full refund sets OrderStatus = Refunded.
                var refundedTotalAfter = refundedSoFar + refundAmount;
                var isFullRefund = refundedTotalAfter >= order.TotalPrice || isAllItemsFullyRefunded;
                if (isFullRefund)
                    order.Status = OrderStatus.Refunded;

                var receipt = new RefundReceipt
                {
                    Id = Guid.NewGuid(),
                    OriginalOrderId = order.Id,
                    ShiftId = shift.Id,
                    RefundAmount = refundAmount,
                    RefundMethod = refundMethod,
                    AmountReturned = amountReturned,
                    PerformedByUserId = staffId,
                    Reason = request.Reason?.Trim() ?? string.Empty,
                    CreatedAt = DateTime.UtcNow,
                    Items = receiptItems
                };
                _db.RefundReceipts.Add(receipt);

                foreach (var it in receiptItems)
                    it.RefundReceiptId = receipt.Id;

                _db.PaymentTransactions.Add(new PaymentTransaction
                {
                    Id = Guid.NewGuid(),
                    PerformedByUserId = staffId,
                    PaymentRef = $"REFUND-{receipt.Id}",
                    Amount = -refundAmount,
                    PaymentMethod = refundMethod,
                    Purpose = PaymentPurpose.OfflineOrderRefund,
                    OrderId = order.Id,
                    ShiftId = shift.Id,
                    IsSuccess = true,
                    CreatedAt = DateTime.UtcNow
                });

                if (refundMethod == PaymentMethod.Cash)
                    shift.SystemCashTotal -= refundAmount;
                else if (refundMethod == PaymentMethod.Qr)
                    shift.SystemQrTotal -= refundAmount;
                else if (refundMethod == PaymentMethod.Wallet)
                    shift.SystemOnlineTotal -= refundAmount;

                await _db.SaveChangesAsync();
                await dbTx.CommitAsync();

                return Ok(new
                {
                    refundReceiptId = receipt.Id,
                    orderId = order.Id,
                    refundAmount = receipt.RefundAmount,
                    amountReturned = receipt.AmountReturned,
                    refundMethod = receipt.RefundMethod.ToString(),
                    performedByUserId = receipt.PerformedByUserId,
                    createdAt = receipt.CreatedAt,
                    reason = receipt.Reason,
                });
            }
            catch (Exception ex)
            {
                await dbTx.RollbackAsync();
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Hủy 1 order POS đang Pending (void) nếu khách không thanh toán.
        /// </summary>
        [HttpPost("{orderId:guid}/cancel")]
        public async Task<IActionResult> CancelExistingOrder(Guid orderId)
        {
            var gate = await _dayGate.EnsurePosOperationsAllowedAsync();
            if (!gate.allowed)
                return BadRequest(gate.reason);

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
