using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Enums
{
    public enum OrderSource
    {
        Online = 0,
        Offline = 1
    }

    public enum PaymentMethod
    {
        Wallet = 0,
        Cash = 1,
        QR = 2
    }

    public enum OrderStatus
    {
        Pending = 0,
        Paid = 1,
        Cancelled = 2
    }
}
