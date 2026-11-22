using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserEntity = pameka_7033_l005.Models.User;
using pameka_7033_l005.Helpers;
using pameka_7033_l005.Models;
using pameka_7033_l005.ViewModels;

namespace pameka_7033_l005.Controllers;

// ผู้จัดการ: Dashboard + ฐาน AdminAreaController, การจอง, ห้อง, บริการ, โปรโมชั่น, รายงาน, รีวิว, พนักงาน

// ======================================================================
// AdminController
// ======================================================================
/// <summary>
/// ฐานของทุกหน้าหลังบ้านของผู้จัดการ: เข้าได้เฉพาะบทบาท admin (Authorize สืบทอดไปยังทุกคลาสที่สืบจากตัวนี้)
/// และมีตัวช่วย "เพิ่ม/แก้ไข" ที่ใช้ร่วมกันทุกหน้าจัดการข้อมูล
/// </summary>
[Authorize(Roles = RoleNames.Admin)]
public abstract class AdminAreaController : AppController
{
    protected readonly Csi402dbContext Db;
    protected AdminAreaController(Csi402dbContext db) => Db = db;

    /// <summary>
    /// บันทึกฟอร์มจัดการข้อมูล: id = 0 คือเพิ่มใหม่ (ถ้ามี create) มิฉะนั้นแก้ไขรายการเดิม
    /// ตรวจ ModelState ก่อนเสมอ และจับกรณีชื่อ/เลขซ้ำ (unique) จากฐานข้อมูลให้เป็นข้อความที่อ่านเข้าใจ
    /// </summary>
    protected async Task<IActionResult> SaveAsync<T>(DbSet<T> set, int id, Func<T>? create, Action<T> apply, string what, string action = "Index") where T : class
    {
        if (!ModelState.IsValid) { FlashModelErrors(); return RedirectToAction(action); }

        T? entity;
        if (id == 0)
        {
            if (create == null) return Stale(action);
            entity = create();
            set.Add(entity);
        }
        else
        {
            entity = await set.FindAsync(id);
            if (entity == null) return Stale(action);
        }

        apply(entity);
        try { await Db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            Flash($"บันทึก{what}ไม่สำเร็จ (ชื่อหรือเลขอาจซ้ำกับรายการเดิม)", true);
            return RedirectToAction(action);
        }
        Flash($"บันทึก{what}แล้ว");
        return RedirectToAction(action);
    }
}

public class AdminController : AdminAreaController
{
    public AdminController(Csi402dbContext db) : base(db) { }

    private static readonly string[] Sold = { "CONFIRMED", "CHECKED_IN", "CHECKED_OUT" };

    public async Task<IActionResult> Index()
    {
        var today = DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today);
        var vm = new DashboardViewModel
        {
            Bookings = await Db.Bookings.CountAsync(b => b.Status != "CANCELLED"),
            Rooms = await Db.Rooms.CountAsync(),
            Customers = await Db.Users.CountAsync(u => u.Role.RoleName == RoleNames.Customer),
            PaymentsToVerify = await Db.Payments.CountAsync(p => p.PaymentType == BookingWorkflow.PaymentType && p.Status == "PENDING"),
            RefundsPending = await Db.Payments.CountAsync(p => p.PaymentType == BookingWorkflow.RefundType && p.Status == "PENDING"),
            OpenHousekeeping = await Db.HousekeepingTasks.CountAsync(t => t.Status != "DONE"),
            OpenDelivery = await Db.DeliveryTasks.CountAsync(t => t.Status != "DONE"),
            PendingBookings = await Db.Bookings.CountAsync(b => b.Status == "PENDING"),
            ArrivalsToday = await Db.Bookings.CountAsync(b => b.CheckIn == today && (b.Status == "CONFIRMED" || b.Status == "PENDING")),
            DeparturesToday = await Db.Bookings.CountAsync(b => b.CheckOut == today && b.Status == "CHECKED_IN"),
            Reviews = await Db.Reviews.CountAsync(),
            RoomsByStatus = (await Db.Rooms.AsNoTracking().Select(r => r.Status).ToListAsync()).GroupBy(x => x).OrderBy(g => g.Key).Select(g => (g.Key, g.Count())).ToList()
        };
        vm.OpenTasks = vm.OpenHousekeeping + vm.OpenDelivery;

        // ---- รายรับ / การจองใหม่: 60 วันล่าสุด แบ่งเป็น 30 วันนี้ กับ 30 วันก่อนหน้า (คำนวณในหน่วยความจำ ช่วงข้อมูลจำกัด)
        var from30 = today.AddDays(-29);
        var start60 = today.AddDays(-59).ToDateTime(TimeOnly.MinValue);
        var start30 = from30.ToDateTime(TimeOnly.MinValue);
        var paidAll = await Db.Payments.AsNoTracking()
            .Where(p => p.PaymentType == BookingWorkflow.PaymentType && p.Status == "PAID")
            .Select(p => new { p.PaidAt, p.Amount }).ToListAsync();
        vm.Revenue = paidAll.Sum(p => p.Amount);
        var paid60 = paidAll.Where(p => p.PaidAt >= start60).ToList();
        vm.Revenue30 = paid60.Where(p => p.PaidAt >= start30).Sum(p => p.Amount);
        vm.RevenuePrev30 = paid60.Where(p => p.PaidAt < start30).Sum(p => p.Amount);
        var booked60 = await Db.Bookings.AsNoTracking().Where(b => b.BookedAt >= start60).Select(b => b.BookedAt).ToListAsync();
        vm.NewBookings30 = booked60.Count(d => d >= start30);
        vm.NewBookingsPrev30 = booked60.Count(d => d < start30);
        for (var d = from30; d <= today; d = d.AddDays(1))
        {
            var day = d;
            vm.DailyRevenue.Add((day, paid60.Where(p => p.PaidAt.HasValue && DateOnly.FromDateTime(p.PaidAt.Value) == day).Sum(p => p.Amount)));
            vm.DailyBookings.Add((day, booked60.Count(x => DateOnly.FromDateTime(x) == day)));
        }

        // ---- การเข้าพัก: คืนนี้, 30 คืนที่ผ่านมา (อัตราเข้าพัก/ADR) และคาดการณ์ 14 คืนข้างหน้า
        var horizon = today.AddDays(14);
        var stays = await Db.BookingRooms.AsNoTracking()
            .Where(x => x.Booking.Status != "CANCELLED" && x.Booking.CheckIn < horizon && x.Booking.CheckOut > from30)
            .Select(x => new { x.Booking.CheckIn, x.Booking.CheckOut, x.Booking.Status, x.PricePerNight, Type = x.Room.Type.TypeName }).ToListAsync();
        var sold = stays.Where(x => Sold.Contains(x.Status)).ToList();
        vm.OccupiedTonight = sold.Count(x => x.CheckIn <= today && x.CheckOut > today);
        decimal roomRev = 0; var nights = 0;
        for (var d = from30; d <= today; d = d.AddDays(1))
        {
            var n = sold.Where(x => x.CheckIn <= d && x.CheckOut > d).ToList();
            nights += n.Count; roomRev += n.Sum(x => x.PricePerNight);
        }
        var capacity = vm.Rooms * 30;
        vm.OccupancyPercent30 = capacity == 0 ? 0 : Math.Round(100m * nights / capacity, 1);
        vm.Adr30 = nights == 0 ? 0 : Math.Round(roomRev / nights, 0);
        for (var d = today; d < horizon; d = d.AddDays(1))
        {
            var day = d;
            var on = stays.Where(x => x.CheckIn <= day && x.CheckOut > day).ToList();
            vm.Forecast.Add((day, on.Count(x => Sold.Contains(x.Status)), on.Count(x => x.Status == "PENDING")));
        }
        vm.TopTypes = sold
            .SelectMany(x => Enumerable.Range(0, x.CheckOut.DayNumber - x.CheckIn.DayNumber).Select(i => new { Night = x.CheckIn.AddDays(i), x.Type, x.PricePerNight }))
            .Where(n => n.Night >= from30 && n.Night <= today)
            .GroupBy(n => n.Type).Select(g => (g.Key, g.Count(), g.Sum(n => n.PricePerNight)))
            .OrderByDescending(t => t.Item3).ToList();

        // ---- รายการการจอง: เข้าพักวันนี้, ออกวันนี้, ล่าสุด
        async Task<List<DashBookingRow>> Rows(IQueryable<Booking> q, int take)
        {
            var raw = await q.Take(take).Select(b => new
            {
                b.BookingId, b.User.FirstName, b.User.LastName, Rooms = b.BookingRooms.Select(r => r.Room.RoomNo).ToList(),
                b.CheckIn, b.CheckOut, b.Guests, b.NetTotal, b.Status, b.BookedAt
            }).ToListAsync();
            return raw.Select(r => new DashBookingRow(r.BookingId, $"{r.FirstName} {r.LastName}", string.Join(", ", r.Rooms.OrderBy(x => x)),
                r.CheckIn, r.CheckOut, r.Guests ?? 0, r.NetTotal, r.Status, r.BookedAt)).ToList();
        }
        var bk = Db.Bookings.AsNoTracking();
        vm.Arrivals = await Rows(bk.Where(b => b.CheckIn == today && (b.Status == "CONFIRMED" || b.Status == "PENDING")).OrderBy(b => b.BookingId), 8);
        vm.Departures = await Rows(bk.Where(b => b.CheckOut == today && b.Status == "CHECKED_IN").OrderBy(b => b.BookingId), 8);
        vm.Recent = await Rows(bk.OrderByDescending(b => b.BookingId), 6);

        // ---- รีวิว
        var ratings = await Db.Reviews.AsNoTracking().Select(r => (int)r.Rating).ToListAsync();
        foreach (var r in ratings) if (r >= 1 && r <= 5) vm.RatingCounts[r]++;
        vm.AvgRating = ratings.Count == 0 ? 0 : Math.Round((decimal)ratings.Average(), 1);
        return View(vm);
    }
}

// ======================================================================
// AdminBookingsController
// ======================================================================
/// <summary>
/// ผู้จัดการดูการจองทั้งหมด (ค้นหา/กรอง/แบ่งหน้า) ดูรายละเอียดครบทั้งห้อง บริการ การเงิน งานส่งของ/ทำความสะอาด รีวิว
/// และยกเลิกการจองได้ — ที่ชำระเงินแล้วจะสร้าง "คำขอคืนเงิน" ให้ฝ่ายบัญชีอนุมัติ (ไม่คืนเงินเองข้ามฝ่ายบัญชี)
/// </summary>
public class AdminBookingsController : AdminAreaController
{
    private static readonly List<string> Statuses = new() { "PENDING", "CONFIRMED", "CHECKED_IN", "CHECKED_OUT", "CANCELLED", "NO_SHOW" };
    public AdminBookingsController(Csi402dbContext db) : base(db) { }

    [HttpGet]
    public async Task<IActionResult> Index(string? status, string? q, int page = 1)
    {
        const int PageSize = 25;
        if (status != null && !Statuses.Contains(status)) status = null;
        q = q?.Trim();

        var baseQuery = Search(q);
        var counts = await baseQuery.GroupBy(b => b.Status).Select(g => new { g.Key, N = g.Count() }).ToListAsync();
        var query = status != null ? baseQuery.Where(b => b.Status == status) : baseQuery;

        var total = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, pages);

        var rows = await query.OrderByDescending(b => b.BookingId).Skip((page - 1) * PageSize).Take(PageSize).Select(b => new
        {
            b.BookingId, b.CheckIn, b.CheckOut, b.BookedAt, Net = b.NetTotal, b.Status,
            Customer = b.User.FirstName + " " + b.User.LastName,
            Rooms = b.BookingRooms.Select(r => r.Room.RoomNo).ToList(),
            Pays = b.Payments.Select(p => new { p.PaymentType, p.Status }).ToList()
        }).ToListAsync();

        return View(new AdminBookingsPageViewModel
        {
            Status = status, Q = q, Page = page, TotalPages = pages, TotalCount = total,
            StatusCounts = counts.ToDictionary(c => c.Key, c => c.N),
            Rows = rows.Select(r => new AdminBookingRowViewModel
            {
                BookingId = r.BookingId, Customer = r.Customer, Rooms = string.Join(", ", r.Rooms),
                CheckIn = r.CheckIn, CheckOut = r.CheckOut, BookedAt = r.BookedAt, Net = r.Net, Status = r.Status,
                PaymentLabel = PaymentLabel(PaymentFlags.From(r.Pays.Select(p => (p.PaymentType, p.Status))))
            }).ToList()
        });
    }

    private IQueryable<Booking> Search(string? q)
    {
        var query = Db.Bookings.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(q))
        {
            var id = int.TryParse(q.TrimStart('#'), out var n) ? n : -1;
            query = query.Where(b => b.BookingId == id || b.User.FirstName.Contains(q) || b.User.LastName.Contains(q)
                                     || b.User.Email.Contains(q) || b.User.Phone.Contains(q));
        }
        return query;
    }

    // Part 42: ส่งออกรายการจองตามตัวกรองเป็น CSV (UTF-8 BOM เปิดใน Excel ภาษาไทยได้)
    [HttpGet]
    public async Task<IActionResult> Export(string? status, string? q)
    {
        if (status != null && !Statuses.Contains(status)) status = null;
        q = q?.Trim();
        var query = Search(q);
        if (status != null) query = query.Where(b => b.Status == status);
        var rows = await query.OrderByDescending(b => b.BookingId).Take(20000).Select(b => new
        {
            b.BookingId, b.CheckIn, b.CheckOut, b.BookedAt, b.NetTotal, b.Status, b.Guests,
            b.User.FirstName, b.User.LastName, b.User.Email, b.User.Phone,
            Rooms = b.BookingRooms.Select(r => r.Room.RoomNo).ToList(),
            Pays = b.Payments.Select(p => new { p.PaymentType, p.Status }).ToList()
        }).ToListAsync();

        static string Cell(string? s)
        {
            s = CsvSafe.Guard(s);   // กันสูตรใน Excel (CSV injection) แต่ไม่ทำให้เบอร์โทร +66… เพี้ยน
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var sb = new System.Text.StringBuilder();
        sb.Append("เลขการจอง,ลูกค้า,อีเมล,เบอร์โทร,ห้อง,ผู้เข้าพัก,เช็คอิน,เช็คเอาท์,คืน,ยอดสุทธิ,สถานะ,การเงิน,จองเมื่อ\r\n");
        foreach (var r in rows)
        {
            sb.AppendJoin(',', new[]
            {
                r.BookingId.ToString(), Cell(r.FirstName + " " + r.LastName), Cell(r.Email), Cell(r.Phone),
                Cell(string.Join(" ", r.Rooms)), (r.Guests ?? 1).ToString(),
                r.CheckIn.ToString("yyyy-MM-dd"), r.CheckOut.ToString("yyyy-MM-dd"), (r.CheckOut.DayNumber - r.CheckIn.DayNumber).ToString(),
                r.NetTotal.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                Cell(ThaiLabels.BookingStatus(r.Status)),
                Cell(PaymentLabel(PaymentFlags.From(r.Pays.Select(p => (p.PaymentType, p.Status))))),
                r.BookedAt.ToString("yyyy-MM-dd HH:mm")
            });
            sb.Append("\r\n");
        }
        var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"bookings-{pameka_7033_l005.Helpers.AppClock.Now:yyyyMMdd-HHmm}.csv");
    }

    private static string PaymentLabel(PaymentFlags f) =>
        f.PendingRefund ? "รอคืนเงิน" : f.Refunded ? "คืนเงินแล้ว" : f.Paid ? "ชำระแล้ว" : f.PendingPayment ? "รอตรวจสอบ" : "ยังไม่ชำระ";

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var b = await Db.Bookings.Include(x => x.Payments).FirstOrDefaultAsync(x => x.BookingId == id);
        if (b == null) return Stale();
        var flags = PaymentFlags.From(b.Payments.Select(p => (p.PaymentType, p.Status)));

        if (b.Status == "PENDING")
        {
            // ยังไม่ยืนยัน: ยกเลิกได้ทันที และตัดรายการแจ้งชำระที่รอตรวจสอบทิ้ง (ยังไม่มีเงินเข้า)
            // สลิปที่รอตรวจเปลี่ยนเป็น "ถูกปฏิเสธ" เก็บไว้เป็นหลักฐาน (ลูกค้าอาจโอนแล้วจริง ฝ่ายบัญชีตรวจย้อนหลังได้)
            foreach (var p in b.Payments.Where(p => p.PaymentType == BookingWorkflow.PaymentType && p.Status == "PENDING"))
            {
                p.Status = "REJECTED"; p.HandledBy = CurrentUserId; p.PaidAt = pameka_7033_l005.Helpers.AppClock.Now;
            }
            b.Status = "CANCELLED";
            Notify.Add(Db, b.UserId, $"การจอง #{id} ถูกยกเลิกโดยโรงแรม",
                "หากคุณโอนเงินแล้ว กรุณาติดต่อโรงแรมเพื่อตรวจสอบ", Notify.BookingLink(id));
            await Db.SaveChangesAsync();
            Flash($"ยกเลิกการจอง #{id} แล้ว");
        }
        else if (BookingWorkflow.CanRequestRefund(b.Status, flags))
        {
            // ยืนยัน+ชำระแล้ว: เปิดคำขอคืนเงินเต็มจำนวนให้ฝ่ายบัญชีอนุมัติ (ฝ่ายบัญชีอนุมัติแล้วจึงยกเลิกและคืนเงิน)
            var paid = b.Payments.First(p => p.PaymentType == BookingWorkflow.PaymentType && p.Status == "PAID");
            b.Payments.Add(new Payment { Amount = paid.Amount, PaymentType = BookingWorkflow.RefundType, Method = paid.Method, Status = "PENDING" });
            Notify.Add(Db, b.UserId, b.Status == "NO_SHOW" ? $"โรงแรมพิจารณาคืนเงินการจอง #{id}" : $"โรงแรมขอยกเลิกการจอง #{id}",
                $"ระบบสร้างคำขอคืนเงิน {paid.Amount:N2} บาทให้แล้ว รอฝ่ายบัญชีอนุมัติ", Notify.BookingLink(id));
            await Db.SaveChangesAsync();
            Flash($"สร้างคำขอคืนเงินของการจอง #{id} แล้ว รอฝ่ายบัญชีอนุมัติ");
        }
        else
        {
            Flash("ยกเลิกไม่ได้ในสถานะนี้ (เช็คอินแล้ว/เสร็จสิ้นแล้ว/ยกเลิกแล้ว หรือมีคำขอคืนเงินอยู่แล้ว)", true);
        }
        return RedirectToAction("Index", "BookingDetails", new { id });
    }
}

// ======================================================================
// AdminRoomsController
// ======================================================================
/// <summary>ผู้จัดการจัดการห้องพัก ประเภทห้อง (ราคา/จำนวนผู้พัก) และธีมห้อง</summary>
public class AdminRoomsController : AdminAreaController
{
    public AdminRoomsController(Csi402dbContext db) : base(db) { }

    [HttpGet]
    public async Task<IActionResult> Index() => View(new RoomsAdminPageViewModel
    {
        Rooms = await Db.Rooms.AsNoTracking().OrderBy(r => r.RoomNo)
            .Select(r => new RoomAdminRowViewModel { RoomId = r.RoomId, RoomNo = r.RoomNo, TypeId = r.TypeId, ThemeId = r.ThemeId, Status = r.Status }).ToListAsync(),
        Types = await Db.RoomTypes.AsNoTracking().OrderBy(t => t.TypeName)
            .Select(t => new RoomTypeEditViewModel { TypeId = t.TypeId, TypeName = t.TypeName, BasePrice = t.BasePrice, WeekendPrice = t.WeekendPrice, MaxGuests = t.MaxGuests,
                Description = t.Description, Amenities = t.Amenities, ImageUrl = t.ImageUrl }).ToListAsync(),
        Themes = await Db.RoomThemes.AsNoTracking().OrderBy(t => t.ThemeName)
            .Select(t => new ThemeEditViewModel { ThemeId = t.ThemeId, ThemeName = t.ThemeName, Description = t.Description }).ToListAsync(),
        Seasons = await Db.RateSeasons.AsNoTracking().OrderByDescending(s => s.IsActive).ThenBy(s => s.StartDate).ToListAsync()
    });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRoom(RoomEditViewModel f)
    {
        if (ModelState.IsValid && !await Db.RoomTypes.AnyAsync(t => t.TypeId == f.TypeId))
            ModelState.AddModelError(nameof(f.TypeId), "ไม่พบประเภทห้องที่เลือก");
        var themeId = f.ThemeId is > 0 ? f.ThemeId : null;
        return await SaveAsync(Db.Rooms, f.RoomId,
            () => new Room { Status = "AVAILABLE" },
            r => { r.RoomNo = f.RoomNo.Trim(); r.TypeId = f.TypeId; r.ThemeId = themeId; }, "ห้องพัก");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRoom(int id)
    {
        var room = await Db.Rooms.FirstOrDefaultAsync(r => r.RoomId == id);
        if (room == null) return Stale();

        var used = await Db.BookingRooms.AnyAsync(b => b.RoomId == id)
                   || await Db.HousekeepingTasks.AnyAsync(t => t.RoomId == id)
                   || await Db.DeliveryTasks.AnyAsync(t => t.RoomId == id);
        if (used)
        {
            Flash($"ลบห้อง {room.RoomNo} ไม่ได้ เพราะมีประวัติการจอง/งานแล้ว (ให้ฝ่ายบริการปิดซ่อมบำรุงแทน)", true);
            return RedirectToAction(nameof(Index));
        }
        Db.Rooms.Remove(room);
        await Db.SaveChangesAsync();
        Flash($"ลบห้อง {room.RoomNo} แล้ว");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>คำอธิบาย สิ่งอำนวยความสะดวก และรูปของประเภทห้อง (รูปใหม่แทนรูปเดิม)</summary>
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<IActionResult> SaveTypeDetails(RoomTypeDetailsViewModel f)
    {
        if (!ModelState.IsValid) { FlashModelErrors(); return RedirectToAction(nameof(Index)); }
        var t = await Db.RoomTypes.FindAsync(f.TypeId);
        if (t == null) return Stale(nameof(Index));

        string? newImage = null;
        if (f.Image is { Length: > 0 })
        {
            var (name, error) = await UploadStore.SaveRoomImageAsync(f.Image);
            if (error != null) { Flash(error, true); return RedirectToAction(nameof(Index)); }
            newImage = "/images/rooms/" + name;
        }

        var oldImage = t.ImageUrl;
        t.Description = string.IsNullOrWhiteSpace(f.Description) ? null : f.Description.Trim();
        // ทำให้เป็นรายการสะอาด: ตัดช่องว่าง ตัดซ้ำ คั่นด้วย ,
        var items = (f.Amenities ?? "").Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
        t.Amenities = items.Count == 0 ? null : string.Join(",", items);
        if (newImage != null) t.ImageUrl = newImage;
        else if (f.RemoveImage) t.ImageUrl = null;
        await Db.SaveChangesAsync();
        if (t.ImageUrl != oldImage) UploadStore.DeleteRoomImage(oldImage);
        Flash($"บันทึกรายละเอียดประเภท {t.TypeName} แล้ว");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> SaveType(RoomTypeEditViewModel f) => SaveAsync(Db.RoomTypes, f.TypeId,
        () => new RoomType(), t => { t.TypeName = f.TypeName.Trim(); t.BasePrice = f.BasePrice; t.WeekendPrice = f.WeekendPrice is > 0 ? f.WeekendPrice : null; t.MaxGuests = f.MaxGuests; }, "ประเภทห้อง");

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> SaveTheme(ThemeEditViewModel f) => SaveAsync(Db.RoomThemes, f.ThemeId,
        () => new RoomTheme(), t => { t.ThemeName = f.ThemeName.Trim(); t.Description = string.IsNullOrWhiteSpace(f.Description) ? null : f.Description.Trim(); }, "ธีมห้อง");

    // ---------------- Part 43: ช่วงราคาพิเศษ (ปรับราคาห้องทุกประเภท +/− % ตามช่วงวันที่)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSeason(SeasonEditViewModel f)
    {
        f.Name = (f.Name ?? "").Trim();
        string? err = f.Name.Length is 0 or > 100 ? "กรุณากรอกชื่อช่วงราคา (ไม่เกิน 100 ตัวอักษร)"
            : f.Start is null || f.End is null ? "กรุณาเลือกวันเริ่มและวันสิ้นสุด"
            : f.End < f.Start ? "วันสิ้นสุดต้องไม่ก่อนวันเริ่ม"
            : f.End.Value.DayNumber - f.Start.Value.DayNumber > 366 ? "ช่วงราคาพิเศษยาวได้ไม่เกิน 1 ปี"
            : f.Percent is null or 0 || f.Percent < RatePlan.MinPercent || f.Percent > RatePlan.MaxPercent ? $"เปอร์เซ็นต์ต้องอยู่ระหว่าง {RatePlan.MinPercent:0} ถึง +{RatePlan.MaxPercent:0} และไม่เป็น 0"
            : null;
        if (err != null) { Flash(err, true); return RedirectToAction(nameof(Index), null, "seasons"); }

        RateSeason? s = f.SeasonId > 0 ? await Db.RateSeasons.FindAsync(f.SeasonId) : new RateSeason { CreatedAt = pameka_7033_l005.Helpers.AppClock.Now, IsActive = true };
        if (s == null) { Flash("ไม่พบช่วงราคาที่เลือก", true); return RedirectToAction(nameof(Index), null, "seasons"); }
        s.SeasonName = f.Name; s.StartDate = f.Start!.Value; s.EndDate = f.End!.Value;
        s.AdjustPercent = Math.Round(f.Percent!.Value, 2);
        if (s.SeasonId == 0) Db.RateSeasons.Add(s);
        await Db.SaveChangesAsync();
        Flash($"บันทึกช่วงราคา \"{s.SeasonName}\" แล้ว · การจองใหม่ในช่วงนี้จะคิดราคา {(s.AdjustPercent > 0 ? "+" : "")}{s.AdjustPercent:0.##}% (การจองเดิมไม่เปลี่ยน)");
        return RedirectToAction(nameof(Index), null, "seasons");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleSeason(int id)
    {
        var s = await Db.RateSeasons.FindAsync(id);
        if (s == null) { Flash("ไม่พบช่วงราคาที่เลือก", true); return RedirectToAction(nameof(Index), null, "seasons"); }
        s.IsActive = !s.IsActive;
        await Db.SaveChangesAsync();
        Flash(s.IsActive ? $"เปิดใช้ช่วงราคา \"{s.SeasonName}\" แล้ว" : $"ปิดช่วงราคา \"{s.SeasonName}\" แล้ว");
        return RedirectToAction(nameof(Index), null, "seasons");
    }

    // Part 47: สร้างช่วงราคาเทศกาลอัตโนมัติทันที (ปกติระบบทำให้วันละครั้ง)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateFestivals()
    {
        var n = await FestivalPricing.EnsureAsync(Db, DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today));
        Flash(n > 0 ? $"เพิ่มช่วงราคาเทศกาล {n} ช่วงแล้ว · แก้ % หรือปิดเทศกาลที่ไม่ต้องการได้ในตารางด้านล่าง" : "ช่วงราคาเทศกาลของปีนี้และปีหน้าครบแล้ว");
        return RedirectToAction(nameof(Index), null, "seasons");
    }

    // ลบได้เสมอ: การจองเก็บราคาที่ตกลงไว้แล้วใน booking_rooms ไม่อ้างอิงตารางนี้
    // ช่วงเทศกาลอัตโนมัติ: ปิดใช้แทนการลบ (ถ้าลบ ระบบจะสร้างกลับมาในวันถัดไป)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSeason(int id)
    {
        var auto = await Db.RateSeasons.FirstOrDefaultAsync(s => s.SeasonId == id && s.AutoKey != null);
        if (auto != null)
        {
            auto.IsActive = false;
            await Db.SaveChangesAsync();
            Flash($"\"{auto.SeasonName}\" เป็นเทศกาลที่ระบบสร้างอัตโนมัติ จึงปิดใช้แทนการลบ (ระบบจะไม่สร้างซ้ำ)");
            return RedirectToAction(nameof(Index), null, "seasons");
        }
        var n = await Db.RateSeasons.Where(s => s.SeasonId == id).ExecuteDeleteAsync();
        Flash(n == 0 ? "ไม่พบช่วงราคาที่เลือก" : "ลบช่วงราคาแล้ว (การจองเดิมไม่เปลี่ยน)", n == 0);
        return RedirectToAction(nameof(Index), null, "seasons");
    }

}

// ======================================================================
// AdminServicesController
// ======================================================================
/// <summary>ผู้จัดการจัดการบริการเสริม (รวมอาหาร/เตียงเสริม ฯลฯ ใช้ตาราง extra_services ตารางเดียว)</summary>
public class AdminServicesController : AdminAreaController
{
    public AdminServicesController(Csi402dbContext db) : base(db) { }

    [HttpGet]
    public async Task<IActionResult> Index() => View(await Db.ExtraServices.AsNoTracking().OrderBy(s => s.ServiceName)
        .Select(s => new ServiceEditViewModel
        {
            ServiceId = s.ServiceId, ServiceName = s.ServiceName, Price = s.Price, IsActive = s.IsActive == true,
            Category = s.Category, Description = s.Description, ImageUrl = s.ImageUrl, TimesBooked = s.BookingServices.Count
        }).ToListAsync());

    /// <summary>เพิ่ม/แก้บริการ พร้อมหมวด คำอธิบาย และรูป (รูปใหม่แทนรูปเดิม · ติ๊ก "ลบรูป" เพื่อเอาออก)</summary>
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<IActionResult> Save(ServiceEditViewModel f)
    {
        if (!string.IsNullOrEmpty(f.Category) && !ServiceCategories.IsValid(f.Category)) ModelState.AddModelError(nameof(f.Category), "หมวดบริการไม่ถูกต้อง");
        if (!ModelState.IsValid) { FlashModelErrors(); return RedirectToAction(nameof(Index)); }

        ExtraService? s;
        if (f.ServiceId == 0) { s = new ExtraService(); Db.ExtraServices.Add(s); }
        else if ((s = await Db.ExtraServices.FindAsync(f.ServiceId)) == null) return Stale(nameof(Index));

        string? newImage = null;
        if (f.Image is { Length: > 0 })
        {
            var (name, error) = await UploadStore.SaveServiceImageAsync(f.Image);
            if (error != null) { Flash(error, true); return RedirectToAction(nameof(Index)); }
            newImage = "/images/services/" + name;
        }
        var oldImage = s.ImageUrl;
        s.ServiceName = f.ServiceName.Trim(); s.Price = f.Price; s.IsActive = f.IsActive;
        s.Category = string.IsNullOrEmpty(f.Category) ? null : f.Category;
        s.Description = string.IsNullOrWhiteSpace(f.Description) ? null : f.Description.Trim();
        if (newImage != null) s.ImageUrl = newImage;
        else if (f.RemoveImage) s.ImageUrl = null;
        try { await Db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            UploadStore.DeleteServiceImage(newImage);
            Flash("บันทึกบริการเสริมไม่สำเร็จ (ชื่อบริการอาจซ้ำกับรายการเดิม)", true);
            return RedirectToAction(nameof(Index));
        }
        if (s.ImageUrl != oldImage) UploadStore.DeleteServiceImage(oldImage);
        Flash($"บันทึกบริการ \"{s.ServiceName}\" แล้ว");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var s = await Db.ExtraServices.FirstOrDefaultAsync(x => x.ServiceId == id);
        if (s == null) return Stale();
        if (await Db.BookingServices.AnyAsync(b => b.ServiceId == id))
        {
            Flash($"ลบ \"{s.ServiceName}\" ไม่ได้ เพราะมีการจองที่ใช้บริการนี้แล้ว (ให้ปิดการใช้งานแทน)", true);
            return RedirectToAction(nameof(Index));
        }
        Db.ExtraServices.Remove(s);
        await Db.SaveChangesAsync();
        UploadStore.DeleteServiceImage(s.ImageUrl);
        Flash($"ลบ \"{s.ServiceName}\" แล้ว");
        return RedirectToAction(nameof(Index));
    }
}

// ======================================================================
// AdminPromotionsController
// ======================================================================
/// <summary>ผู้จัดการปรับโปรโมชั่น (เปอร์เซ็นต์, เปิด/ปิด, ช่วงวันที่, จองล่วงหน้ากี่วัน) ประเภทโปรมี 5 แบบตายตัวตามกติการาคา</summary>
public class AdminPromotionsController : AdminAreaController
{
    public AdminPromotionsController(Csi402dbContext db) : base(db) { }

    [HttpGet]
    public async Task<IActionResult> Index() => View(await Db.Promotions.AsNoTracking()
        .OrderByDescending(p => p.IsActive).ThenBy(p => p.PromoId)
        .Select(p => new PromoAdminRowViewModel
        {
            PromoId = p.PromoId, PromoName = p.PromoName, PromoType = p.PromoType, DiscountPercent = p.DiscountPercent,
            MinAdvanceDays = p.MinAdvanceDays, StartDate = p.StartDate, EndDate = p.EndDate, IsActive = p.IsActive == true,
            PromoCode = p.PromoCode, Description = p.Description, MinNights = p.MinNights, MaxUses = p.MaxUses,
            MaxDiscount = p.MaxDiscount, NeedStaffConfirm = p.NeedStaffConfirm, MaxPerUser = p.MaxPerUser, RecipientsOnly = p.RecipientsOnly,
            Claimable = p.Claimable, ClaimFrom = p.ClaimFrom, ClaimUntil = p.ClaimUntil, Featured = p.Featured, AutoKey = p.AutoKey,
            Recipients = p.Recipients.Count(),
            Used = p.Bookings.Count(b => b.Status != "CANCELLED"),
            DiscountGiven = p.Bookings.Where(b => b.Status != "CANCELLED").Sum(b => (decimal?)b.DiscountTotal) ?? 0,
            Revenue = p.Bookings.Where(b => b.Status != "CANCELLED").Sum(b => (decimal?)b.NetTotal) ?? 0,
            HasBookings = p.Bookings.Any()
        }).ToListAsync());

    /// <summary>
    /// สร้าง (PromoId = 0) หรือแก้โปรโมชั่น — ประเภทกำหนดตอนสร้างเท่านั้น
    /// กติกาตามประเภท: ช่วงเทศกาล/วันแม่ ต้องมีวันเริ่ม-สิ้นสุด · รหัสโปรโมชั่นต้องมีรหัสไม่ซ้ำ · พักยาวต้องกำหนดคืนขั้นต่ำ ≥ 2
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(PromoEditViewModel f)
    {
        Promotion? p;
        if (f.PromoId == 0)
        {
            if (!PromoCatalog.Types.Contains(f.PromoType)) ModelState.AddModelError(nameof(f.PromoType), "กรุณาเลือกประเภทโปรโมชั่น");
            p = new Promotion { PromoType = f.PromoType ?? "" };
        }
        else if ((p = await Db.Promotions.FindAsync(f.PromoId)) == null) return Stale(nameof(Index));

        var name = f.PromoName?.Trim();
        if (string.IsNullOrEmpty(name)) ModelState.AddModelError(nameof(f.PromoName), "กรุณาตั้งชื่อโปรโมชั่น");
        var code = PricingService.NormalizeCode(f.PromoCode);
        switch (p.PromoType)
        {
            case "SEASONAL" or "MOTHERS_DAY" when f.StartDate == null || f.EndDate == null:
                ModelState.AddModelError("", "โปรช่วงเทศกาลต้องกำหนดวันเริ่มและวันสิ้นสุดการเข้าพัก"); break;
            case "CODE" when code == null:
                ModelState.AddModelError(nameof(f.PromoCode), "โปรแบบรหัสต้องกำหนดรหัสโปรโมชั่น"); break;
            case "LONG_STAY" when (f.MinNights ?? 0) < 2:
                ModelState.AddModelError(nameof(f.MinNights), "โปรพักยาวต้องกำหนดจำนวนคืนขั้นต่ำอย่างน้อย 2 คืน"); break;
        }
        if (p.PromoType != "CODE") code = null;
        if (code != null && await Db.Promotions.AnyAsync(x => x.PromoCode == code && x.PromoId != f.PromoId))
            ModelState.AddModelError(nameof(f.PromoCode), $"รหัส {code} ถูกใช้กับโปรอื่นแล้ว");
        if (!ModelState.IsValid) { FlashModelErrors(); return RedirectToAction(nameof(Index)); }

        p.PromoName = name!; p.DiscountPercent = f.DiscountPercent; p.IsActive = f.IsActive;
        p.MinAdvanceDays = p.PromoType == "EARLY_BIRD" ? f.MinAdvanceDays ?? 30 : null;
        p.StartDate = f.StartDate; p.EndDate = f.EndDate;
        p.PromoCode = code; p.Description = string.IsNullOrWhiteSpace(f.Description) ? null : f.Description.Trim();
        p.MinNights = f.MinNights; p.MaxUses = f.MaxUses; p.MaxDiscount = f.MaxDiscount;
        p.NeedStaffConfirm = f.NeedStaffConfirm;
        p.MaxPerUser = f.MaxPerUser;
        p.RecipientsOnly = p.PromoType == "CODE" && f.RecipientsOnly && !f.Claimable;
        // Part 49: คูปองกดเก็บใช้ได้กับโปรที่ไม่ผูกกับข้อมูลลูกค้า (วันเกิด/ครบรอบ/สมาชิก ใช้อัตโนมัติอยู่แล้ว)
        p.Claimable = f.Claimable && p.PromoType is not ("MEMBER" or "BIRTHDAY" or "ANNIVERSARY");
        p.ClaimFrom = p.Claimable ? f.ClaimFrom : null; p.ClaimUntil = p.Claimable ? f.ClaimUntil : null;
        p.Featured = f.Featured;
        if (f.PromoId == 0) Db.Promotions.Add(p);
        await Db.SaveChangesAsync();
        Flash($"บันทึกโปรโมชั่น \"{p.PromoName}\" แล้ว");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Part 49: สร้างแคมเปญโปรเทศกาลไทยทันที (ปกติระบบทำให้วันละครั้ง) — เพิ่มอย่างเดียว ไม่แก้ของเดิม</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateCampaigns()
    {
        var n = await PromoCampaigns.EnsureAsync(Db, DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today));
        Flash(n > 0 ? $"เพิ่มแคมเปญโปรเทศกาล {n} รายการแล้ว · แก้ % / ช่วงเวลา หรือปิดได้ที่การ์ดของแต่ละโปร" : "แคมเปญโปรเทศกาลของปีนี้และปีหน้าครบแล้ว");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// ส่งรหัสโปรโมชั่นให้ลูกค้าเป็นการแจ้งเตือนในระบบ (มีปุ่ม "ใช้รหัสนี้" พาไปหน้าจองพร้อมเติมรหัส)
    /// target: ALL = ลูกค้าทุกคน · STAYED = ลูกค้าที่เคยเข้าพัก · EMAILS = ระบุอีเมล (คั่นด้วยจุลภาค/ขึ้นบรรทัดใหม่)
    /// ส่งซ้ำคนเดิมไม่ได้รับซ้ำ · ถ้าโปรตั้ง "เฉพาะผู้ได้รับรหัส" รายชื่อนี้คือคนที่ใช้รหัสได้
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SendCode(int id, string? target, string? emails)
    {
        var p = await Db.Promotions.FirstOrDefaultAsync(x => x.PromoId == id);
        if (p == null) return Stale(nameof(Index));
        if (p.PromoType != "CODE" || p.PromoCode == null) { Flash("ส่งได้เฉพาะโปรแบบรหัสโปรโมชั่น", true); return RedirectToAction(nameof(Index)); }
        if (p.IsActive != true) { Flash("เปิดใช้งานโปรนี้ก่อนส่งรหัสให้ลูกค้า", true); return RedirectToAction(nameof(Index)); }
        if (p.EndDate is { } end && end < DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today)) { Flash("โปรนี้หมดช่วงแล้ว", true); return RedirectToAction(nameof(Index)); }

        var customers = Db.Users.Where(u => u.Role.RoleName == RoleNames.Customer);
        List<int> ids;
        string[] notFound = Array.Empty<string>();
        switch (target)
        {
            case "ALL": ids = await customers.Select(u => u.UserId).ToListAsync(); break;
            case "STAYED": ids = await customers.Where(u => Db.Bookings.Any(b => b.UserId == u.UserId && b.Status == "CHECKED_OUT")).Select(u => u.UserId).ToListAsync(); break;
            case "EMAILS":
                var list = (emails ?? "").Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim().ToLowerInvariant()).Where(e => e.Length <= 100).Distinct().Take(500).ToList();
                if (list.Count == 0) { Flash("กรุณาระบุอีเมลลูกค้าอย่างน้อย 1 รายการ", true); return RedirectToAction(nameof(Index)); }
                var found = await customers.Where(u => list.Contains(u.Email)).Select(u => new { u.UserId, u.Email }).ToListAsync();
                ids = found.Select(f => f.UserId).ToList();
                notFound = list.Except(found.Select(f => f.Email)).ToArray();
                break;
            default: Flash("กรุณาเลือกกลุ่มลูกค้าที่จะส่ง", true); return RedirectToAction(nameof(Index));
        }
        var already = await Db.PromoRecipients.Where(r => r.PromoId == id).Select(r => r.UserId).ToListAsync();
        var fresh = ids.Except(already).ToList();
        var by = User.FindFirstValue(ClaimTypes.Email);
        var cond = ThaiLabels.PromoCondition(p);
        foreach (var uid in fresh)
        {
            Db.PromoRecipients.Add(new PromoRecipient { PromoId = id, UserId = uid, SentAt = pameka_7033_l005.Helpers.AppClock.Now, SentBy = by });
            Notify.Add(Db, uid, $"คุณได้รับรหัสโปรโมชั่น {p.PromoCode} ลด {p.DiscountPercent:0.##}%",
                $"{p.PromoName}" + (cond != "" ? $" · {cond}" : "") + " — กดเพื่อจองห้องพร้อมใช้รหัส", "/Booking/Create?code=" + Uri.EscapeDataString(p.PromoCode));
        }
        await Db.SaveChangesAsync();
        var msg = $"ส่งรหัส {p.PromoCode} ให้ลูกค้าใหม่ {fresh.Count} คน" + (ids.Count > fresh.Count ? $" (ข้าม {ids.Count - fresh.Count} คนที่เคยได้รับแล้ว)" : "");
        if (notFound.Length > 0) msg += $" · ไม่พบบัญชีลูกค้า: {string.Join(", ", notFound.Take(5))}{(notFound.Length > 5 ? " …" : "")}";
        Flash(msg, fresh.Count == 0);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>ลบได้เฉพาะโปรที่ไม่เคยถูกใช้กับการจองใดเลย (มีประวัติแล้ว → ปิดการใช้งานแทน เพื่อเก็บหลักฐานส่วนลด)</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var p = await Db.Promotions.FirstOrDefaultAsync(x => x.PromoId == id);
        if (p == null) return Stale(nameof(Index));
        if (await Db.Bookings.AnyAsync(b => b.PromoId == id))
        {
            Flash($"\"{p.PromoName}\" มีการจองใช้แล้ว ลบไม่ได้ ให้ปิดการใช้งานแทน", true);
            return RedirectToAction(nameof(Index));
        }
        Db.Promotions.Remove(p);
        await Db.SaveChangesAsync();
        Flash($"ลบโปรโมชั่น \"{p.PromoName}\" แล้ว");
        return RedirectToAction(nameof(Index));
    }
}

// ======================================================================
// AdminReportsController
// ======================================================================
/// <summary>รายงานสรุปของผู้จัดการ: รายรับ/คืนเงินรายวัน, สถานะการจอง, บริการเสริมขายดี, จำนวนคืนที่ขายห้อง</summary>
public class AdminReportsController : AdminAreaController
{
    private static readonly List<string> SoldStatuses = new() { "CONFIRMED", "CHECKED_IN", "CHECKED_OUT" };
    public AdminReportsController(Csi402dbContext db) : base(db) { }

    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to) => View(await BuildAsync(from, to));

    /// <summary>Part 53: สถิติฝ่ายส่งของ — จำนวนงาน เวลาเฉลี่ย งานล่าช้า แยกตามพนักงาน</summary>
    [HttpGet]
    public async Task<IActionResult> Delivery(DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(AppClock.Today);
        var t = to ?? today; var f = from ?? t.AddDays(-29);
        if (f > t) (f, t) = (t, f);
        if (t.DayNumber - f.DayNumber > 366) f = t.AddDays(-366);
        var start = f.ToDateTime(TimeOnly.MinValue); var end = t.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var now = AppClock.Now; var late = RoomRequests.LateMinutes;

        var svc = await Db.DeliveryTasks.AsNoTracking().Where(x => x.Status == "DONE" && x.DoneAt >= start && x.DoneAt < end && x.AssignedTo != null)
            .Select(x => new { Who = x.AssignedToNavigation!.FirstName + " " + x.AssignedToNavigation.LastName, x.CreatedAt, x.PickedAt, x.DoneAt, Req = false, Item = (string?)null }).ToListAsync();
        var req = await Db.ItemRequests.AsNoTracking().Where(x => x.Status == "DONE" && x.DoneAt >= start && x.DoneAt < end && x.AssignedTo != null)
            .Select(x => new { Who = x.AssignedToNavigation!.FirstName + " " + x.AssignedToNavigation.LastName, CreatedAt = (DateTime?)x.CreatedAt, x.PickedAt, x.DoneAt, Req = true, Item = (string?)x.Item }).ToListAsync();
        var all = svc.Concat(req).ToList();
        static double? Avg(IEnumerable<double> v) { var l = v.ToList(); return l.Count == 0 ? null : Math.Round(l.Average(), 1); }

        var openS = await Db.DeliveryTasks.AsNoTracking().Where(x => x.Status != "DONE").Select(x => new { x.CreatedAt, x.Urgent }).ToListAsync();
        var openR = await Db.ItemRequests.AsNoTracking().Where(x => x.Status == "PENDING" || x.Status == "DELIVERING").Select(x => new { CreatedAt = (DateTime?)x.CreatedAt, x.Urgent }).ToListAsync();
        var open = openS.Concat(openR).ToList();

        return View(new DeliveryStatsViewModel
        {
            From = f, To = t, OpenNow = open.Count, UrgentNow = open.Count(o => o.Urgent),
            LateNow = open.Count(o => o.CreatedAt is { } c && (now - c).TotalMinutes > late),
            Staff = all.GroupBy(x => x.Who).Select(g => new DeliveryStaffStat
            {
                Name = g.Key, Done = g.Count(), Requests = g.Count(x => x.Req),
                AvgWait = Avg(g.Where(x => x.CreatedAt != null && x.PickedAt != null).Select(x => (x.PickedAt!.Value - x.CreatedAt!.Value).TotalMinutes)),
                AvgTotal = Avg(g.Where(x => x.CreatedAt != null).Select(x => (x.DoneAt!.Value - x.CreatedAt!.Value).TotalMinutes)),
                Late = g.Count(x => x.CreatedAt != null && (x.DoneAt!.Value - x.CreatedAt!.Value).TotalMinutes > late)
            }).OrderByDescending(x => x.Done).ToList(),
            TopItems = req.GroupBy(x => x.Item).Select(g => (RoomRequests.ItemName(g.Key), g.Count())).OrderByDescending(x => x.Item2).Take(8).ToList()
        });
    }

    /// <summary>ส่งออกรายงานช่วงวันที่เดียวกับหน้าจอเป็น CSV (UTF-8 มี BOM เพื่อให้ Excel อ่านภาษาไทยถูก)</summary>
    [HttpGet]
    public async Task<IActionResult> Export(DateOnly? from, DateOnly? to)
    {
        var r = await BuildAsync(from, to);
        if (r.Error != null) { Flash(r.Error, true); return RedirectToAction(nameof(Index)); }

        static string C(object? v) => "\"" + CsvSafe.Guard(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)).Replace("\"", "\"\"") + "\"";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{C("รายงาน AurelleStay")},{C(r.From.ToString("yyyy-MM-dd"))},{C(r.To.ToString("yyyy-MM-dd"))}");
        sb.AppendLine($"{C("รายรับ (ชำระแล้ว ยังไม่คืน)")},{C(r.Revenue)}");
        sb.AppendLine($"{C("คืนเงินแล้ว")},{C(r.Refunded)}");
        sb.AppendLine($"{C("รายจ่าย")},{C(r.Expenses)}");
        sb.AppendLine($"{C("กำไรสุทธิ (รายรับ - รายจ่าย)")},{C(r.Profit)}");
        sb.AppendLine($"{C("การจองใหม่")},{C(r.NewBookings)}");
        sb.AppendLine($"{C("จำนวนคืนที่ขายห้อง")},{C(r.RoomNights)}");
        sb.AppendLine($"{C("อัตราการเข้าพักเฉลี่ย (%)")},{C(r.OccupancyPercent)}");
        sb.AppendLine($"{C("ADR ราคาห้องเฉลี่ย/คืน")},{C(r.Adr)}");
        sb.AppendLine($"{C("RevPAR รายได้ห้อง/ห้องที่มี/คืน")},{C(r.RevPar)}");
        sb.AppendLine();
        sb.AppendLine($"{C("วันที่")},{C("รายรับ")}");
        foreach (var d in r.Daily) sb.AppendLine($"{C(d.Date.ToString("yyyy-MM-dd"))},{C(d.Amount)}");
        sb.AppendLine();
        sb.AppendLine($"{C("คืนวันที่")},{C("ห้องที่มีผู้พัก")},{C("ห้องทั้งหมด")},{C("อัตราการเข้าพัก (%)")}");
        foreach (var o in r.Occupancy) sb.AppendLine($"{C(o.Date.ToString("yyyy-MM-dd"))},{C(o.Occupied)},{C(r.TotalRooms)},{C(r.TotalRooms == 0 ? 0 : Math.Round(100m * o.Occupied / r.TotalRooms, 1))}");
        sb.AppendLine();
        sb.AppendLine($"{C("สถานะการจอง")},{C("จำนวน")}");
        foreach (var s in r.StatusCounts) sb.AppendLine($"{C(ThaiLabels.BookingStatus(s.Status))},{C(s.Count)}");
        sb.AppendLine();
        sb.AppendLine($"{C("บริการเสริม")},{C("จำนวน")},{C("ยอดขาย")}");
        foreach (var s in r.Services) sb.AppendLine($"{C(s.Name)},{C(s.Qty)},{C(s.Total)}");

        var bytes = new System.Text.UTF8Encoding(true).GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"report_{r.From:yyyyMMdd}_{r.To:yyyyMMdd}.csv");
    }

    private async Task<ReportViewModel> BuildAsync(DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today);
        var vm = new ReportViewModel { From = from ?? today.AddDays(-29), To = to ?? today };
        if (vm.From.Year is < 2000 or > 2100 || vm.To.Year is < 2000 or > 2100) { vm.Error = "เลือกวันที่ระหว่างปี ค.ศ. 2000 – 2100"; return vm; }
        if (vm.To < vm.From) { vm.Error = "วันสิ้นสุดต้องไม่ก่อนวันเริ่มต้น"; return vm; }
        if (vm.To.DayNumber - vm.From.DayNumber > 366) { vm.Error = "เลือกช่วงวันที่ได้ไม่เกิน 1 ปี"; return vm; }

        var start = vm.From.ToDateTime(TimeOnly.MinValue);
        var end = vm.To.AddDays(1).ToDateTime(TimeOnly.MinValue);

        // ดึงมาคำนวณในหน่วยความจำ (ช่วงวันที่จำกัด ≤ 1 ปี) เพื่อไม่พึ่งการ SUM/GROUP ทศนิยมของฐานข้อมูล
        var paid = await Db.Payments.AsNoTracking()
            .Where(p => p.PaymentType == BookingWorkflow.PaymentType && p.Status == "PAID" && p.PaidAt >= start && p.PaidAt < end)
            .Select(p => new { p.PaidAt, p.Amount }).ToListAsync();
        var refunds = await Db.Payments.AsNoTracking()
            .Where(p => p.PaymentType == BookingWorkflow.RefundType && p.Status == "REFUNDED" && p.PaidAt >= start && p.PaidAt < end)
            .Select(p => p.Amount).ToListAsync();
        var statuses = await Db.Bookings.AsNoTracking().Where(b => b.BookedAt >= start && b.BookedAt < end).Select(b => b.Status).ToListAsync();
        var services = await Db.BookingServices.AsNoTracking()
            .Where(s => s.Booking.BookedAt >= start && s.Booking.BookedAt < end && SoldStatuses.Contains(s.Booking.Status))
            .Select(s => new { Name = s.Service.ServiceName, s.Quantity, s.UnitPrice }).ToListAsync();
        var nights = await Db.BookingRooms.AsNoTracking()
            .Where(r => r.Booking.BookedAt >= start && r.Booking.BookedAt < end && SoldStatuses.Contains(r.Booking.Status))
            .Select(r => (int)r.Nights).ToListAsync();

        vm.Revenue = paid.Sum(p => p.Amount);
        vm.Refunded = refunds.Sum();
        var (fromD, toD) = (vm.From, vm.To);
        var expenses = await Db.Expenses.AsNoTracking().Where(e => e.SpentOn >= fromD && e.SpentOn <= toD)
            .Select(e => new { e.Category, e.Amount }).ToListAsync();
        vm.Expenses = expenses.Sum(e => e.Amount);
        vm.ExpenseByCategory = expenses.GroupBy(e => e.Category).Select(g => (g.Key, g.Sum(x => x.Amount))).OrderByDescending(x => x.Item2).ToList();
        vm.NewBookings = statuses.Count;
        vm.RoomNights = nights.Sum();
        vm.Daily = paid.GroupBy(p => DateOnly.FromDateTime(p.PaidAt!.Value)).OrderBy(g => g.Key).Select(g => (g.Key, g.Sum(x => x.Amount))).ToList();
        vm.StatusCounts = statuses.GroupBy(s => s).OrderBy(g => g.Key).Select(g => (g.Key, g.Count())).ToList();
        vm.Services = services.GroupBy(s => s.Name).Select(g => (g.Key, g.Sum(x => x.Quantity), g.Sum(x => x.Quantity * x.UnitPrice)))
            .OrderByDescending(x => x.Item3).ToList();

        // อัตราการเข้าพักรายคืน: ห้องที่ขายแล้ว (ยืนยัน/เข้าพัก/ออกแล้ว) ที่มีผู้พักในคืนนั้น เทียบกับจำนวนห้องทั้งหมด
        var lastNight = vm.To.AddDays(1);
        vm.TotalRooms = await Db.Rooms.CountAsync();
        var stays = await Db.BookingRooms.AsNoTracking()
            .Where(x => SoldStatuses.Contains(x.Booking.Status) && x.Booking.CheckIn < lastNight && x.Booking.CheckOut > vm.From)
            .Select(x => new { x.Booking.CheckIn, x.Booking.CheckOut, x.PricePerNight }).ToListAsync();
        decimal roomRevenue = 0; var soldNights = 0;
        for (var d = vm.From; d <= vm.To; d = d.AddDays(1))
        {
            var night = stays.Where(x => x.CheckIn <= d && x.CheckOut > d).ToList();
            vm.Occupancy.Add((d, night.Count));
            soldNights += night.Count; roomRevenue += night.Sum(x => x.PricePerNight);
        }
        var capacity = vm.TotalRooms * vm.Occupancy.Count;
        vm.OccupancyPercent = capacity == 0 ? 0 : Math.Round(100m * soldNights / capacity, 1);
        vm.Adr = soldNights == 0 ? 0 : Math.Round(roomRevenue / soldNights, 2);
        vm.RevPar = capacity == 0 ? 0 : Math.Round(roomRevenue / capacity, 2);
        return vm;
    }
}

// ======================================================================
// AdminAuditController
// ======================================================================
/// <summary>บันทึกกิจกรรม (อ่านอย่างเดียว): ใครทำอะไร กับรายการไหน สำเร็จหรือไม่ — ค้นหา/กรองตามกลุ่มและช่วงวันที่</summary>
public class AdminAuditController : AdminAreaController
{
    public AdminAuditController(Csi402dbContext db) : base(db) { }
    private const int PageSize = 50;

    // ---------------------------------------------------------------- Part 50: สถานะระบบ + สำรองข้อมูล
    /// <summary>สถานะระบบ: ฐานข้อมูล ขนาด จำนวนข้อมูลแต่ละตาราง เวลาเซิร์ฟเวอร์ พื้นที่ดิสก์ งานเบื้องหลัง</summary>
    [HttpGet]
    public async Task<IActionResult> SystemStatus([FromServices] IWebHostEnvironment env, [FromServices] IConfiguration cfg)
    {
        var vm = new SystemStatusViewModel
        {
            ServerTime = pameka_7033_l005.Helpers.AppClock.Now,
            Environment = env.EnvironmentName,
            AppVersion = typeof(AdminAuditController).Assembly.GetName().Version?.ToString() ?? "-",
            Runtime = global::System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Os = global::System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Uptime = DateTime.Now - global::System.Diagnostics.Process.GetCurrentProcess().StartTime
        };
        try
        {
            var conn = Db.Database.GetDbConnection();
            if (conn.State != global::System.Data.ConnectionState.Open) await conn.OpenAsync();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT VERSION(), DATABASE()";
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync()) { vm.DbVersion = r.GetString(0); vm.DbName = r.IsDBNull(1) ? "-" : r.GetString(1); }
            }
            await using (var cmd = conn.CreateCommand())
            {
                // จำนวนแถวจริง (COUNT) ทีละตาราง — ตารางในระบบมีไม่มาก จึงนับตรงได้
                cmd.CommandText = "SELECT TABLE_NAME, DATA_LENGTH + INDEX_LENGTH FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME";
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) vm.Tables.Add((r.GetString(0), 0, r.IsDBNull(1) ? 0 : Convert.ToInt64(r.GetValue(1))));
            }
            for (var i = 0; i < vm.Tables.Count; i++)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM `{vm.Tables[i].Name.Replace("`", "")}`";
                vm.Tables[i] = (vm.Tables[i].Name, Convert.ToInt64(await cmd.ExecuteScalarAsync()), vm.Tables[i].Bytes);
            }
            vm.DbOk = true;
        }
        catch (Exception ex) { vm.DbError = ex.Message; }
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(env.ContentRootPath) ?? "/");
            vm.DiskFree = drive.AvailableFreeSpace; vm.DiskTotal = drive.TotalSize;
        }
        catch { /* บางระบบอ่านพื้นที่ดิสก์ไม่ได้ */ }
        var slips = Path.Combine(env.ContentRootPath, "App_Data", "slips");
        if (Directory.Exists(slips))
        {
            var files = new DirectoryInfo(slips).GetFiles("*", SearchOption.AllDirectories);
            vm.SlipFiles = files.Length; vm.SlipBytes = files.Sum(f => f.Length);
        }
        vm.DemoUsers = await DemoData.CountAsync(Db);
        var mail = MailSettings.Load(cfg);
        vm.MailEnabled = mail.Enabled; vm.MailServer = mail.Enabled ? $"{mail.Host}:{mail.Port}" : null; vm.MailFrom = mail.From;
        if (vm.DbOk)
        {
            vm.OutboxCounts = await Db.EmailOutbox.GroupBy(m => m.Status).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
            vm.Outbox = await Db.EmailOutbox.AsNoTracking().OrderByDescending(m => m.EmailId).Take(15).ToListAsync();
            vm.Waiting = await Db.Waitlist.CountAsync(w => w.Status == "WAITING");
        }
        vm.LastBackup = await Db.AuditLogs.AsNoTracking().Where(a => a.Action == "AdminAudit.Backup" && a.Success)
            .OrderByDescending(a => a.CreatedAt).Select(a => (DateTime?)a.CreatedAt).FirstOrDefaultAsync();
        return View(vm);
    }

    /// <summary>Part 51: ส่งอีเมลทดสอบถึงตัวเอง แล้วสั่งตัวส่งทำงานทันที (ไม่ต้องรอรอบ 15 วินาที)</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TestEmail([FromServices] IConfiguration cfg)
    {
        var uid = CurrentUserId ?? 0;
        var me = await Db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == uid);
        if (me == null) return Forbid();
        Mailer.Queue(Db, me.Email, "ทดสอบการส่งอีเมล — AurelleStay", $"ถ้าคุณได้รับอีเมลนี้ แปลว่าระบบส่งอีเมลของโรงแรมทำงานปกติ\nส่งเมื่อ {AppClock.Now:dd/MM/yyyy HH:mm}", "/AdminAudit/SystemStatus", uid);
        await Db.SaveChangesAsync();
        await OutboxSender.RunOnceAsync(Db, MailSettings.Load(cfg));
        var last = await Db.EmailOutbox.AsNoTracking().OrderByDescending(m => m.EmailId).FirstAsync(m => m.UserId == uid);
        if (last.Status == "FAILED") TempData["StatusError"] = true;
        TempData["Status"] = last.Status switch
        {
            "SENT" => $"ส่งอีเมลทดสอบถึง {me.Email} แล้ว",
            "FAILED" => "ส่งอีเมลไม่สำเร็จ: " + last.LastError,
            _ => "ยังไม่ได้ตั้งค่า SMTP (หรือเป็นอีเมลภายใน .local) — อีเมลถูกเก็บไว้ในกล่องจดหมายด้านล่าง"
        };
        await AuditTrail.WriteAsync(HttpContext, "AdminAudit.TestEmail", null, last.Status != "FAILED", Mailer.StatusLabel(last.Status));
        return RedirectToAction(nameof(SystemStatus));
    }

    /// <summary>
    /// ดาวน์โหลดไฟล์สำรองฐานข้อมูลทั้งหมด (.zip ที่มี backup.sql: โครงสร้างตาราง + ข้อมูล) — กู้คืนด้วย mysql csi402db &lt; backup.sql
    /// เป็น POST เพื่อให้ถูกบันทึกในบันทึกกิจกรรม และกันการดาวน์โหลดจากลิงก์ภายนอก (CSRF) · ไฟล์มีข้อมูลส่วนบุคคล เก็บให้ปลอดภัย
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Backup()
    {
        var conn = Db.Database.GetDbConnection();
        if (conn.State != global::System.Data.ConnectionState.Open) await conn.OpenAsync();
        var sql = new global::System.Text.StringBuilder();
        var now = pameka_7033_l005.Helpers.AppClock.Now;
        sql.Append($"-- AurelleStay database backup {now:yyyy-MM-dd HH:mm:ss} (Asia/Bangkok)\n-- กู้คืน: mysql -u root -p csi402db < backup.sql\n");
        sql.Append("SET NAMES utf8mb4;\nSET FOREIGN_KEY_CHECKS = 0;\nSET SQL_MODE = 'NO_AUTO_VALUE_ON_ZERO';\n\n");
        var tables = new List<string>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) tables.Add(r.GetString(0));
        }
        var totalRows = 0L;
        foreach (var t in tables)
        {
            var name = t.Replace("`", "");
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SHOW CREATE TABLE `{name}`";
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync()) sql.Append($"DROP TABLE IF EXISTS `{name}`;\n{r.GetString(1)};\n\n");
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SELECT * FROM `{name}`";
                await using var r = await cmd.ExecuteReaderAsync();
                var cols = string.Join(", ", Enumerable.Range(0, r.FieldCount).Select(i => $"`{r.GetName(i)}`"));
                var batch = 0;
                while (await r.ReadAsync())
                {
                    sql.Append(batch == 0 ? $"INSERT INTO `{name}` ({cols}) VALUES\n(" : ",\n(");
                    for (var i = 0; i < r.FieldCount; i++)
                    {
                        if (i > 0) sql.Append(", ");
                        sql.Append(SqlLiteral(r.IsDBNull(i) ? null : r.GetValue(i)));
                    }
                    sql.Append(')');
                    totalRows++;
                    if (++batch == 200) { sql.Append(";\n"); batch = 0; }
                }
                if (batch > 0) sql.Append(";\n");
                sql.Append('\n');
            }
        }
        sql.Append("SET FOREIGN_KEY_CHECKS = 1;\n");
        using var ms = new MemoryStream();
        using (var zip = new global::System.IO.Compression.ZipArchive(ms, global::System.IO.Compression.ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("backup.sql", global::System.IO.Compression.CompressionLevel.Optimal);
            await using (var w = new StreamWriter(entry.Open(), new global::System.Text.UTF8Encoding(false))) await w.WriteAsync(sql.ToString());
            var readme = zip.CreateEntry("อ่านก่อน.txt");
            await using (var w = new StreamWriter(readme.Open(), new global::System.Text.UTF8Encoding(true)))
                await w.WriteAsync($"ไฟล์สำรองฐานข้อมูล AurelleStay\r\nสร้างเมื่อ {now:dd/MM/yyyy HH:mm:ss} · {tables.Count} ตาราง · {totalRows:N0} แถว\r\n\r\nกู้คืน (Docker):  docker cp backup.sql aurellestay-db-1:/tmp/b.sql  แล้ว  docker exec aurellestay-db-1 sh -c \"mysql -uroot -p$MYSQL_ROOT_PASSWORD csi402db < /tmp/b.sql\"\r\nกู้คืน (MySQL ในเครื่อง):  cmd /c \"mysql -u root -p csi402db < backup.sql\"\r\n\r\nไฟล์นี้มีข้อมูลส่วนบุคคลของลูกค้า เก็บในที่ปลอดภัย ห้ามส่งต่อ\r\n");
        }
        Flash($"สร้างไฟล์สำรอง {tables.Count} ตาราง {totalRows:N0} แถวแล้ว");
        return File(ms.ToArray(), "application/zip", $"aurellestay-backup-{now:yyyyMMdd-HHmm}.zip");
    }

    /// <summary>Part 50: สร้างข้อมูลลูกค้าตัวอย่างสำหรับนำเสนอ (บัญชี @demo.aurellestay.local ลบออกได้ทั้งหมด)</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SeedDemo()
    {
        var msg = await DemoData.SeedAsync(Db, pameka_7033_l005.Helpers.AppClock.Now);
        Flash(msg, msg.StartsWith("มีข้อมูล") || msg.StartsWith("ยังไม่มี"));
        return RedirectToAction(nameof(SystemStatus));
    }

    /// <summary>Part 50: ลบเฉพาะข้อมูลลูกค้าตัวอย่าง (ไม่แตะข้อมูลจริง)</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveDemo()
    {
        Flash(await DemoData.RemoveAsync(Db));
        return RedirectToAction(nameof(SystemStatus));
    }

    /// <summary>ค่าในคำสั่ง INSERT ของ MySQL (escape ตามกฎของ MySQL)</summary>
    private static string SqlLiteral(object? v) => v switch
    {
        null => "NULL",
        bool b => b ? "1" : "0",
        byte[] bytes => bytes.Length == 0 ? "''" : "0x" + Convert.ToHexString(bytes),
        DateTime d => $"'{d:yyyy-MM-dd HH:mm:ss}'",
        DateOnly d => $"'{d:yyyy-MM-dd}'",
        TimeSpan t => $"'{t:hh\\:mm\\:ss}'",
        sbyte or byte or short or ushort or int or uint or long or ulong => Convert.ToString(v, global::System.Globalization.CultureInfo.InvariantCulture)!,
        float or double or decimal => Convert.ToString(v, global::System.Globalization.CultureInfo.InvariantCulture)!,
        _ => "'" + v.ToString()!.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\0", "\\0").Replace("\u001a", "\\Z") + "'"
    };


    private static readonly string[] Groups = { "staff", "customer", "failed", "login" };

    [HttpGet]
    public async Task<IActionResult> Index(string? q, string? group, DateOnly? from, DateOnly? to, int page = 1)
    {
        q = q?.Trim();
        if (group != null && !Groups.Contains(group)) group = null;
        var query = Filter(q, group, from, to);

        var total = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, pages);
        var rows = await query.OrderByDescending(a => a.AuditId).Skip((page - 1) * PageSize).Take(PageSize).ToListAsync();
        return View(new AuditPageViewModel { Q = q, Group = group, From = from, To = to, Page = page, TotalPages = pages, TotalCount = total, Rows = rows });
    }

    /// <summary>ส่งออกตามตัวกรองเดียวกับหน้าจอเป็น CSV (สูงสุด 10,000 รายการล่าสุด, UTF-8 มี BOM ให้ Excel อ่านไทยถูก)</summary>
    [HttpGet]
    public async Task<IActionResult> Export(string? q, string? group, DateOnly? from, DateOnly? to)
    {
        q = q?.Trim();
        if (group != null && !Groups.Contains(group)) group = null;
        var rows = await Filter(q, group, from, to).OrderByDescending(a => a.AuditId).Take(10_000).ToListAsync();
        // กันสูตร Excel: ค่าที่ขึ้นต้นด้วย = + - @ (เช่น อีเมลที่ผู้โจมตีพิมพ์ตอนล็อกอิน) ใส่ ' นำหน้า
        static string C(object? v)
        {
            var s = CsvSafe.Guard(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture));
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var sb = new System.Text.StringBuilder("เวลา,ผู้ทำ,บทบาท,การกระทำ,รหัสการกระทำ,รายการ,ผล,รายละเอียด,IP\r\n");
        foreach (var a in rows)
            sb.AppendJoin(',', C(a.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")), C(a.Actor), C(RoleNames.Label(a.ActorRole)), C(AuditTrail.Label(a.Action)),
                C(a.Action), C(a.TargetId), C(a.Success ? "สำเร็จ" : "ไม่สำเร็จ"), C(a.Detail), C(a.Ip)).Append("\r\n");
        var bytes = new System.Text.UTF8Encoding(true).GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"audit_{pameka_7033_l005.Helpers.AppClock.Now:yyyyMMdd_HHmm}.csv");
    }

    private IQueryable<AuditLog> Filter(string? q, string? group, DateOnly? from, DateOnly? to)
    {
        var query = Db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(q))
        {
            var id = int.TryParse(q.TrimStart('#'), out var n) ? n : -1;
            query = query.Where(a => a.Actor.Contains(q) || (a.Detail != null && a.Detail.Contains(q)) || a.TargetId == id || a.Ip == q);
        }
        query = group switch
        {
            "staff" => query.Where(a => a.ActorRole != RoleNames.Customer && a.Action != "Account.Login" && a.Action != "Account.Register"),
            "customer" => query.Where(a => a.ActorRole == RoleNames.Customer && a.Action != "Account.Login"),
            "failed" => query.Where(a => !a.Success),
            "login" => query.Where(a => a.Action == "Account.Login" || a.Action == "Account.Register"),
            _ => query
        };
        // จำกัดช่วงปีที่รับ: วันที่สุดขอบ (เช่น 9999-12-31) บวกวันแล้วจะเกินขอบเขตของ DateTime
        static DateOnly Clamp(DateOnly d) => d < new DateOnly(2000, 1, 1) ? new DateOnly(2000, 1, 1) : d > new DateOnly(2100, 12, 31) ? new DateOnly(2100, 12, 31) : d;
        if (from != null) { var f = Clamp(from.Value).ToDateTime(TimeOnly.MinValue); query = query.Where(a => a.CreatedAt >= f); }
        if (to != null) { var t = Clamp(to.Value).AddDays(1).ToDateTime(TimeOnly.MinValue); query = query.Where(a => a.CreatedAt < t); }
        return query;
    }
}

// ======================================================================
// AdminReviewsController
// ======================================================================
/// <summary>ผู้จัดการดูและลบรีวิวที่ไม่เหมาะสม (ลบเท่านั้น ไม่แก้ข้อความของลูกค้า)</summary>
public class AdminReviewsController : AdminAreaController
{
    public AdminReviewsController(Csi402dbContext db) : base(db) { }

    [HttpGet]
    public async Task<IActionResult> Index(int? rating, int page = 1)
    {
        const int PageSize = 25;
        var q = Db.Reviews.AsNoTracking().AsQueryable();
        if (rating is >= 1 and <= 5) q = q.Where(r => r.Rating == rating); else rating = null;

        var total = await q.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, pages);
        var rows = await q.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.ReviewId).Skip((page - 1) * PageSize).Take(PageSize)
            .Select(r => new { r.ReviewId, r.BookingId, Name = r.User.FirstName + " " + r.User.LastName, r.Rating, r.Comment, r.CreatedAt }).ToListAsync();

        return View(new AdminReviewsPageViewModel
        {
            Rating = rating, Page = page, TotalPages = pages, TotalCount = total,
            Items = rows.Select(r => new ReviewItemViewModel { Id = r.ReviewId, BookingId = r.BookingId, Name = r.Name, Rating = r.Rating, Comment = r.Comment, CreatedAt = r.CreatedAt }).ToList()
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var r = await Db.Reviews.FirstOrDefaultAsync(x => x.ReviewId == id);
        if (r == null) return Stale();
        Db.Reviews.Remove(r);
        await Db.SaveChangesAsync();
        Flash("ลบรีวิวแล้ว");
        return RedirectToAction(nameof(Index));
    }
}

// ======================================================================
// AdminMessagesController — ข้อความติดต่อจากหน้า "ติดต่อเรา" (Part 30)
// ======================================================================
public class AdminMessagesController : AdminAreaController
{
    public AdminMessagesController(Csi402dbContext db) : base(db) { }
    private static readonly string[] Statuses = { "NEW", "DONE", "ALL" };

    [HttpGet]
    public async Task<IActionResult> Index(string? status, string? q, int page = 1)
    {
        const int PageSize = 20;
        status = Statuses.Contains(status?.ToUpperInvariant()) ? status!.ToUpperInvariant() : "NEW";
        var query = Db.ContactMessages.AsNoTracking().AsQueryable();
        if (status != "ALL") query = query.Where(m => m.Status == status);
        q = q?.Trim();
        if (!string.IsNullOrEmpty(q)) query = query.Where(m => m.Name.Contains(q) || m.Email.Contains(q) || m.Message.Contains(q) || (m.Phone != null && m.Phone.Contains(q)));
        var total = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, pages);
        var items = await query.OrderBy(m => m.Status == "NEW" ? 0 : 1).ThenByDescending(m => m.CreatedAt).ThenByDescending(m => m.MessageId)
            .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync();
        var emails = items.Select(i => i.Email).Distinct().ToList();
        var accounts = await Db.Users.AsNoTracking().Where(u => emails.Contains(u.Email)).Select(u => new { u.Email, u.UserId }).ToListAsync();
        return View(new AdminMessagesPageViewModel
        {
            Status = status, Q = q, Page = page, TotalPages = pages, TotalCount = total, Items = items,
            NewCount = await Db.ContactMessages.CountAsync(m => m.Status == "NEW"),
            Accounts = accounts.GroupBy(a => a.Email, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().UserId, StringComparer.OrdinalIgnoreCase)
        });
    }

    /// <summary>ปิดเรื่อง (DONE) พร้อมบันทึกถึงลูกค้า หรือเปิดเรื่องกลับ (NEW) — ถ้าผู้ส่งมีบัญชีและมีบันทึก จะส่งการแจ้งเตือนในระบบให้</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(long id, string status, string? note)
    {
        var m = await Db.ContactMessages.FirstOrDefaultAsync(x => x.MessageId == id);
        if (m == null) return Stale();
        status = status?.ToUpperInvariant() ?? "";
        if (status is not ("NEW" or "DONE")) { Flash("สถานะไม่ถูกต้อง", true); return RedirectToAction(nameof(Index)); }
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note is { Length: > 500 }) { Flash("บันทึกยาวไม่เกิน 500 ตัวอักษร", true); return RedirectToAction(nameof(Index), new { status = m.Status }); }
        var wasNew = m.Status == "NEW";
        m.Status = status;
        if (note != null) m.StaffNote = note;
        m.HandledBy = User.FindFirstValue(System.Security.Claims.ClaimTypes.Email);
        m.HandledAt = status == "DONE" ? pameka_7033_l005.Helpers.AppClock.Now : null;
        if (status == "DONE" && wasNew && m.UserId is { } uid && note != null)
            Notify.Add(Db, uid, "โรงแรมตอบกลับข้อความของคุณ", note.Length > 480 ? note[..480] + "…" : note, null);
        await Db.SaveChangesAsync();
        Flash(status == "DONE" ? $"ปิดเรื่องข้อความ #{m.MessageId} แล้ว" : $"เปิดเรื่องข้อความ #{m.MessageId} อีกครั้ง");
        return RedirectToAction(nameof(Index));
    }
}

// ======================================================================
// AdminStaffController
// ======================================================================
/// <summary>ผู้จัดการออกบัญชีพนักงาน/รหัสผ่าน และกำหนดบทบาท (พนักงานสมัครเองไม่ได้)</summary>
[Authorize(Roles = RoleNames.Admin)]
public class AdminStaffController : AppController
{
    private readonly Csi402dbContext _db;
    public AdminStaffController(Csi402dbContext db) => _db = db;

    private async Task<StaffPageViewModel> BuildPage(CreateStaffViewModel? form = null) => new()
    {
        Form = form ?? new CreateStaffViewModel(),
        Staff = await _db.Users.AsNoTracking()
            .Where(u => u.Role.RoleName != RoleNames.Customer)
            .OrderBy(u => u.UserId)
            .Select(u => new StaffListItemViewModel
            {
                UserId = u.UserId,
                FullName = u.FirstName + " " + u.LastName,
                Email = u.Email,
                Phone = u.Phone,
                RoleName = u.Role.RoleName,
                CreatedAt = u.CreatedAt
            })
            .ToListAsync()
    };

    private void ShowCredential(string email, string password)
    {
        TempData["NewEmail"] = email;
        TempData["NewPassword"] = password;
    }

    [HttpGet]
    public async Task<IActionResult> Index() => View(await BuildPage());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Form")] CreateStaffViewModel form)
    {
        if (!RoleNames.IsStaff(form.Role))
            ModelState.AddModelError("Form.Role", "บทบาทพนักงานไม่ถูกต้อง");
        if (!ModelState.IsValid) return View("Index", await BuildPage(form));

        var email = form.Email.Trim().ToLowerInvariant();
        var phone = form.Phone.Trim();

        var dup = await BookingQueries.FindDuplicateUserAsync(_db, email, phone);
        if (dup != null)
        {
            ModelState.AddModelError(dup.Email == email ? "Form.Email" : "Form.Phone",
                dup.Email == email ? "อีเมลนี้ถูกใช้แล้ว" : "เบอร์โทรนี้ถูกใช้แล้ว");
            return View("Index", await BuildPage(form));
        }

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName == form.Role.ToLower());
        if (role == null)
        {
            Flash("ไม่พบบทบาทนี้ในฐานข้อมูล กรุณารีสตาร์ทระบบเพื่อสร้างบทบาทเริ่มต้น", true);
            return RedirectToAction(nameof(Index));
        }

        var password = PasswordHelper.Generate();
        _db.Users.Add(new UserEntity
        {
            FirstName = form.FirstName.Trim(),
            LastName = form.LastName.Trim(),
            Phone = phone,
            Email = email,
            PasswordHash = PasswordHelper.Hash(password), SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = role,
            IsMember = false,
            CreatedAt = pameka_7033_l005.Helpers.AppClock.Now
        });

        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            Flash("บันทึกไม่สำเร็จ (อีเมลหรือเบอร์อาจซ้ำ) กรุณาลองใหม่", true);
            return RedirectToAction(nameof(Index));
        }

        ShowCredential(email, password);
        Flash("สร้างบัญชีพนักงานสำเร็จ");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    // ออกรหัสผ่านใหม่ให้พนักงานหรือลูกค้าที่ลืมรหัส (ยังไม่มีระบบส่งอีเมล) · back = "users" กลับไปหน้ารายชื่อผู้ใช้
    public async Task<IActionResult> ResetPassword(int userId, string? back)
    {
        IActionResult Back() => back == "users" ? RedirectToAction("UserList", "Account") : RedirectToAction(nameof(Index));
        if (userId == CurrentUserId) { Flash("เปลี่ยนรหัสผ่านของตัวเองได้ที่หน้าตั้งค่า", true); return Back(); }
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null) { Flash("ไม่พบผู้ใช้", true); return Back(); }

        var password = PasswordHelper.Generate();
        user.SetPassword(password);   // เซสชันเดิมของผู้ใช้นี้ถูกตัด
        await _db.SaveChangesAsync();

        ShowCredential(user.Email, password);
        Flash("ออกรหัสผ่านใหม่สำเร็จ");
        return Back();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeRole(int userId, string role)
    {
        if (userId == CurrentUserId) { Flash("ไม่สามารถเปลี่ยนบทบาทของตัวเองได้", true); return RedirectToAction(nameof(Index)); }
        if (!RoleNames.IsStaff(role)) { Flash("บทบาทพนักงานไม่ถูกต้อง", true); return RedirectToAction(nameof(Index)); }

        var user = await _db.Users.Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.UserId == userId && u.Role.RoleName != RoleNames.Customer);
        var newRole = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName == role.ToLower());
        if (user == null || newRole == null) { Flash("ไม่พบข้อมูลที่ต้องการแก้ไข", true); return RedirectToAction(nameof(Index)); }

        user.RoleId = newRole.RoleId;
        await _db.SaveChangesAsync();
        Flash("เปลี่ยนบทบาทสำเร็จ (มีผลทันที พนักงานต้องเข้าสู่ระบบใหม่)");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int userId)
    {
        if (userId == CurrentUserId) { Flash("ไม่สามารถลบบัญชีของตัวเองได้", true); return RedirectToAction(nameof(Index)); }

        var user = await _db.Users.Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.UserId == userId && u.Role.RoleName != RoleNames.Customer);
        if (user == null) { Flash("ไม่พบพนักงาน", true); return RedirectToAction(nameof(Index)); }

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            await _db.LoginHistories.Where(h => h.UserId == userId).ExecuteDeleteAsync();
            _db.Users.Remove(user);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync();
            Flash("ลบไม่สำเร็จ: พนักงานนี้มีข้อมูลที่เกี่ยวข้อง (เช่น งานที่รับผิดชอบ/การยืนยันการจอง)", true);
            return RedirectToAction(nameof(Index));
        }

        Flash("ลบพนักงานสำเร็จ");
        return RedirectToAction(nameof(Index));
    }
}

// ======================================================================
// Part 52: AdminActivitiesController — จัดการกิจกรรมของโรงแรม
// ======================================================================
public class AdminActivitiesController : AdminAreaController
{
    public AdminActivitiesController(Csi402dbContext db) : base(db) { }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var today = DateOnly.FromDateTime(AppClock.Today);
        var stats = await Db.ActivityBookings.Where(b => b.Status != "CANCELLED")
            .GroupBy(b => b.ActivityId).Select(g => new { g.Key, Total = g.Count(), Up = g.Count(b => b.ActDate >= today && b.Status == "CONFIRMED") })
            .ToDictionaryAsync(x => x.Key);
        var list = (await Db.Activities.AsNoTracking().OrderBy(a => a.Category).ThenBy(a => a.Name).ToListAsync())
            .Select(a => new ActivityEditViewModel
            {
                ActivityId = a.ActivityId, Name = a.Name, Category = a.Category, Description = a.Description, Price = a.Price, DurationMin = a.DurationMin,
                Capacity = a.Capacity, Times = a.Times, Days = a.Days.Select(c => c - '0').ToList(), Location = a.Location, IsActive = a.IsActive,
                TotalBooked = stats.TryGetValue(a.ActivityId, out var s) ? s.Total : 0, Upcoming = stats.TryGetValue(a.ActivityId, out var s2) ? s2.Up : 0
            }).ToList();
        return View(list);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(ActivityEditViewModel f)
    {
        var times = HotelActivities.ParseTimes(f.Times);
        var days = HotelActivities.NormalizeDays(string.Concat(f.Days.Where(d => d is >= 0 and <= 6)));
        if (!HotelActivities.IsCategory(f.Category)) ModelState.AddModelError(nameof(f.Category), "หมวดกิจกรรมไม่ถูกต้อง");
        if (times.Count == 0) ModelState.AddModelError(nameof(f.Times), "รอบเวลาต้องเป็นรูปแบบ HH:mm คั่นด้วยจุลภาค เช่น 09:00,14:00");
        if (times.Count > 6) ModelState.AddModelError(nameof(f.Times), "กำหนดได้ไม่เกิน 6 รอบต่อวัน");
        if (days.Length == 0) ModelState.AddModelError(nameof(f.Days), "เลือกวันที่เปิดกิจกรรมอย่างน้อย 1 วัน");
        if (!ModelState.IsValid) { FlashModelErrors(); return RedirectToAction(nameof(Index)); }

        Activity? a;
        if (f.ActivityId == 0) { a = new Activity { CreatedAt = AppClock.Now }; Db.Activities.Add(a); }
        else if ((a = await Db.Activities.FindAsync(f.ActivityId)) == null) return Stale(nameof(Index));
        a.Name = f.Name.Trim(); a.Category = f.Category; a.Price = f.Price; a.DurationMin = (short)f.DurationMin; a.Capacity = (short)f.Capacity;
        a.Times = string.Join(",", times); a.Days = days; a.IsActive = f.IsActive;
        a.Description = string.IsNullOrWhiteSpace(f.Description) ? null : f.Description.Trim();
        a.Location = string.IsNullOrWhiteSpace(f.Location) ? null : f.Location.Trim();
        try { await Db.SaveChangesAsync(); }
        catch (DbUpdateException) { Flash("บันทึกไม่สำเร็จ (ชื่อกิจกรรมอาจซ้ำกับรายการเดิม)", true); return RedirectToAction(nameof(Index)); }
        await AuditTrail.WriteAsync(HttpContext, "AdminActivities.Save", a.ActivityId, true, a.Name);
        Flash($"บันทึกกิจกรรม \"{a.Name}\" แล้ว");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>ลบได้เฉพาะกิจกรรมที่ยังไม่เคยมีคนจอง (มีประวัติแล้วให้ปิดใช้งานแทน)</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var a = await Db.Activities.FirstOrDefaultAsync(x => x.ActivityId == id);
        if (a == null) return Stale();
        if (await Db.ActivityBookings.AnyAsync(b => b.ActivityId == id))
        {
            Flash($"ลบ \"{a.Name}\" ไม่ได้ เพราะมีประวัติการจองแล้ว (ให้ปิดการใช้งานแทน)", true);
            return RedirectToAction(nameof(Index));
        }
        Db.Activities.Remove(a);
        await Db.SaveChangesAsync();
        await AuditTrail.WriteAsync(HttpContext, "AdminActivities.Delete", id, true, a.Name);
        Flash($"ลบกิจกรรม \"{a.Name}\" แล้ว");
        return RedirectToAction(nameof(Index));
    }
}
