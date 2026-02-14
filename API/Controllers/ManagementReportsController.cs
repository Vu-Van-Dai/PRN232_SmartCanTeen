using Core.Entities;
using Core.Enums;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using API.Services;
using System.Text;
using MigraDocCore.DocumentObjectModel;
using MigraDocCore.DocumentObjectModel.Tables;
using MigraDocCore.Rendering;
using System.Globalization;

namespace API.Controllers
{
    [ApiController]
    [Route("api/management/reports")]
    [Authorize(Roles = "Manager,AdminSystem")]
    public class ManagementReportsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly BusinessDayClock _clock;

        public ManagementReportsController(AppDbContext db, BusinessDayClock clock)
        {
            _db = db;
            _clock = clock;
        }

        /// <summary>
        /// Báo cáo tổng hợp theo ngày (realtime theo ca trong ngày vận hành)
        /// </summary>
        [HttpGet("daily")]
        public async Task<IActionResult> GetDailyReport(
            DateTime date)
        {
            var dateLocal = DateOnly.FromDateTime(date);
            var (fromUtc, toUtc) = _clock.GetOperationalWindowUtc(dateLocal);

            var allowedStatuses = new[]
            {
                OrderStatus.Paid,
                OrderStatus.SystemHolding,
                OrderStatus.Preparing,
                OrderStatus.Ready,
                OrderStatus.Completed,
            };

            var shifts = await _db.Shifts
                // Include any shift that overlaps the operational window.
                .Where(x =>
                    x.OpenedAt < toUtc &&
                    (x.ClosedAt == null || x.ClosedAt >= fromUtc))
                .Select(x => new
                {
                    x.Id,
                    x.UserId,
                    status = x.Status.ToString(),
                    openedByName = x.User.FullName,
                    x.OpenedAt,
                    x.ClosedAt,

                    x.SystemCashTotal,
                    x.SystemQrTotal,
                    x.SystemOnlineTotal,

                    x.StaffCashInput,
                    x.StaffQrInput
                })
                .ToListAsync();

            var totalOrders = await _db.Orders
                .Where(o =>
                    o.CreatedAt >= fromUtc &&
                    o.CreatedAt < toUtc &&
                    allowedStatuses.Contains(o.Status))
                .CountAsync();

            var totalItemsSold = await _db.OrderItems
                .Where(oi =>
                    oi.Order.CreatedAt >= fromUtc &&
                    oi.Order.CreatedAt < toUtc &&
                    allowedStatuses.Contains(oi.Order.Status))
                .SumAsync(oi => (int?)oi.Quantity) ?? 0;

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

            var stats = new
            {
                totalOrders,
                totalItemsSold
            };

            return Ok(new
            {
                date = dateLocal.ToDateTime(TimeOnly.MinValue),
                shifts,
                summary,
                stats
            });
        }

        [HttpGet("shift/{shiftId}/report")]
        public async Task<IActionResult> GetShiftReport(Guid shiftId)
        {
            var shift = await _db.Shifts
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.Id == shiftId);

            if (shift == null)
                return NotFound();

            var orders = await _db.Orders
                .Where(o => o.ShiftId == shiftId)
                .Include(o => o.OrderedByUser)
                .Include(o => o.Items)
                    .ThenInclude(i => i.Item)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            var operationalDate = _clock.GetOperationalDateFromUtc(shift.OpenedAt);

            var staffName = shift.User.FullName ?? shift.User.Email;

            var orderCount = orders.Count;
            var totalItemsSold = orders
                .Where(o => o.Status != OrderStatus.Cancelled)
                .SelectMany(o => o.Items)
                .Sum(i => i.Quantity);

            var response = new
            {
                shiftId = shift.Id,
                operationalDate = operationalDate.ToDateTime(TimeOnly.MinValue),
                openedAt = shift.OpenedAt,
                closedAt = shift.ClosedAt,
                status = shift.Status.ToString(),
                openedBy = new { id = shift.UserId, name = staffName },

                revenue = new
                {
                    cashPos = shift.SystemCashTotal,
                    qrPos = shift.SystemQrTotal,
                    online = shift.SystemOnlineTotal,
                    total = shift.SystemCashTotal + shift.SystemQrTotal + shift.SystemOnlineTotal
                },

                stats = new
                {
                    totalOrders = orderCount,
                    totalItemsSold
                },

                orders = orders.Select(o => new
                {
                    orderId = o.Id,
                    createdAt = o.CreatedAt,
                    source = o.OrderSource == OrderSource.Online
                        ? "Online"
                        : (o.PaymentMethod == PaymentMethod.Cash ? "Cash" : "QR"),
                    subTotal = o.Items.Sum(i => i.UnitPrice * i.Quantity),
                    discountAmount = o.DiscountAmount,
                    vatRate = 0.08m,
                    vatAmount = Math.Max(
                        0m,
                        o.TotalPrice - ((o.Items.Sum(i => i.UnitPrice * i.Quantity)) - o.DiscountAmount)
                    ),
                    totalPrice = o.TotalPrice,
                    status = o.Status.ToString(),
                    createdBy = o.OrderSource == OrderSource.Online
                        ? new { type = "User", name = (o.OrderedByUser.FullName ?? o.OrderedByUser.Email) }
                        : new { type = "POS", name = $"POS - {staffName}" },
                    items = o.Items.Select(i => new
                    {
                        itemId = i.ItemId,
                        name = i.Item.Name,
                        quantity = i.Quantity,
                        unitPrice = i.UnitPrice,
                        lineTotal = i.UnitPrice * i.Quantity
                    })
                })
            };

            return Ok(response);
        }

        [HttpGet("day-status")]
        public async Task<IActionResult> GetDayStatus(DateTime date)
        {
            var dateLocal = DateOnly.FromDateTime(date);
            var localNow = _clock.LocalNow;

            var dayKeyUtc = DateTime.SpecifyKind(dateLocal.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var isClosed = await _db.DailyRevenues.AnyAsync(x => x.Date == dayKeyUtc);

            // Locked window applies to the current local time only.
            var isLockedNow = !_clock.IsPosWindowOpen(localNow);
            var currentOperationalDate = _clock.GetOperationalDate(localNow);

            return Ok(new
            {
                date = dateLocal.ToDateTime(TimeOnly.MinValue),
                isClosed,
                isLockedNow,
                currentOperationalDate = currentOperationalDate.ToDateTime(TimeOnly.MinValue)
            });
        }

        /// <summary>
        /// Biểu mẫu chốt ngày: tổng hợp số lượng bán ra theo món (theo ngày vận hành 05:00 -> 05:00)
        /// </summary>
        [HttpGet("daily-sales")]
        public async Task<IActionResult> GetDailySalesReport(DateTime date)
        {
            var dateLocal = DateOnly.FromDateTime(date);
            var (fromUtc, toUtc) = _clock.GetOperationalWindowUtc(dateLocal);

            const decimal vatRate = 0.08m;

            var allowedStatuses = new[]
            {
                OrderStatus.Paid,
                OrderStatus.Preparing,
                OrderStatus.Ready,
                OrderStatus.Completed,
            };

            var items = await _db.OrderItems
                .Where(oi =>
                    oi.Order.CreatedAt >= fromUtc &&
                    oi.Order.CreatedAt < toUtc &&
                    allowedStatuses.Contains(oi.Order.Status))
                .GroupBy(oi => new { oi.ItemId, oi.Item.Name, oi.Item.ImageUrl })
                .Select(g => new
                {
                    itemId = g.Key.ItemId,
                    name = g.Key.Name,
                    imageUrl = g.Key.ImageUrl,
                    quantity = g.Sum(x => x.Quantity),
                    grossAmount = g.Sum(x => x.UnitPrice * x.Quantity)
                })
                .OrderByDescending(x => x.quantity)
                .ThenBy(x => x.name)
                .ToListAsync();

            // Placeholder discount for future promo-code logic
            var itemsWithMoney = items.Select(x => new
            {
                x.itemId,
                x.name,
                x.imageUrl,
                x.quantity,
                grossAmount = x.grossAmount,
                discountAmount = 0m,
                vatRate,
                vatAmount = (x.grossAmount - 0m) * vatRate,
                totalAmount = (x.grossAmount - 0m) * (1m + vatRate)
            }).ToList();

            var totals = new
            {
                totalItems = itemsWithMoney.Sum(x => x.quantity),
                totalGrossAmount = itemsWithMoney.Sum(x => x.grossAmount),
                totalDiscountAmount = itemsWithMoney.Sum(x => x.discountAmount),
                totalVatAmount = itemsWithMoney.Sum(x => x.vatAmount),
                totalAmount = itemsWithMoney.Sum(x => x.totalAmount)
            };

            return Ok(new
            {
                date = dateLocal.ToDateTime(TimeOnly.MinValue),
                items = itemsWithMoney,
                totals
            });
        }

        /// <summary>
        /// Tải biểu mẫu chốt ngày (CSV)
        /// </summary>
        [HttpGet("daily-sales/export")]
        public async Task<IActionResult> ExportDailySalesReport(DateTime date)
        {
            var dateLocal = DateOnly.FromDateTime(date);

            const decimal vatRate = 0.08m;

            // Reuse JSON endpoint logic
            var (fromUtc, toUtc) = _clock.GetOperationalWindowUtc(dateLocal);
            var allowedStatuses = new[]
            {
                OrderStatus.Paid,
                OrderStatus.Preparing,
                OrderStatus.Ready,
                OrderStatus.Completed,
            };

            var items = await _db.OrderItems
                .Where(oi =>
                    oi.Order.CreatedAt >= fromUtc &&
                    oi.Order.CreatedAt < toUtc &&
                    allowedStatuses.Contains(oi.Order.Status))
                .GroupBy(oi => new { oi.ItemId, oi.Item.Name })
                .Select(g => new
                {
                    itemId = g.Key.ItemId,
                    name = g.Key.Name,
                    quantity = g.Sum(x => x.Quantity),
                    grossAmount = g.Sum(x => x.UnitPrice * x.Quantity)
                })
                .OrderByDescending(x => x.quantity)
                .ThenBy(x => x.name)
                .ToListAsync();

            static string CsvEscape(string? s)
            {
                s ??= "";
                if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
                {
                    return "\"" + s.Replace("\"", "\"\"") + "\"";
                }

                return s;
            }

            // Placeholder discount for future promo-code logic
            var rows = items.Select(x => new
            {
                x.itemId,
                x.name,
                x.quantity,
                grossAmount = x.grossAmount,
                discountAmount = 0m,
                vatAmount = (x.grossAmount - 0m) * vatRate,
                totalAmount = (x.grossAmount - 0m) * (1m + vatRate)
            }).ToList();

            var totalItems = rows.Sum(x => x.quantity);
            var totalGross = rows.Sum(x => x.grossAmount);
            var totalDiscount = rows.Sum(x => x.discountAmount);
            var totalVat = rows.Sum(x => x.vatAmount);
            var totalAmount = rows.Sum(x => x.totalAmount);

            var sb = new StringBuilder();
            // UTF-8 BOM for Excel compatibility
            sb.Append('\uFEFF');
            sb.AppendLine($"Ngay,{dateLocal:yyyy-MM-dd}");
            sb.AppendLine($"VAT,{vatRate:P0}");
            sb.AppendLine("MaMon,TenMon,SoLuong,Goc,Discount,VAT,ToanBo");
            foreach (var x in rows)
            {
                sb.AppendLine(
                    $"{x.itemId},{CsvEscape(x.name)},{x.quantity},{x.grossAmount},{x.discountAmount},{x.vatAmount},{x.totalAmount}");
            }
            sb.AppendLine($"TOTAL,,{totalItems},{totalGross},{totalDiscount},{totalVat},{totalAmount}");

            var filename = $"daily-sales-{dateLocal:yyyyMMdd}.csv";
            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", filename);
        }

        /// <summary>
        /// Tải biểu mẫu chốt ngày (PDF)
        /// </summary>
        [HttpGet("daily-sales/export-pdf")]
        public async Task<IActionResult> ExportDailySalesReportPdf(DateTime date)
        {
            var dateLocal = DateOnly.FromDateTime(date);
            var (fromUtc, toUtc) = _clock.GetOperationalWindowUtc(dateLocal);

            const decimal vatRate = 0.08m;
            var allowedStatuses = new[]
            {
                OrderStatus.Paid,
                OrderStatus.Preparing,
                OrderStatus.Ready,
                OrderStatus.Completed,
            };

            var items = await _db.OrderItems
                .Where(oi =>
                    oi.Order.CreatedAt >= fromUtc &&
                    oi.Order.CreatedAt < toUtc &&
                    allowedStatuses.Contains(oi.Order.Status))
                .GroupBy(oi => new { oi.ItemId, oi.Item.Name })
                .Select(g => new
                {
                    itemId = g.Key.ItemId,
                    name = g.Key.Name,
                    quantity = g.Sum(x => x.Quantity),
                    grossAmount = g.Sum(x => x.UnitPrice * x.Quantity)
                })
                .OrderByDescending(x => x.quantity)
                .ThenBy(x => x.name)
                .ToListAsync();

            var rows = items.Select(x => new
            {
                x.itemId,
                x.name,
                x.quantity,
                grossAmount = x.grossAmount,
                discountAmount = 0m,
                vatAmount = (x.grossAmount - 0m) * vatRate,
                totalAmount = (x.grossAmount - 0m) * (1m + vatRate)
            }).ToList();

            var totalItems = rows.Sum(x => x.quantity);
            var totalGross = rows.Sum(x => x.grossAmount);
            var totalDiscount = rows.Sum(x => x.discountAmount);
            var totalVat = rows.Sum(x => x.vatAmount);
            var totalAmount = rows.Sum(x => x.totalAmount);

            var culture = CultureInfo.GetCultureInfo("vi-VN");
            string FormatMoney(decimal v) => v.ToString("#,0", culture) + " đ";

            var doc = new Document();
            doc.Info.Title = "Daily Sales Report";

            var normal = doc.Styles["Normal"];
            normal.Font.Name = "Segoe UI";
            normal.Font.Size = 9;

            var section = doc.AddSection();
            section.PageSetup.Orientation = Orientation.Landscape;
            section.PageSetup.PageFormat = PageFormat.A4;
            section.PageSetup.TopMargin = Unit.FromCentimeter(1.2);
            section.PageSetup.BottomMargin = Unit.FromCentimeter(1.2);
            section.PageSetup.LeftMargin = Unit.FromCentimeter(1.2);
            section.PageSetup.RightMargin = Unit.FromCentimeter(1.2);

            var title = section.AddParagraph("SMART CANTEEN");
            title.Format.Font.Bold = true;
            title.Format.Font.Size = 14;
            title.Format.Alignment = ParagraphAlignment.Center;

            var sub = section.AddParagraph("BIỂU MẪU SỐ LƯỢNG BÁN RA (NGÀY VẬN HÀNH)");
            sub.Format.Font.Bold = true;
            sub.Format.SpaceAfter = Unit.FromCentimeter(0.2);
            sub.Format.Alignment = ParagraphAlignment.Center;

            var meta = section.AddParagraph($"Ngày: {dateLocal:dd/MM/yyyy}    VAT: {vatRate:P0}    Khung giờ: 05:00 → 05:00");
            meta.Format.SpaceAfter = Unit.FromCentimeter(0.4);
            meta.Format.Alignment = ParagraphAlignment.Center;

            var table = section.AddTable();
            table.Borders.Width = 0.5;
            table.Borders.Color = Colors.Gray;

            table.AddColumn(Unit.FromCentimeter(3.0));  // itemId (short)
            table.AddColumn(Unit.FromCentimeter(8.0));  // name
            table.AddColumn(Unit.FromCentimeter(2.4));  // qty
            table.AddColumn(Unit.FromCentimeter(3.2));  // gross
            table.AddColumn(Unit.FromCentimeter(3.2));  // discount
            table.AddColumn(Unit.FromCentimeter(3.2));  // vat
            table.AddColumn(Unit.FromCentimeter(3.4));  // total

            var header = table.AddRow();
            header.Shading.Color = Colors.LightGray;
            header.Format.Font.Bold = true;
            header.Cells[0].AddParagraph("Mã món");
            header.Cells[1].AddParagraph("Tên món");
            header.Cells[2].AddParagraph("Số lượng");
            header.Cells[3].AddParagraph("Giá gốc");
            header.Cells[4].AddParagraph("Discount");
            header.Cells[5].AddParagraph("VAT");
            header.Cells[6].AddParagraph("Toàn bộ");
            for (var i = 2; i <= 6; i++) header.Cells[i].Format.Alignment = ParagraphAlignment.Right;
            header.Cells[0].Format.Alignment = ParagraphAlignment.Left;
            header.Cells[1].Format.Alignment = ParagraphAlignment.Left;

            foreach (var x in rows)
            {
                var r = table.AddRow();
                r.Cells[0].AddParagraph(x.itemId.ToString("N").Substring(0, 8));
                r.Cells[1].AddParagraph(x.name);
                r.Cells[2].AddParagraph(x.quantity.ToString(culture));
                r.Cells[3].AddParagraph(FormatMoney(x.grossAmount));
                r.Cells[4].AddParagraph(FormatMoney(x.discountAmount));
                r.Cells[5].AddParagraph(FormatMoney(x.vatAmount));
                r.Cells[6].AddParagraph(FormatMoney(x.totalAmount));

                for (var i = 2; i <= 6; i++) r.Cells[i].Format.Alignment = ParagraphAlignment.Right;
            }

            var totalRow = table.AddRow();
            totalRow.Format.Font.Bold = true;
            totalRow.Shading.Color = Colors.LightYellow;
            totalRow.Cells[0].MergeRight = 1;
            totalRow.Cells[0].AddParagraph("TỔNG");
            totalRow.Cells[2].AddParagraph(totalItems.ToString(culture));
            totalRow.Cells[3].AddParagraph(FormatMoney(totalGross));
            totalRow.Cells[4].AddParagraph(FormatMoney(totalDiscount));
            totalRow.Cells[5].AddParagraph(FormatMoney(totalVat));
            totalRow.Cells[6].AddParagraph(FormatMoney(totalAmount));
            for (var i = 2; i <= 6; i++) totalRow.Cells[i].Format.Alignment = ParagraphAlignment.Right;

            var renderer = new PdfDocumentRenderer(unicode: true)
            {
                Document = doc
            };
            renderer.RenderDocument();

            using var ms = new MemoryStream();
            renderer.PdfDocument.Save(ms, closeStream: false);
            var bytes = ms.ToArray();

            var filename = $"daily-sales-{dateLocal:yyyyMMdd}.pdf";
            return File(bytes, "application/pdf", filename);
        }

        [HttpPost("close-day")]
        [Authorize(Roles = "Manager,AdminSystem")]
        public async Task<IActionResult> CloseDay(DateTime date)
        {
            var dateLocal = DateOnly.FromDateTime(date);
            var (fromUtc, toUtc) = _clock.GetOperationalWindowUtc(dateLocal);

            var shifts = await _db.Shifts
                .Where(x =>
                    x.Status == ShiftStatus.Closed &&
                    x.OpenedAt >= fromUtc &&
                    x.OpenedAt < toUtc)
                .ToListAsync();

            if (!shifts.Any())
                return BadRequest("No closed shifts");

            var dayKeyUtc = DateTime.SpecifyKind(dateLocal.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var exists = await _db.DailyRevenues.AnyAsync(x => x.Date == dayKeyUtc);

            if (exists)
                return BadRequest("Day already closed");

            var anyActiveShift = await _db.Shifts.AnyAsync(x =>
                x.Status != ShiftStatus.Closed &&
                x.OpenedAt >= fromUtc &&
                x.OpenedAt < toUtc);
            if (anyActiveShift)
                return BadRequest("There are still active shifts");

            var managerId = Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!
            );

            var daily = new DailyRevenue
            {
                Id = Guid.NewGuid(),
                Date = dayKeyUtc,

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
        [Authorize(Roles = "Manager,AdminSystem")]
        public async Task<IActionResult> GetDashboardSnapshot()
        {
            var opDate = _clock.GetOperationalDate(_clock.LocalNow);
            var (fromUtc, toUtc) = _clock.GetOperationalWindowUtc(opDate);

            var shifts = await _db.Shifts
                .Where(x =>
                    x.OpenedAt >= fromUtc &&
                    x.OpenedAt < toUtc)
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

        [HttpGet("dashboard/hourly")]
        public async Task<IActionResult> GetDashboardHourly(DateTime date)
        {
            var dateLocal = DateOnly.FromDateTime(date);
            var (fromUtc, toUtc) = _clock.GetOperationalWindowUtc(dateLocal);

            var startLocal = _clock.GetOperationalStartLocal(dateLocal);

            var allowedStatuses = new[]
            {
                OrderStatus.Paid,
                OrderStatus.Preparing,
                OrderStatus.Ready,
                OrderStatus.Completed,
            };

            var orders = await _db.Orders
                .Where(o =>
                    o.CreatedAt >= fromUtc &&
                    o.CreatedAt < toUtc &&
                    allowedStatuses.Contains(o.Status))
                .Select(o => new { o.CreatedAt, o.TotalPrice })
                .ToListAsync();

            var totals = new decimal[24];
            foreach (var o in orders)
            {
                var localTime = _clock.ConvertUtcToLocal(o.CreatedAt);
                var idx = (int)Math.Floor((localTime - startLocal).TotalHours);
                if (idx >= 0 && idx < 24)
                {
                    totals[idx] += o.TotalPrice;
                }
            }

            var points = Enumerable.Range(0, 24)
                .Select(i => new
                {
                    hour = startLocal.AddHours(i).ToString("HH:00"),
                    value = totals[i]
                })
                .ToList();

            return Ok(new
            {
                date = dateLocal.ToDateTime(TimeOnly.MinValue),
                points
            });
        }
    }
}
