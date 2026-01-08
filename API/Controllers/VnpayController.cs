using API.Hubs;
using Application.Orders.Services;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace API.Controllers
{
    [ApiController]
    [Route("api/vnpay")]
    public class VnpayController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHubContext<OrderHub> _hub;
        private readonly InventoryService _inventoryService;

        public VnpayController(
            AppDbContext db,
            IHubContext<OrderHub> hub,
            InventoryService inventoryService)
        {
            _db = db;
            _hub = hub;
            _inventoryService = inventoryService;
        }

        /// <summary>
        /// VNPAY IPN CALLBACK
        /// </summary>
        [HttpGet("ipn")]
        public async Task<IActionResult> Ipn()
        {
            var txnRef = Request.Query["vnp_TxnRef"].ToString();
            var responseCode = Request.Query["vnp_ResponseCode"].ToString();

            if (responseCode != "00")
                return Ok(); // failed

            var txn = await _db.VnpayTransactions
                .FirstOrDefaultAsync(x => x.VnpTxnRef == txnRef);

            if (txn == null || txn.IsSuccess)
                return Ok(); // idempotent

            using var dbTx = await _db.Database.BeginTransactionAsync();

            txn.IsSuccess = true;

            if (txn.Purpose == PaymentPurpose.OfflineOrder)
            {
                var order = await _db.Orders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.Id == txn.OrderId);

                if (order == null || order.Status != OrderStatus.Pending)
                    return Ok();

                order.Status = OrderStatus.Paid;

                // cộng QR cho ca
                var shift = await _db.Shifts.FindAsync(txn.ShiftId);
                if (shift != null)
                {
                    shift.SystemQrTotal += txn.Amount;
                }

                // TRỪ KHO + INVENTORY LOG
                await _inventoryService.DeductInventoryAsync(
                    order,
                    order.OrderedByUserId
                );
            }

            await _db.SaveChangesAsync();
            await dbTx.CommitAsync();

            // SIGNALR → POS
            if (txn.Purpose == PaymentPurpose.OfflineOrder && txn.ShiftId != null)
            {
                await _hub.Clients
                    .Group(txn.ShiftId.ToString()!)
                    .SendAsync("OrderPaid", new
                    {
                        orderId = txn.OrderId,
                        amount = txn.Amount
                    });
            }

            return Ok();
        }
    }
}
