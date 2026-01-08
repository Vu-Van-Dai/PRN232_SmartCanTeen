using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Enums
{
    public enum OrderSource { 
        Online = 1, 
        Offline = 2 
    }
    public enum PaymentMethod { 
        Wallet = 1, 
        Cash = 2, 
        Qr = 3 
    }
    public enum OrderStatus { 
        Pending = 1, 
        Paid = 2, 
        Cancelled = 3 
    }
}
