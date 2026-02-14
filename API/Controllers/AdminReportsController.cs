using API.Services;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MigraDocCore.DocumentObjectModel;
using MigraDocCore.DocumentObjectModel.Tables;
using MigraDocCore.Rendering;
using System.Globalization;
using System.Security.Claims;

namespace API.Controllers
{
    [ApiController]
    [Route("api/admin/reports")]
    [Authorize(Roles = "AdminSystem")]
    public class AdminReportsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly BusinessDayClock _clock;
        private readonly IConfiguration _config;

        public AdminReportsController(AppDbContext db, BusinessDayClock clock, IConfiguration config)
        {
            _db = db;
            _clock = clock;
            _config = config;
        }

        public record ExportRevenueReportPdfRequest(
            string Mode,
            DateTime? Date,
            string? PreparedBy,
            string? CanteenRep,
            string? SchoolRep,
            DateTime? ConfirmedDate
        );

        private static bool IsRevenueStatus(OrderStatus status)
            => status == OrderStatus.Paid
               || status == OrderStatus.SystemHolding
               || status == OrderStatus.Preparing
               || status == OrderStatus.Ready
               || status == OrderStatus.Completed;

        private static string PaymentBucket(OrderSource source, PaymentMethod paymentMethod)
        {
            if (source == OrderSource.Online) return "Online";
            return paymentMethod == PaymentMethod.Cash ? "Cash" : "QR";
        }

        [HttpGet("series")]
        public async Task<IActionResult> GetRevenueSeries(
            [FromQuery] string mode = "week",
            [FromQuery] DateTime? date = null)
        {
            var anchorLocalDate = DateOnly.FromDateTime(date ?? _clock.LocalNow);

            DateOnly start;
            int count;

            if (string.Equals(mode, "month", StringComparison.OrdinalIgnoreCase))
            {
                start = new DateOnly(anchorLocalDate.Year, anchorLocalDate.Month, 1);
                count = DateTime.DaysInMonth(anchorLocalDate.Year, anchorLocalDate.Month);
                mode = "month";
            }
            else
            {
                // Week view: Monday..Sunday (can cross months; do not split by month)
                var dow = (int)anchorLocalDate.DayOfWeek; // Sunday=0
                var mondayIndex = (dow + 6) % 7; // Monday=0
                start = anchorLocalDate.AddDays(-mondayIndex);
                count = 7;
                mode = "week";
            }

            var days = Enumerable.Range(0, count).Select(i => start.AddDays(i)).ToList();
            var end = days[^1];

            var (fromUtc, _) = _clock.GetOperationalWindowUtc(start);
            var (_, toUtc) = _clock.GetOperationalWindowUtc(end);

            var orders = await _db.Orders
                .AsNoTracking()
                .Where(o => o.CreatedAt >= fromUtc && o.CreatedAt < toUtc)
                .Select(o => new
                {
                    o.CreatedAt,
                    o.TotalPrice,
                    o.DiscountAmount,
                    o.Status,
                    o.OrderSource,
                    o.PaymentMethod,
                })
                .ToListAsync();

            var daily = orders
                .Where(o => IsRevenueStatus(o.Status))
                .Select(o =>
                {
                    var localCreated = _clock.ConvertUtcToLocal(o.CreatedAt);
                    var opDate = _clock.GetOperationalDate(localCreated);
                    var bucket = PaymentBucket(o.OrderSource, o.PaymentMethod);
                    return new { opDate, bucket, o.TotalPrice, o.DiscountAmount };
                })
                .GroupBy(x => x.opDate)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        totalRevenue = g.Sum(x => x.TotalPrice),
                        totalDiscountAmount = g.Sum(x => x.DiscountAmount),
                        totalCash = g.Where(x => x.bucket == "Cash").Sum(x => x.TotalPrice),
                        totalQr = g.Where(x => x.bucket == "QR").Sum(x => x.TotalPrice),
                        totalOnline = g.Where(x => x.bucket == "Online").Sum(x => x.TotalPrice),
                        totalOrders = g.Count(),
                    }
                );

            var dayKeysUtc = days
                .Select(d => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc))
                .ToList();

            var closedMap = await _db.DailyRevenues
                .AsNoTracking()
                .Where(x => dayKeysUtc.Contains(x.Date))
                .Select(x => new { x.Date, x.ClosedAt })
                .ToListAsync();

            var closedLookup = closedMap
                .ToDictionary(x => DateOnly.FromDateTime(x.Date), x => (DateTime?)x.ClosedAt);

            var points = days.Select(d =>
            {
                daily.TryGetValue(d, out var t);
                closedLookup.TryGetValue(d, out var closedAt);

                return new
                {
                    date = d.ToDateTime(TimeOnly.MinValue),
                    label = d.ToString("dd/MM"),
                    totalRevenue = t?.totalRevenue ?? 0m,
                    totalDiscountAmount = t?.totalDiscountAmount ?? 0m,
                    totalCash = t?.totalCash ?? 0m,
                    totalQr = t?.totalQr ?? 0m,
                    totalOnline = t?.totalOnline ?? 0m,
                    totalOrders = t?.totalOrders ?? 0,
                    isClosed = closedAt != null,
                    closedAt,
                };
            }).ToList();

            return Ok(new
            {
                mode,
                anchorDate = anchorLocalDate.ToDateTime(TimeOnly.MinValue),
                points,
            });
        }

        [HttpGet("orders")]
        public async Task<IActionResult> GetOrdersByDay([FromQuery] DateTime date)
        {
            var dateLocal = DateOnly.FromDateTime(date);
            var (fromUtc, toUtc) = _clock.GetOperationalWindowUtc(dateLocal);

            var orders = await _db.Orders
                .AsNoTracking()
                .Where(o => o.CreatedAt >= fromUtc && o.CreatedAt < toUtc)
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => new
                {
                    orderId = o.Id,
                    createdAt = o.CreatedAt,
                    pickupTime = o.PickupTime,
                    source = o.OrderSource.ToString(),
                    paymentMethod = PaymentBucket(o.OrderSource, o.PaymentMethod),
                    totalPrice = o.TotalPrice,
                    discountAmount = o.DiscountAmount,
                    status = o.Status.ToString(),
                    orderedBy = (o.OrderedByUser.FullName ?? o.OrderedByUser.Email),
                    items = o.Items.Select(i => new { name = i.Item.Name, quantity = i.Quantity }).ToList(),
                })
                .ToListAsync();

            var revenueOrders = orders
                .Where(o => o.status is "Paid" or "SystemHolding" or "Preparing" or "Ready" or "Completed")
                .ToList();

            var totals = new
            {
                totalRevenue = revenueOrders.Sum(o => o.totalPrice),
                totalDiscountAmount = revenueOrders.Sum(o => o.discountAmount),
                totalCash = revenueOrders.Where(o => o.paymentMethod == "Cash").Sum(o => o.totalPrice),
                totalQr = revenueOrders.Where(o => o.paymentMethod == "QR").Sum(o => o.totalPrice),
                totalOnline = revenueOrders.Where(o => o.paymentMethod == "Online").Sum(o => o.totalPrice),
                totalOrders = revenueOrders.Count,
            };

            var dayKeyUtc = DateTime.SpecifyKind(dateLocal.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var closed = await _db.DailyRevenues
                .AsNoTracking()
                .AnyAsync(x => x.Date == dayKeyUtc);

            return Ok(new
            {
                date = dateLocal.ToDateTime(TimeOnly.MinValue),
                fromUtc,
                toUtc,
                isClosed = closed,
                totals,
                orders,
            });
        }

        [HttpPost("export-pdf")]
        public async Task<IActionResult> ExportRevenueReportPdf([FromBody] ExportRevenueReportPdfRequest req)
        {
            var mode = string.Equals(req.Mode, "month", StringComparison.OrdinalIgnoreCase) ? "month" : "week";
            var anchorLocalDate = DateOnly.FromDateTime(req.Date ?? _clock.LocalNow);

            DateOnly start;
            int count;

            if (mode == "month")
            {
                start = new DateOnly(anchorLocalDate.Year, anchorLocalDate.Month, 1);
                count = DateTime.DaysInMonth(anchorLocalDate.Year, anchorLocalDate.Month);
            }
            else
            {
                var dow = (int)anchorLocalDate.DayOfWeek; // Sunday=0
                var mondayIndex = (dow + 6) % 7; // Monday=0
                start = anchorLocalDate.AddDays(-mondayIndex);
                count = 7;
            }

            var days = Enumerable.Range(0, count).Select(i => start.AddDays(i)).ToList();
            var end = days[^1];

            var (fromUtc, _) = _clock.GetOperationalWindowUtc(start);
            var (_, toUtc) = _clock.GetOperationalWindowUtc(end);

            var orders = await _db.Orders
                .AsNoTracking()
                .Where(o => o.CreatedAt >= fromUtc && o.CreatedAt < toUtc)
                .Select(o => new
                {
                    o.CreatedAt,
                    o.TotalPrice,
                    o.Status,
                    o.OrderSource,
                    o.PaymentMethod,
                })
                .ToListAsync();

            var daily = orders
                .Where(o => IsRevenueStatus(o.Status))
                .Select(o =>
                {
                    var localCreated = _clock.ConvertUtcToLocal(o.CreatedAt);
                    var opDate = _clock.GetOperationalDate(localCreated);
                    var bucket = PaymentBucket(o.OrderSource, o.PaymentMethod);
                    return new { opDate, bucket, o.TotalPrice };
                })
                .GroupBy(x => x.opDate)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        totalRevenue = g.Sum(x => x.TotalPrice),
                        totalCash = g.Where(x => x.bucket == "Cash").Sum(x => x.TotalPrice),
                        totalQr = g.Where(x => x.bucket == "QR").Sum(x => x.TotalPrice),
                        totalOnline = g.Where(x => x.bucket == "Online").Sum(x => x.TotalPrice),
                        totalOrders = g.Count(),
                    }
                );

            var dayKeysUtc = days
                .Select(d => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc))
                .ToList();

            var closedMap = await _db.DailyRevenues
                .AsNoTracking()
                .Where(x => dayKeysUtc.Contains(x.Date))
                .Select(x => x.Date)
                .ToListAsync();

            var closedSet = closedMap
                .Select(d => DateOnly.FromDateTime(d))
                .ToHashSet();

            var points = days.Select(d =>
            {
                daily.TryGetValue(d, out var t);
                var isClosed = closedSet.Contains(d);
                return new
                {
                    Date = d,
                    TotalRevenue = t?.totalRevenue ?? 0m,
                    TotalCash = t?.totalCash ?? 0m,
                    TotalQr = t?.totalQr ?? 0m,
                    TotalOnline = t?.totalOnline ?? 0m,
                    TotalOrders = t?.totalOrders ?? 0,
                    IsClosed = isClosed,
                };
            }).ToList();

            var totalRevenue = points.Sum(x => x.TotalRevenue);
            var totalOrders = points.Sum(x => x.TotalOrders);
            var totalCash = points.Sum(x => x.TotalCash);
            var totalQr = points.Sum(x => x.TotalQr);
            var totalOnline = points.Sum(x => x.TotalOnline);

            var totalDays = days.Count;
            var closedDays = points.Count(x => x.IsClosed);
            var unclosedDays = totalDays - closedDays;
            var unclosedList = points.Where(x => !x.IsClosed).Select(x => x.Date).ToList();

            // Cash-only declaration rule
            var declaredCashTotal = await _db.Shifts
                .AsNoTracking()
                .Where(s => s.OpenedAt < toUtc && (s.ClosedAt == null || s.ClosedAt >= fromUtc))
                .SumAsync(s => s.StaffCashInput) ?? 0m;

            var variancePeriod = declaredCashTotal - totalCash;

            var culture = CultureInfo.GetCultureInfo("vi-VN");
            string FormatMoney(decimal v) => v.ToString("#,0", culture) + " đ";
            string FormatDate(DateOnly d) => d.ToString("dd/MM/yyyy", culture);
            string FormatDateTime(DateTime dtLocal) => dtLocal.ToString("dd/MM/yyyy HH:mm", culture);

            var schoolName = _config["Report:SchoolName"] ?? "Springfield High School";
            var exportedBy = User.FindFirstValue(ClaimTypes.Name)
                ?? User.FindFirstValue(ClaimTypes.Email)
                ?? "Admin System";
            var exportTimeLocal = _clock.LocalNow;
            var confirmedLocalDate = DateOnly.FromDateTime(req.ConfirmedDate ?? exportTimeLocal);

            var preparedBy = (req.PreparedBy ?? "").Trim();
            var canteenRep = (req.CanteenRep ?? "").Trim();
            var schoolRep = (req.SchoolRep ?? "").Trim();

            var doc = new Document();
            doc.Info.Title = "Revenue Report";

            var normal = doc.Styles["Normal"];
            normal.Font.Name = "Segoe UI";
            normal.Font.Size = 10;

            var section = doc.AddSection();
            section.PageSetup.PageFormat = PageFormat.A4;
            section.PageSetup.TopMargin = Unit.FromCentimeter(1.5);
            section.PageSetup.BottomMargin = Unit.FromCentimeter(1.5);
            section.PageSetup.LeftMargin = Unit.FromCentimeter(1.5);
            section.PageSetup.RightMargin = Unit.FromCentimeter(1.5);

            var title = section.AddParagraph("BÁO CÁO DOANH THU CĂNG-TIN");
            title.Format.Font.Bold = true;
            title.Format.Font.Size = 14;
            title.Format.Alignment = ParagraphAlignment.Center;
            title.Format.SpaceAfter = Unit.FromCentimeter(0.3);

            var meta = section.AddParagraph();
            meta.Format.SpaceAfter = Unit.FromCentimeter(0.25);
            meta.AddFormattedText($"Trường: {schoolName}", TextFormat.Bold);

            section.AddParagraph($"Kỳ báo cáo: {(mode == "week" ? "Tuần" : "Tháng")}");
            section.AddParagraph($"Từ ngày: {FormatDate(start)}");
            section.AddParagraph($"Đến ngày: {FormatDate(end)}");
            section.AddParagraph($"Người xuất báo cáo: {exportedBy}");
            section.AddParagraph($"Thời gian xuất: {FormatDateTime(exportTimeLocal)}");

            section.AddParagraph().Format.SpaceAfter = Unit.FromCentimeter(0.2);

            var pI = section.AddParagraph("I. PDF BÁO CÁO DOANH THU THEO TUẦN / THÁNG (PDF CHÍNH)");
            pI.Format.Font.Bold = true;
            section.AddParagraph("Báo cáo này tổng hợp doanh thu căng-tin theo tuần hoặc tháng, dùng cho công tác quản lý, kiểm tra và lưu trữ của nhà trường.");

            section.AddParagraph().Format.SpaceAfter = Unit.FromCentimeter(0.2);

            var pII = section.AddParagraph("II. TỔNG DOANH THU TRONG KỲ (TUẦN / THÁNG)");
            pII.Format.Font.Bold = true;
            section.AddParagraph($"Tổng doanh thu hệ thống: {FormatMoney(totalRevenue)}");
            section.AddParagraph($"Tổng số đơn hàng: {totalOrders.ToString("#,0", culture)} đơn");
            section.AddParagraph($"Tiền mặt: {FormatMoney(totalCash)}");
            section.AddParagraph($"QR / Ví điện tử: {FormatMoney(totalQr)}");
            section.AddParagraph($"Online (đặt trước): {FormatMoney(totalOnline)}");

            section.AddParagraph().Format.SpaceAfter = Unit.FromCentimeter(0.2);

            var pIII = section.AddParagraph("III. DOANH THU THEO NGÀY");
            pIII.Format.Font.Bold = true;

            var table = section.AddTable();
            table.Borders.Width = 0.5;
            table.Borders.Color = Colors.Gray;
            table.Format.Font.Size = 9;

            table.AddColumn(Unit.FromCentimeter(2.6)); // date
            table.AddColumn(Unit.FromCentimeter(3.2)); // total
            table.AddColumn(Unit.FromCentimeter(2.6)); // cash
            table.AddColumn(Unit.FromCentimeter(2.6)); // qr
            table.AddColumn(Unit.FromCentimeter(2.6)); // online
            table.AddColumn(Unit.FromCentimeter(2.0)); // orders
            table.AddColumn(Unit.FromCentimeter(2.6)); // status

            var header = table.AddRow();
            header.Shading.Color = Colors.LightGray;
            header.Format.Font.Bold = true;
            header.Cells[0].AddParagraph("Ngày");
            header.Cells[1].AddParagraph("Tổng doanh thu");
            header.Cells[2].AddParagraph("Tiền mặt");
            header.Cells[3].AddParagraph("QR");
            header.Cells[4].AddParagraph("Online");
            header.Cells[5].AddParagraph("Số đơn");
            header.Cells[6].AddParagraph("Trạng thái");

            for (var i = 1; i <= 5; i++) header.Cells[i].Format.Alignment = ParagraphAlignment.Right;
            header.Cells[6].Format.Alignment = ParagraphAlignment.Center;

            foreach (var x in points)
            {
                var r = table.AddRow();
                r.Cells[0].AddParagraph(FormatDate(x.Date));
                r.Cells[1].AddParagraph(FormatMoney(x.TotalRevenue));
                r.Cells[2].AddParagraph(FormatMoney(x.TotalCash));
                r.Cells[3].AddParagraph(FormatMoney(x.TotalQr));
                r.Cells[4].AddParagraph(FormatMoney(x.TotalOnline));
                r.Cells[5].AddParagraph(x.TotalOrders.ToString("#,0", culture));
                r.Cells[6].AddParagraph(x.IsClosed ? "Đã chốt" : "Chưa chốt");
                for (var i = 1; i <= 5; i++) r.Cells[i].Format.Alignment = ParagraphAlignment.Right;
                r.Cells[6].Format.Alignment = ParagraphAlignment.Center;
            }

            section.AddParagraph().Format.SpaceAfter = Unit.FromCentimeter(0.2);

            var pIV = section.AddParagraph("IV. TỔNG HỢP CHỐT NGÀY TRONG KỲ");
            pIV.Format.Font.Bold = true;
            section.AddParagraph($"Tổng số ngày trong kỳ: {totalDays.ToString("#,0", culture)} ngày");
            section.AddParagraph($"Số ngày đã chốt: {closedDays.ToString("#,0", culture)} ngày");
            section.AddParagraph($"Số ngày chưa chốt: {unclosedDays.ToString("#,0", culture)} ngày");
            var pUnclosed = section.AddParagraph("Danh sách ngày chưa chốt:");
            pUnclosed.Format.SpaceBefore = Unit.FromCentimeter(0.1);
            if (unclosedList.Count == 0)
            {
                section.AddParagraph("(Không có)");
            }
            else
            {
                foreach (var d in unclosedList)
                    section.AddParagraph(FormatDate(d));
            }

            section.AddParagraph().Format.SpaceAfter = Unit.FromCentimeter(0.2);

            var pV = section.AddParagraph("V. TỔNG HỢP CHÊNH LỆCH THEO KỲ");
            pV.Format.Font.Bold = true;
            section.AddParagraph($"Tổng tiền nhân viên khai báo: {FormatMoney(declaredCashTotal)}");
            section.AddParagraph($"Tổng doanh thu hệ thống: {FormatMoney(totalCash)}");
            section.AddParagraph($"Chênh lệch toàn kỳ: {FormatMoney(variancePeriod)}");

            section.AddParagraph().Format.SpaceAfter = Unit.FromCentimeter(0.2);

            var pVI = section.AddParagraph("VI. XÁC NHẬN");
            pVI.Format.Font.Bold = true;
            section.AddParagraph($"Người lập báo cáo: {(string.IsNullOrWhiteSpace(preparedBy) ? "........................................................" : preparedBy)}");
            section.AddParagraph($"Đại diện căn-tin: {(string.IsNullOrWhiteSpace(canteenRep) ? "........................................................" : canteenRep)}");
            section.AddParagraph($"Đại diện nhà trường: {(string.IsNullOrWhiteSpace(schoolRep) ? "........................................................" : schoolRep)}");
            section.AddParagraph($"Ngày xác nhận: {FormatDate(confirmedLocalDate)}");

            var renderer = new PdfDocumentRenderer(unicode: true) { Document = doc };
            renderer.RenderDocument();

            using var ms = new MemoryStream();
            renderer.PdfDocument.Save(ms, closeStream: false);
            var bytes = ms.ToArray();

            var filename = $"admin-revenue-report-{mode}-{start:yyyyMMdd}-{end:yyyyMMdd}.pdf";
            return File(bytes, "application/pdf", filename);
        }
    }
}
