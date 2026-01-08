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
        Open = 1,            // đang bán
        StaffDeclaring = 2,  // NV bấm "Khai báo cuối ca"
        Counting = 3,        // NV đang đếm tiền
        WaitingConfirm = 4,  // NV đã nhập số, chờ khai báo
        Closed = 5           // ca đã đóng
    }
}
