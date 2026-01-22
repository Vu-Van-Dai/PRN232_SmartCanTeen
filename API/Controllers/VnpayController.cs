using API.Hubs;
using Application.Orders.Services;
using Core.Entities;
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
        private readonly IHubContext<ManagementHub> _managementHub;

        public VnpayController(
            AppDbContext db,
            IHubContext<OrderHub> hub,
            InventoryService inventoryService,
            IHubContext<ManagementHub> managementHub)
        {
            _db = db;
            _hub = hub;
            _inventoryService = inventoryService;
            _managementHub = managementHub;
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
                return Ok();

            var txn = await _db.VnpayTransactions
                .FirstOrDefaultAsync(x => x.VnpTxnRef == txnRef);

            if (txn == null || txn.IsSuccess)
                return Ok();

            using var dbTx = await _db.Database.BeginTransactionAsync();

            Order? order = null;
            Wallet? wallet = null;

            try
            {
                // ========= OFFLINE ORDER =========
                if (txn.Purpose == PaymentPurpose.OfflineOrder)
                {
                    order = await _db.Orders
                        .Include(o => o.Items)
                        .FirstOrDefaultAsync(o => o.Id == txn.OrderId);

                    if (order == null || order.Status != OrderStatus.Pending)
                        throw new Exception("Invalid order");

                    var shift = await _db.Shifts.FindAsync(txn.ShiftId);
                    if (shift == null || shift.Status != ShiftStatus.Open)
                        throw new Exception("Invalid shift");

                    if (order.OrderSource == OrderSource.Offline)
                    {
                        // POS: LUÔN NẤU NGAY
                        order.Status = OrderStatus.Preparing;
                        order.IsUrgent = true;
                    }
                    else
                    {
                        // ONLINE
                        if (order.PickupTime == null)
                        {
                            // Online ăn liền
                            order.Status = OrderStatus.Preparing;
                            order.IsUrgent = true;
                        }
                        else
                        {
                            // Online đặt trước
                            order.Status = OrderStatus.SystemHolding;
                            order.IsUrgent = false;
                        }
                    }
                    shift.SystemQrTotal += txn.Amount;

                    await _inventoryService.DeductInventoryAsync(
                        order,
                        order.OrderedByUserId
                    );
                }

                // ========= WALLET TOPUP =========
                if (txn.Purpose == PaymentPurpose.WalletTopup)
                {
                    wallet = await _db.Wallets
                        .FirstOrDefaultAsync(x => x.Id == txn.WalletId);

                    if (wallet == null)
                        throw new Exception("Wallet not found");

                    wallet.Balance += txn.Amount;

                    _db.Transactions.Add(new Transaction
                    {
                        Id = Guid.NewGuid(),
                        CampusId = wallet.CampusId,
                        WalletId = wallet.Id,
                        Amount = txn.Amount,
                        Type = TransactionType.Credit,
                        Status = TransactionStatus.Success,
                        PaymentMethod = PaymentMethod.Qr,
                        PerformedByUserId = wallet.UserId,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                // ========= FINAL =========
                txn.IsSuccess = true;

                await _db.SaveChangesAsync();
                await dbTx.CommitAsync();
            }
            catch
            {
                await dbTx.RollbackAsync();
                return Ok();
            }

            // ========= REALTIME (SAU COMMIT) =========

            if (txn.Purpose == PaymentPurpose.OfflineOrder && order != null)
            {
                await _managementHub.Clients
                    .Group($"campus-{order.CampusId}")
                    .SendAsync("OrderPaid", new
                    {
                        orderId = order.Id,
                        shiftId = order.ShiftId,
                        amount = txn.Amount,
                        method = PaymentMethod.Qr
                    });

                await _hub.Clients
                    .Group(order.ShiftId.ToString())
                    .SendAsync("OrderPaid", new
                    {
                        orderId = order.Id,
                        amount = txn.Amount
                    });
            }

            if (txn.Purpose == PaymentPurpose.WalletTopup && wallet != null)
            {
                await _managementHub.Clients
                    .Group($"campus-{wallet.CampusId}")
                    .SendAsync("WalletTopup", new
                    {
                        walletId = wallet.Id,
                        amount = txn.Amount
                    });
            }

            return Ok();
        }

    }
}
