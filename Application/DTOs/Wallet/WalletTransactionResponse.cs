using System;
using System.Collections.Generic;
using Core.Enums;

namespace Application.DTOs.Wallet
{
    public class WalletTransactionItem
    {
        public Guid Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public decimal Amount { get; set; }
        public TransactionType Type { get; set; }
        public TransactionStatus Status { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public Guid? OrderId { get; set; }
    }

    public class WalletTransactionsResponse
    {
        public int Total { get; set; }
        public List<WalletTransactionItem> Items { get; set; } = new();
    }
}
