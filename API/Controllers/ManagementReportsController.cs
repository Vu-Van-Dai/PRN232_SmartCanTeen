using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace API.Controllers
{
    [ApiController]
    [Route("api/management/reports")]
    [Authorize(Roles = "Manager")]
    public class ManagementReportsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public ManagementReportsController(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Báo cáo tổng hợp theo ngày (theo ca đã đóng)
        /// </summary>
        [HttpGet("daily")]
        public async Task<IActionResult> GetDailyReport(
            DateTime date)
        {
            var from = date.Date;
            var to = from.AddDays(1);

            var shifts = await _db.Shifts
                .Where(x =>
                    x.Status == ShiftStatus.Closed &&
                    x.ClosedAt >= from &&
                    x.ClosedAt < to)
                .Select(x => new
                {
                    x.Id,
                    x.UserId,
                    x.OpenedAt,
                    x.ClosedAt,

                    x.SystemCashTotal,
                    x.SystemQrTotal,
                    x.SystemOnlineTotal,

                    x.StaffCashInput,
                    x.StaffQrInput
                })
                .ToListAsync();

            var summary = new
            {
                TotalCash = shifts.Sum(x => x.SystemCashTotal),
                TotalQr = shifts.Sum(x => x.SystemQrTotal),
                TotalOnline = shifts.Sum(x => x.SystemOnlineTotal),

                TotalRevenue =
                    shifts.Sum(x => x.SystemCashTotal) +
                    shifts.Sum(x => x.SystemQrTotal) +
                    shifts.Sum(x => x.SystemOnlineTotal)
            };

            return Ok(new
            {
                date = from,
                shifts,
                summary
            });
        }
        [HttpPost("close-day")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> CloseDay(DateTime date)
        {
            var from = date.Date;
            var to = from.AddDays(1);

            var shifts = await _db.Shifts
                .Where(x =>
                    x.Status == ShiftStatus.Closed &&
                    x.ClosedAt >= from &&
                    x.ClosedAt < to)
                .ToListAsync();

            if (!shifts.Any())
                return BadRequest("No closed shifts");

            var exists = await _db.DailyRevenues.AnyAsync(x => x.Date == from);

            if (exists)
                return BadRequest("Day already closed");

            var managerId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!
            );

            var daily = new DailyRevenue
            {
                Id = Guid.NewGuid(),
                Date = from,

                TotalCash = shifts.Sum(x => x.SystemCashTotal),
                TotalQr = shifts.Sum(x => x.SystemQrTotal),
                TotalOnline = shifts.Sum(x => x.SystemOnlineTotal),

                ClosedByUserId = managerId,
                ClosedAt = DateTime.UtcNow
            };
            
            _db.DailyRevenues.Add(daily);
            await _db.SaveChangesAsync();

            return Ok(daily);
        }
        [HttpGet("shift/{shiftId}")]
        public async Task<IActionResult> GetShiftDetail(Guid shiftId)
        {
            var shift = await _db.Shifts
                .Where(x =>
                    x.Id == shiftId &&
                    x.Status == ShiftStatus.Closed)
                .Select(x => new
                {
                    x.Id,
                    x.UserId,
                    x.OpenedAt,
                    x.ClosedAt,
                    x.SystemCashTotal,
                    x.SystemQrTotal,
                    x.SystemOnlineTotal,
                    x.StaffCashInput,
                    x.StaffQrInput
                })
                .FirstOrDefaultAsync();

            if (shift == null)
                return NotFound();

            return Ok(shift);
        }
        [HttpGet("dashboard")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> GetDashboardSnapshot()
        {
            var today = DateTime.UtcNow.Date;

            var shifts = await _db.Shifts
                .Where(x =>
                    x.OpenedAt >= today)
                .Select(x => new
                {
                    x.Id,
                    x.UserId,
                    x.Status,
                    x.SystemCashTotal,
                    x.SystemQrTotal,
                    x.SystemOnlineTotal
                })
                .ToListAsync();

            return Ok(new
            {
                shifts,
                totalCash = shifts.Sum(x => x.SystemCashTotal),
                totalQr = shifts.Sum(x => x.SystemQrTotal),
                totalOnline = shifts.Sum(x => x.SystemOnlineTotal)
            });
        }
    }
}
