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
        Open = 0,            // đang bán
        StaffDeclaring = 1,  // NV bấm "Khai báo cuối ca"
        Counting = 2,        // NV đang đếm tiền
        WaitingConfirm = 3,  // NV đã nhập số, chờ khai báo
        Closed = 4           // ca đã đóng
    }
}
