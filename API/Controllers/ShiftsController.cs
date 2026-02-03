using API.Hubs;
using Application.DTOs;
using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using API.Services;

namespace API.Controllers
{
    [ApiController]
    [Route("api/shifts")]
    [Authorize(Roles = "Staff,StaffPOS,Manager")]
    public class ShiftsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHubContext<ManagementHub> _managementHub;
        private readonly BusinessDayGate _dayGate;
        public ShiftsController(AppDbContext db, IHubContext<ManagementHub> managementHub, BusinessDayGate dayGate)
        {
            _db = db;
            _managementHub = managementHub;
            _dayGate = dayGate;
        }

        // =========================
        // 1. MỞ CA
        // =========================
        [HttpPost("open")]
        [Authorize(Roles = "StaffPOS,Manager")]
        public async Task<IActionResult> OpenShift()
        {
            var gate = await _dayGate.EnsurePosOperationsAllowedAsync();
            if (!gate.allowed)
                return BadRequest(gate.reason);

            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var exists = await _db.Shifts.AnyAsync(x =>
                x.UserId == userId &&
                x.Status != ShiftStatus.Closed);

            if (exists)
                return BadRequest("You already have an active shift");

            // Enforce: only one active shift (POS) at a time.
            var anyActive = await _db.Shifts.AnyAsync(x => x.Status != ShiftStatus.Closed);
            if (anyActive)
                return BadRequest("Another shift is already active");

            var shift = new Shift
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                OpenedAt = DateTime.UtcNow,
                Status = ShiftStatus.Open
            };

            _db.Shifts.Add(shift);
            await _db.SaveChangesAsync();
            await _managementHub.Clients
                .All
                .SendAsync("ShiftOpened", new
                {
                    shiftId = shift.Id,
                    staffId = shift.UserId,
                    openedAt = shift.OpenedAt
                });
            return Ok(shift.Id);
        }

        // =========================
        // CURRENT SHIFT (for POS declare UI)
        // =========================
        [HttpGet("current")]
        public async Task<IActionResult> GetCurrent()
        {
            var shift = await GetCurrentShiftOrNull();
            if (shift == null)
                return BadRequest("No active shift");

            return Ok(new
            {
                id = shift.Id,
                status = shift.Status.ToString(),
                openedAt = shift.OpenedAt,
                systemCashTotal = shift.SystemCashTotal,
                systemQrTotal = shift.SystemQrTotal,
                systemOnlineTotal = shift.SystemOnlineTotal,
                staffCashInput = shift.StaffCashInput,
                staffQrInput = shift.StaffQrInput
            });
        }

        // =========================
        // 5. NHÂN VIÊN BẤM "KHAI BÁO CUỐI CA"
        // =========================
        [HttpPost("start-declare")]
        [Authorize(Roles = "StaffPOS,Manager")]
        public async Task<IActionResult> StartDeclare()
        {
            var shift = await GetCurrentShiftOrNull();
            if (shift == null)
                return BadRequest("No active shift");

            shift.Status = ShiftStatus.StaffDeclaring;
            await _db.SaveChangesAsync();

            return Ok();
        }

        // =========================
        // 6. NHÂN VIÊN BẤM "ĐẾM"
        // =========================
        [HttpPost("start-counting")]
        [Authorize(Roles = "StaffPOS,Manager")]
        public async Task<IActionResult> StartCounting()
        {
            var shift = await GetCurrentShiftOrNull();
            if (shift == null)
                return BadRequest("No active shift");

            shift.Status = ShiftStatus.Counting;
            await _db.SaveChangesAsync();

            return Ok();
        }

        // =========================
        // 7–8. NHẬP SỐ + XÁC NHẬN
        // =========================
        [HttpPost("confirm")]
        [Authorize(Roles = "StaffPOS,Manager")]
        public async Task<IActionResult> Confirm(StaffConfirmRequest request)
        {
            var shift = await GetCurrentShiftOrNull();
            if (shift == null)
                return BadRequest("No active shift");

            if (request.Cash != shift.SystemCashTotal ||
                request.Qr != shift.SystemQrTotal)
            {
                return BadRequest("Invalid declared amount");
            }

            shift.StaffCashInput = request.Cash;
            shift.StaffQrInput = request.Qr;
            shift.Status = ShiftStatus.WaitingConfirm;

            await _db.SaveChangesAsync();
            return Ok();
        }

        // =========================
        // 9–10. KHAI BÁO → ĐÓNG CA
        // =========================
        [HttpPost("close")]
        [Authorize(Roles = "StaffPOS,Manager")]
        public async Task<IActionResult> Close()
        {
            var shift = await GetCurrentShiftOrNull();
            if (shift == null)
                return BadRequest("No active shift");

            shift.Status = ShiftStatus.Closed;
            shift.ClosedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            await _managementHub.Clients
                .All
                .SendAsync("ShiftClosed", new
                {
                    shiftId = shift.Id,
                    staffId = shift.UserId,
                    cash = shift.SystemCashTotal,
                    qr = shift.SystemQrTotal,
                    online = shift.SystemOnlineTotal
                });
            // TODO: force logout
            return Ok();
        }

        // =========================
        // HELPER
        // =========================
        private async Task<Shift?> GetCurrentShiftOrNull()
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            return await _db.Shifts.FirstOrDefaultAsync(x =>
                x.UserId == userId &&
                x.Status != ShiftStatus.Closed);
        }
        // Không cho mở ca mới nếu còn ca đang Open / Declaring / Counting / WaitingConfirm
    }
}
