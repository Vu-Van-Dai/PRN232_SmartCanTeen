using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Enums
{
    public enum TransactionType
    {
        Credit = 0,
        Debit = 1,
        TransferIn = 2,
        TransferOut = 3
    }

    public enum TransactionStatus
    {
        Pending = 0,
        Success = 1,
        Failed = 2
    }
}
