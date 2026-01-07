using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Enums
{
    public enum WalletStatus
    {
        Active = 0,
        Closed = 1,
        Locked = 2
    }

    public enum WalletAccessType
    {
        Owner = 0,
        Shared = 1
    }

    public enum ShiftStatus
    {
        Open = 0,
        Closed = 1,
        Approved = 2
    }
}
