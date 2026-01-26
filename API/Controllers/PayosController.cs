using API.Hubs;
using Application.Orders;
using Application.Payments;
using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

namespace API.Controllers
{
    [ApiController]
    [Route("api/payos")]
    public class PayosController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly PayosService _payos;
        private readonly InventoryService _inventoryService;
        private readonly IHubContext<OrderHub> _hub;
        private readonly IHubContext<ManagementHub> _managementHub;

        public PayosController(
            AppDbContext db,
            PayosService payos,
            InventoryService inventoryService,
            IHubContext<OrderHub> hub,
            IHubContext<ManagementHub> managementHub)
        {
            _db = db;
            _payos = payos;
            _inventoryService = inventoryService;
            _hub = hub;
            _managementHub = managementHub;
        }

        // Local-dev fallback: PayOS dashboard/webhook usually can't reach localhost.
        // These endpoints allow the FE return/cancel pages to confirm the payment outcome.
        // NOTE: This is not a replacement for webhooks in production.

        [HttpPost("confirm")]
        public async Task<IActionResult> Confirm([FromQuery] int orderCode, [FromQuery] string? status = null)
        {
            if (orderCode <= 0) return BadRequest("Invalid orderCode");

            // Accept only PAID confirmations
            if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "PAID", StringComparison.OrdinalIgnoreCase))
                return Ok();

            var payRef = $"PAYOS-{orderCode}";
            var txn = await _db.PaymentTransactions.FirstOrDefaultAsync(x => x.PaymentRef == payRef);
            if (txn == null || txn.IsSuccess)
                return Ok();

            using var dbTx = await _db.Database.BeginTransactionAsync();

            Order? order = null;
            Wallet? wallet = null;

            try
            {
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

                    // Offline orders paid at POS go straight to preparing
                    order.Status = OrderStatus.Preparing;
                    order.IsUrgent = true;

                    shift.SystemQrTotal += txn.Amount;

                    await _inventoryService.DeductInventoryAsync(order, order.OrderedByUserId);
                }

                if (txn.Purpose == PaymentPurpose.WalletTopup)
                {
                    wallet = await _db.Wallets.FirstOrDefaultAsync(x => x.Id == txn.WalletId);
                    if (wallet == null)
                        throw new Exception("Wallet not found");

                    wallet.Balance += txn.Amount;

                    _db.Transactions.Add(new Transaction
                    {
                        Id = Guid.NewGuid(),
                        WalletId = wallet.Id,
                        Amount = txn.Amount,
                        Type = TransactionType.Credit,
                        Status = TransactionStatus.Success,
                        PaymentMethod = PaymentMethod.Qr,
                        PerformedByUserId = wallet.UserId,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                txn.IsSuccess = true;
                await _db.SaveChangesAsync();
                await dbTx.CommitAsync();
            }
            catch
            {
                await dbTx.RollbackAsync();
                return Ok();
            }

            if (txn.Purpose == PaymentPurpose.OfflineOrder && order != null)
            {
                await _managementHub.Clients.All.SendAsync("OrderPaid", new
                {
                    orderId = order.Id,
                    shiftId = order.ShiftId,
                    amount = txn.Amount,
                    method = PaymentMethod.Qr
                });

                if (order.ShiftId != null)
                {
                    await _hub.Clients
                        .Group(order.ShiftId.Value.ToString())
                        .SendAsync("OrderPaid", new
                        {
                            orderId = order.Id,
                            amount = txn.Amount
                        });
                }
            }

            if (txn.Purpose == PaymentPurpose.WalletTopup && wallet != null)
            {
                await _managementHub.Clients.All.SendAsync("WalletTopup", new
                {
                    walletId = wallet.Id,
                    amount = txn.Amount
                });
            }

            return Ok();
        }

        [HttpPost("cancel")]
        public async Task<IActionResult> Cancel([FromQuery] int orderCode)
        {
            if (orderCode <= 0) return BadRequest("Invalid orderCode");

            var payRef = $"PAYOS-{orderCode}";
            var txn = await _db.PaymentTransactions.FirstOrDefaultAsync(x => x.PaymentRef == payRef);
            if (txn == null || txn.IsSuccess)
                return Ok();

            // Cancel behavior for POS offline orders:
            // - mark the pending order as cancelled to avoid accumulating pending QR orders
            // - cashier can still create a CASH order from the POS UI
            if (txn.Purpose == PaymentPurpose.OfflineOrder && txn.OrderId != null)
            {
                var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == txn.OrderId);
                if (order != null && order.Status == OrderStatus.Pending)
                {
                    order.Status = OrderStatus.Cancelled;
                }
            }

            // We keep txn.IsSuccess=false. Optionally delete txn later; for now, we just persist the cancel effect.
            await _db.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook([FromBody] PayosWebhookPayload payload)
        {
            // Always be tolerant: if DB was recreated after a payment link was generated,
            // we no-op instead of throwing (prevents "payment failed" loops).
            if (payload == null)
                return Ok();

            if (!_payos.VerifyWebhookSignature(payload.Data, payload.Signature))
                return BadRequest();

            // Accept only successful notifications
            if (!payload.Success || payload.Code != "00")
                return Ok();

            var orderCode = TryGetInt(payload.Data, "orderCode");
            if (orderCode == null)
                return Ok();

            // Some payloads also include a nested success code inside data
            var dataCode = TryGetString(payload.Data, "code");
            if (!string.IsNullOrWhiteSpace(dataCode) && dataCode != "00")
                return Ok();

            var payRef = $"PAYOS-{orderCode.Value}";

            var txn = await _db.PaymentTransactions.FirstOrDefaultAsync(x => x.PaymentRef == payRef);
            if (txn == null || txn.IsSuccess)
                return Ok();

            using var dbTx = await _db.Database.BeginTransactionAsync();

            Order? order = null;
            Wallet? wallet = null;

            try
            {
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
                        order.Status = OrderStatus.Preparing;
                        order.IsUrgent = true;
                    }
                    else
                    {
                        if (order.PickupTime == null)
                        {
                            order.Status = OrderStatus.Preparing;
                            order.IsUrgent = true;
                        }
                        else
                        {
                            order.Status = OrderStatus.SystemHolding;
                            order.IsUrgent = false;
                        }
                    }

                    shift.SystemQrTotal += txn.Amount;

                    await _inventoryService.DeductInventoryAsync(order, order.OrderedByUserId);
                }

                if (txn.Purpose == PaymentPurpose.WalletTopup)
                {
                    wallet = await _db.Wallets.FirstOrDefaultAsync(x => x.Id == txn.WalletId);
                    if (wallet == null)
                        throw new Exception("Wallet not found");

                    wallet.Balance += txn.Amount;

                    _db.Transactions.Add(new Transaction
                    {
                        Id = Guid.NewGuid(),
                        WalletId = wallet.Id,
                        Amount = txn.Amount,
                        Type = TransactionType.Credit,
                        Status = TransactionStatus.Success,
                        PaymentMethod = PaymentMethod.Qr,
                        PerformedByUserId = wallet.UserId,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                txn.IsSuccess = true;

                await _db.SaveChangesAsync();
                await dbTx.CommitAsync();
            }
            catch
            {
                await dbTx.RollbackAsync();
                return Ok();
            }

            if (txn.Purpose == PaymentPurpose.OfflineOrder && order != null)
            {
                await _managementHub.Clients.All.SendAsync("OrderPaid", new
                {
                    orderId = order.Id,
                    shiftId = order.ShiftId,
                    amount = txn.Amount,
                    method = PaymentMethod.Qr
                });

                if (order.ShiftId != null)
                {
                    await _hub.Clients
                        .Group(order.ShiftId.Value.ToString())
                        .SendAsync("OrderPaid", new
                        {
                            orderId = order.Id,
                            amount = txn.Amount
                        });
                }
            }

            if (txn.Purpose == PaymentPurpose.WalletTopup && wallet != null)
            {
                await _managementHub.Clients.All.SendAsync("WalletTopup", new
                {
                    walletId = wallet.Id,
                    amount = txn.Amount
                });
            }

            return Ok();
        }

        private static int? TryGetInt(System.Collections.Generic.Dictionary<string, object?> data, string key)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
                return null;

            if (value is int i) return i;
            if (value is long l) return checked((int)l);

            if (value is JsonElement el)
            {
                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var j))
                    return j;

                if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var s))
                    return s;
            }

            if (value is string str && int.TryParse(str, out var parsed))
                return parsed;

            return null;
        }

        private static string? TryGetString(System.Collections.Generic.Dictionary<string, object?> data, string key)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
                return null;

            if (value is string s) return s;
            if (value is JsonElement el && el.ValueKind == JsonValueKind.String)
                return el.GetString();

            return value.ToString();
        }
    }
}
