using Application.DTOs;
using Application.Payments;
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
    [Route("api/pos/orders")]
    [Authorize(Roles = "Staff")]
    public class PosOrderController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ICurrentCampusService _campus;
        private readonly VnpayService _vnpay;

        public PosOrderController(
            AppDbContext db,
            ICurrentCampusService campus,
            VnpayService vnpay)
        {
            _db = db;
            _campus = campus;
            _vnpay = vnpay;
        }

        /// <summary>
        /// Tạo Order OFFLINE + QR
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create(CreateOfflineOrderRequest request)
        {
            if (request.TotalPrice <= 0 || request.Items.Count == 0)
                return BadRequest("Invalid order data");

            var staffId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!
            );

            // 1️⃣ LẤY CA ĐANG MỞ
            var shift = await _db.Shifts.FirstOrDefaultAsync(x =>
                x.UserId == staffId &&
                x.CampusId == _campus.CampusId &&
                x.Status == ShiftStatus.Open
            );

            if (shift == null)
                return BadRequest("No open shift");

            // 2️⃣ TẠO ORDER
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CampusId = _campus.CampusId,
                ShiftId = shift.Id,
                OrderedByUserId = staffId,

                OrderSource = OrderSource.Offline,
                PaymentMethod = PaymentMethod.Qr,
                Status = OrderStatus.Pending,

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
                    x.CampusId == _campus.CampusId &&
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

            // 4️⃣ TẠO VNPAY TRANSACTION
            var txnRef = Guid.NewGuid().ToString("N");

            _db.VnpayTransactions.Add(new VnpayTransaction
            {
                Id = Guid.NewGuid(),
                VnpTxnRef = txnRef,
                Amount = request.TotalPrice,
                Purpose = PaymentPurpose.OfflineOrder,
                OrderId = order.Id,
                ShiftId = shift.Id,
                IsSuccess = false,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            // 5️⃣ TẠO QR URL
            var qrUrl = _vnpay.CreatePaymentUrl(
                request.TotalPrice,
                txnRef,
                $"Thanh toan don offline {order.Id}"
            );

            return Ok(new
            {
                orderId = order.Id,
                qrUrl
            });
        }
    }
}
