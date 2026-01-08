using Application.DTOs;
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
    [Route("api/shifts")]
    [Authorize(Roles = "Staff")]
    public class ShiftsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ICurrentCampusService _campus;

        public ShiftsController(AppDbContext db, ICurrentCampusService campus)
        {
            _db = db;
            _campus = campus;
        }

        // =========================
        // 1. MỞ CA
        // =========================
        [HttpPost("open")]
        public async Task<IActionResult> OpenShift()
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var exists = await _db.Shifts.AnyAsync(x =>
                x.UserId == userId &&
                x.CampusId == _campus.CampusId &&
                x.Status != ShiftStatus.Closed);

            if (exists)
                return BadRequest("You already have an active shift");

            var shift = new Shift
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CampusId = _campus.CampusId,
                OpenedAt = DateTime.UtcNow,
                Status = ShiftStatus.Open
            };

            _db.Shifts.Add(shift);
            await _db.SaveChangesAsync();

            return Ok(shift.Id);
        }

        // =========================
        // 5. NHÂN VIÊN BẤM "KHAI BÁO CUỐI CA"
        // =========================
        [HttpPost("start-declare")]
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
        public async Task<IActionResult> Close()
        {
            var shift = await GetCurrentShiftOrNull();
            if (shift == null)
                return BadRequest("No active shift");

            shift.Status = ShiftStatus.Closed;
            shift.ClosedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

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
                x.CampusId == _campus.CampusId &&
                x.Status != ShiftStatus.Closed);
        }
        // Không cho mở ca mới nếu còn ca đang Open / Declaring / Counting / WaitingConfirm
    }
}
