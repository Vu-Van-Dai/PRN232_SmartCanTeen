using API.Hubs;
using Application.Orders;
using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace API.Services
{
    public sealed class PayosPaymentProcessor
    {
        private readonly AppDbContext _db;
        private readonly InventoryService _inventoryService;
        private readonly IHubContext<OrderHub> _orderHub;
        private readonly IHubContext<ManagementHub> _managementHub;
        private readonly ILogger<PayosPaymentProcessor> _logger;

        public PayosPaymentProcessor(
            AppDbContext db,
            InventoryService inventoryService,
            IHubContext<OrderHub> orderHub,
            IHubContext<ManagementHub> managementHub,
            ILogger<PayosPaymentProcessor> logger)
        {
            _db = db;
            _inventoryService = inventoryService;
            _orderHub = orderHub;
            _managementHub = managementHub;
            _logger = logger;
        }

        public async Task<bool> TryMarkPaidAsync(int orderCode, CancellationToken ct = default)
        {
            if (orderCode <= 0) return false;

            var payRef = $"PAYOS-{orderCode}";
            var txn = await _db.PaymentTransactions.FirstOrDefaultAsync(x => x.PaymentRef == payRef, ct);
            if (txn == null) return false;
            if (txn.IsSuccess) return true;

            using var dbTx = await _db.Database.BeginTransactionAsync(ct);

            Order? order = null;
            Wallet? wallet = null;

            try
            {
                if (txn.Purpose == PaymentPurpose.OfflineOrder)
                {
                    order = await _db.Orders
                        .Include(o => o.Items)
                        .FirstOrDefaultAsync(o => o.Id == txn.OrderId, ct);

                    if (order == null || order.Status != OrderStatus.Pending)
                        throw new InvalidOperationException("Invalid order");

                    var shift = await _db.Shifts.FindAsync(new object?[] { txn.ShiftId }, ct);
                    if (shift == null || shift.Status != ShiftStatus.Open)
                        throw new InvalidOperationException("Invalid shift");

                    // Offline orders paid at POS go straight to preparing
                    order.Status = OrderStatus.Preparing;
                    order.IsUrgent = true;

                    shift.SystemQrTotal += txn.Amount;

                    await _inventoryService.DeductInventoryAsync(order, order.OrderedByUserId);
                }
                else if (txn.Purpose == PaymentPurpose.WalletTopup)
                {
                    wallet = await _db.Wallets.FirstOrDefaultAsync(x => x.Id == txn.WalletId, ct);
                    if (wallet == null)
                        throw new InvalidOperationException("Wallet not found");

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

                await _db.SaveChangesAsync(ct);
                await dbTx.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                await dbTx.RollbackAsync(ct);
                _logger.LogWarning(ex, "PayOS payment processing failed for {PayRef}", payRef);
                return false;
            }

            // Post-commit notifications
            if (txn.Purpose == PaymentPurpose.OfflineOrder && order != null)
            {
                await _managementHub.Clients.All.SendAsync("OrderPaid", new
                {
                    orderId = order.Id,
                    shiftId = order.ShiftId,
                    amount = txn.Amount,
                    method = PaymentMethod.Qr
                }, ct);

                if (order.ShiftId != null)
                {
                    await _orderHub.Clients
                        .Group(order.ShiftId.Value.ToString())
                        .SendAsync("OrderPaid", new
                        {
                            orderId = order.Id,
                            amount = txn.Amount
                        }, ct);
                }
            }

            if (txn.Purpose == PaymentPurpose.WalletTopup && wallet != null)
            {
                await _managementHub.Clients.All.SendAsync("WalletTopup", new
                {
                    walletId = wallet.Id,
                    amount = txn.Amount
                }, ct);
            }

            return true;
        }
    }
}
