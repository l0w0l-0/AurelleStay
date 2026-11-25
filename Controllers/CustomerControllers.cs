using System.Security.Claims;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using pameka_7033_l005.Helpers;
using pameka_7033_l005.Models;
using pameka_7033_l005.ViewModels;

namespace pameka_7033_l005.Controllers;

// หน้าบ้าน/ลูกค้า: หน้าแรก, ห้องพัก, บริการ, การจอง+ชำระเงิน, รีวิว

// ======================================================================
// HomeController
// ======================================================================
[AllowAnonymous]
public class HomeController : Controller
{
    private readonly Csi402dbContext _db;
    public HomeController(Csi402dbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var page = new HomePageViewModel();
        try
        {
            var rooms = BookingQueries.Bookable(_db);
            page.Types = await _db.RoomTypes.AsNoTracking()
                .Where(t => rooms.Any(r => r.TypeId == t.TypeId))
                .OrderBy(t => t.BasePrice)
                .Select(t => new RoomTypeCardViewModel(t.TypeId, t.TypeName, t.BasePrice, t.MaxGuests, t.ImageUrl, t.Description,
                    rooms.Count(r => r.TypeId == t.TypeId)))
                .ToListAsync();
            page.MinPrice = page.Types.Count > 0 ? page.Types.Min(t => t.Price) : null;

            // รีวิวจริง: ค่าเฉลี่ยทั้งหมด + รีวิว 4–5 ดาวล่าสุดที่มีข้อความ 3 รายการ
            page.ReviewCount = await _db.Reviews.CountAsync();
            if (page.ReviewCount > 0)
            {
                page.ReviewAverage = await _db.Reviews.AverageAsync(r => (double)r.Rating);
                page.TopReviews = (await _db.Reviews.AsNoTracking()
                    .Where(r => r.Rating >= 4 && r.Comment != null && r.Comment != "")
                    .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.ReviewId).Take(3)
                    .Select(r => new { r.ReviewId, r.BookingId, r.User.FirstName, r.User.LastName, r.Rating, r.Comment, r.CreatedAt }).ToListAsync())
                    .Select(r => new ReviewItemViewModel { Id = r.ReviewId, BookingId = r.BookingId, Name = ReviewsController.MaskName(r.FirstName, r.LastName), Rating = r.Rating, Comment = r.Comment, CreatedAt = r.CreatedAt })
                    .ToList();
            }
            page.Services = (await BookingQueries.ActiveServices(_db).ToListAsync()).OrderByDescending(s => s.ImageUrl != null).ThenBy(s => s.Price).Take(4).ToList();
            var pub = await PromoCatalog.PublicAsync(_db, DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today));
            page.MemberPercent = pub.Where(x => x.P.PromoType == "MEMBER").Select(x => (decimal?)x.P.DiscountPercent).FirstOrDefault();
            page.BestPercent = pub.Count > 0 ? pub.Max(x => x.P.DiscountPercent) : null;
            // Part 49: แบนเนอร์แคมเปญ/คูปองเทศกาล (แสดงเฉพาะที่ตั้งเป็นแบนเนอร์ และยังไม่ได้ใช้สิทธิ์)
            var uid = User.IsInRole(RoleNames.Customer) && int.TryParse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier), out var u) ? u : 0;
            page.Campaigns = (await PromoCampaigns.CardsAsync(_db, DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today), uid))
                .Where(c => c.Featured && !c.UsedUp).Take(6).ToList();
        }
        catch (Exception)
        {
            // ฐานข้อมูลยังไม่พร้อม: แสดงหน้าแรกต่อได้ ไม่โชว์ส่วนที่ต้องใช้ข้อมูล
        }
        return View(page);
    }

    /// <summary>เกี่ยวกับเรา + ช่องทางติดต่อ (รวมเป็นหน้าเดียว) ข้อมูลติดต่ออ่านจาก appsettings หมวด Hotel ไม่แสดงค่าที่ยังไม่ได้ตั้ง</summary>
    public async Task<IActionResult> About([FromServices] IConfiguration cfg, string? topic)
    {
        var form = new ContactFormViewModel { Topic = topic != null && ContactInbox.Topics.ContainsKey(topic) ? topic.ToUpperInvariant() : "GENERAL" };
        // ผู้ใช้ที่เข้าสู่ระบบอยู่: เติมชื่อ/อีเมล/เบอร์ให้
        if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
        {
            var u = await _db.Users.AsNoTracking().Where(x => x.UserId == uid).Select(x => new { x.FirstName, x.LastName, x.Email, x.Phone }).FirstOrDefaultAsync();
            if (u != null) { form.Name = $"{u.FirstName} {u.LastName}".Trim(); form.Email = u.Email; form.Phone = u.Phone; }
        }
        return View(new AboutPageViewModel { Hotel = HotelInfo.From(cfg), Form = form });
    }

    /// <summary>
    /// ส่งข้อความติดต่อโรงแรม (ไม่ต้องเข้าสู่ระบบ) — กันสแปมด้วยช่องดักบอท และจำกัด 3 ข้อความ / 10 นาที / IP
    /// ผู้ที่เข้าสู่ระบบอยู่จะผูกข้อความกับบัญชี เพื่อให้เจ้าหน้าที่ตอบกลับเป็นการแจ้งเตือนในระบบได้
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SendMessage([Bind(Prefix = "Form")] ContactFormViewModel f)
    {
        var back = Url.Action(nameof(About)) + "#contact";
        if (!string.IsNullOrEmpty(f.Website)) { TempData["Status"] = "ส่งข้อความแล้ว ขอบคุณที่ติดต่อเรา"; return Redirect(back); }   // บอท: ทำเหมือนสำเร็จแต่ไม่บันทึก
        if (!ContactInbox.Topics.ContainsKey(f.Topic ?? "")) ModelState.AddModelError("Form.Topic", "กรุณาเลือกหัวข้อ");
        if (!ModelState.IsValid)
        {
            TempData["Status"] = string.Join(" / ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct());
            TempData["StatusError"] = true;
            return Redirect(back);
        }
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var since = pameka_7033_l005.Helpers.AppClock.Now - ContactInbox.Window;
        if (ip != null && await _db.ContactMessages.CountAsync(m => m.Ip == ip && m.CreatedAt >= since) >= ContactInbox.MaxPerWindow)
        {
            TempData["Status"] = "ส่งข้อความถี่เกินไป กรุณารอสักครู่แล้วลองใหม่ หรือติดต่อโรงแรมทางโทรศัพท์"; TempData["StatusError"] = true;
            return Redirect(back);
        }
        int? userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null;
        _db.ContactMessages.Add(new ContactMessage
        {
            UserId = userId, Name = f.Name.Trim(), Email = f.Email.Trim(), Phone = string.IsNullOrWhiteSpace(f.Phone) ? null : f.Phone.Trim(),
            Topic = f.Topic!.ToUpperInvariant(), Message = f.Message.Trim(), Ip = ip, Status = "NEW", CreatedAt = pameka_7033_l005.Helpers.AppClock.Now
        });
        await _db.SaveChangesAsync();
        TempData["Status"] = f.Topic!.Equals("PASSWORD", StringComparison.OrdinalIgnoreCase)
            ? "ส่งคำขอแล้ว เจ้าหน้าที่จะตรวจสอบตัวตนและติดต่อกลับทางอีเมล/โทรศัพท์ที่ให้ไว้"
            : "ส่งข้อความแล้ว เจ้าหน้าที่จะติดต่อกลับโดยเร็ว";
        return Redirect(back);
    }

    /// <summary>เงื่อนไขการจอง/ยกเลิก/คืนเงิน และนโยบายความเป็นส่วนตัว (ตัวเลขดึงจากค่าที่ระบบใช้จริง)</summary>
    public IActionResult Policy([FromServices] IConfiguration cfg) => View(HotelInfo.From(cfg));

    /// <summary>ที่อยู่เว็บสำหรับ sitemap/robots: ใช้ App:PublicUrl ถ้าตั้งไว้ (กัน Host header ปลอม) ไม่เช่นนั้นใช้จากคำขอ</summary>
    private string PublicRoot(IConfiguration cfg) =>
        cfg["App:PublicUrl"] is { Length: > 0 } u && Uri.TryCreate(u.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.GetLeftPart(UriPartial.Path).TrimEnd('/')
            : $"{Request.Scheme}://{Request.Host}{Request.PathBase}";

    /// <summary>robots.txt: ให้ค้นหาเจอเฉพาะหน้าสาธารณะ และบอกที่อยู่ sitemap</summary>
    [HttpGet("/robots.txt")]
    public IActionResult Robots([FromServices] IConfiguration cfg)
    {
        var disallow = new[] { "/Account/", "/Admin", "/Accounting", "/FrontDesk", "/Delivery", "/Housekeeping",
            "/Booking", "/BookingDetails", "/Settings", "/Reviews/Create", "/Notifications", "/health" };
        var body = "User-agent: *\n" + string.Concat(disallow.Select(d => $"Disallow: {d}\n"))
                 + $"Sitemap: {PublicRoot(cfg)}/sitemap.xml\n";
        return Content(body, "text/plain; charset=utf-8");
    }

    /// <summary>sitemap.xml: หน้าสาธารณะ + หน้ารายละเอียดของทุกห้อง (อ่านจากฐานข้อมูลจริง)</summary>
    [HttpGet("/sitemap.xml")]
    public async Task<IActionResult> Sitemap([FromServices] IConfiguration cfg)
    {
        var root = PublicRoot(cfg);
        var paths = new List<string> { "/", "/Rooms", "/Rooms/Availability", "/Services", "/Reviews", "/Home/About", "/Home/Policy" };
        paths.AddRange((await _db.Rooms.AsNoTracking().OrderBy(r => r.RoomId).Select(r => r.RoomId).ToListAsync()).Select(id => $"/Rooms/Details/{id}"));
        var xml = new System.Xml.Linq.XNamespace[] { "http://www.sitemaps.org/schemas/sitemap/0.9" }[0];
        var doc = new System.Xml.Linq.XElement(xml + "urlset",
            paths.Select(p => new System.Xml.Linq.XElement(xml + "url", new System.Xml.Linq.XElement(xml + "loc", root + p))));
        return Content("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + doc, "application/xml; charset=utf-8");
    }

    /// <summary>ลิงก์เดิม /Home/Contact ยังใช้ได้ → ไปส่วนติดต่อในหน้าเกี่ยวกับเรา</summary>
    public IActionResult Contact() => Redirect(Url.Action(nameof(About)) + "#contact");

    /// <summary>หน้าแจ้งข้อผิดพลาด: ระบบขัดข้อง (500) และรหัสสถานะ เช่น ไม่พบหน้า (404) — รับได้ทุก method เพราะถูกเรียกซ้ำจากคำขอเดิม</summary>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [IgnoreAntiforgeryToken]
    public IActionResult Error(int? code)
    {
        return View(new ErrorViewModel { StatusCode = code ?? 500, RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

// ======================================================================
// RoomsController
// ======================================================================
/// <summary>ค้นหา/ดูห้องพัก (ทุกคนดูได้ ไม่ต้องล็อกอิน)</summary>
[AllowAnonymous]
public class RoomsController : Controller
{
    private readonly Csi402dbContext _db;
    public RoomsController(Csi402dbContext db) => _db = db;

    /// <summary>คืนที่ห้องไม่ว่าง (JSON รายการวันที่ yyyy-MM-dd) สำหรับปฏิทินเลือกวันในหน้ารายละเอียดห้อง</summary>
    [HttpGet]
    public async Task<IActionResult> BookedNights(int id)
    {
        if (!await _db.Rooms.AnyAsync(r => r.RoomId == id)) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return Json(await BookingQueries.BookedNightsAsync(_db, id));
    }

    /// <summary>Part 51: ลงชื่อรอห้องว่าง (เฉพาะลูกค้า) — typeId ว่าง = ห้องใดก็ได้ · กลับไปหน้าที่ส่งมา
    /// (คอนโทรลเลอร์นี้เปิดให้ทุกคน [AllowAnonymous] ซึ่งชนะ [Authorize] ระดับ action จึงตรวจบทบาทเองที่นี่)</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Waitlist(int? typeId, DateOnly? checkIn, DateOnly? checkOut, int guests = 1, string? back = null)
    {
        if (User.Identity?.IsAuthenticated != true)
            return RedirectToAction("Login", "Account", new { returnUrl = Url.IsLocalUrl(back) ? back : "/Rooms" });
        if (!User.IsInRole(RoleNames.Customer) || !int.TryParse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier), out var uid)) return Forbid();
        var (ok, msg) = await pameka_7033_l005.Helpers.Waitlist.JoinAsync(_db, uid, typeId, checkIn, checkOut, guests, DateOnly.FromDateTime(AppClock.Today));
        TempData["Status"] = msg; if (!ok) TempData["StatusError"] = true;
        if (ok) await AuditTrail.WriteAsync(HttpContext, "Rooms.Waitlist", typeId, true, $"ลงชื่อรอห้องว่าง {checkIn:yyyy-MM-dd} – {checkOut:yyyy-MM-dd}");
        return Url.IsLocalUrl(back) ? Redirect(back!) : RedirectToAction("Index", "Member");
    }

    /// <summary>รายละเอียดห้อง: ข้อมูลห้อง คะแนนรีวิวของผู้ที่เคยพักห้องนี้ ปฏิทินว่าง 2 เดือน และฟอร์มเลือกวันจอง</summary>
    [HttpGet]
    public async Task<IActionResult> Details(int id, DateOnly? checkIn, DateOnly? checkOut, int? guests)
    {
        var room = await _db.Rooms.AsNoTracking().Where(r => r.RoomId == id).Select(BookingQueries.ToCard).FirstOrDefaultAsync();
        if (room == null) return NotFound();

        var reviews = _db.Reviews.AsNoTracking().Where(rv => rv.Booking.BookingRooms.Any(br => br.RoomId == id));
        var count = await reviews.CountAsync();
        var latest = await reviews.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.ReviewId).Take(5)
            .Select(r => new { r.ReviewId, r.BookingId, r.User.FirstName, r.User.LastName, r.Rating, r.Comment, r.CreatedAt }).ToListAsync();
        var today = DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today);
        var vm = new RoomDetailsViewModel
        {
            Room = room, Today = today, ReviewCount = count,
            // รับวันที่จากหน้าค้นหาเฉพาะที่ถูกต้อง (ไม่ย้อนหลัง, ออกหลังเข้า, ไม่เกินจำนวนคืนสูงสุด)
            CheckIn = checkIn is { } ci && ci >= today && checkOut is { } co && co > ci && co.DayNumber - ci.DayNumber <= BookingRules.MaxNights ? ci : today,
            CheckOut = checkIn is { } ci2 && ci2 >= today && checkOut is { } co2 && co2 > ci2 && co2.DayNumber - ci2.DayNumber <= BookingRules.MaxNights ? co2 : today.AddDays(1),
            Guests = guests is >= 1 and <= BookingRules.MaxGuests ? guests.Value : 1,
            Average = count == 0 ? 0 : await reviews.AverageAsync(r => (double)r.Rating),
            Maintenance = await _db.Rooms.AnyAsync(r => r.RoomId == id && r.Status == "MAINTENANCE"),
            Reviews = latest.Select(r => new ReviewItemViewModel { Id = r.ReviewId, BookingId = r.BookingId, Name = ReviewsController.MaskName(r.FirstName, r.LastName), Rating = r.Rating, Comment = r.Comment, CreatedAt = r.CreatedAt }).ToList()
        };
        // Part 43: ราคาช่วงวันที่เลือก + ช่วงราคาพิเศษที่กำลังจะมาถึง (120 วัน)
        RatePlan.ApplyToCards(new[] { room }, vm.CheckIn, vm.CheckOut, await RatePlan.SeasonsAsync(_db, vm.CheckIn, vm.CheckOut));
        vm.Seasons = (await RatePlan.SeasonsAsync(_db, today, today.AddDays(120))).OrderBy(s => s.Start).ToList();
        foreach (var m in new[] { today, today.AddMonths(1) })
        {
            var cal = await BookingQueries.AvailabilityAsync(_db, m.Year, m.Month);
            vm.Months.Add((cal.Month, cal.Rows.FirstOrDefault(r => r.RoomId == id)?.Busy ?? new bool[cal.Days]));
        }
        return View(vm);
    }

    /// <summary>ปฏิทินห้องว่างรายห้อง (ผู้เยี่ยมชมดูได้ เห็นเพียงว่าง/ไม่ว่าง ไม่เห็นผู้จอง)</summary>
    [HttpGet]
    public async Task<IActionResult> Availability(int? year, int? month) => View(await BookingQueries.AvailabilityAsync(_db, year, month));

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] RoomSearchViewModel search)
    {
        var page = new RoomsPageViewModel { Search = search };
        page.Themes = await _db.RoomThemes.AsNoTracking().OrderBy(t => t.ThemeName)
            .Select(t => new ThemeOption(t.ThemeId, t.ThemeName)).ToListAsync();
        page.Types = (await _db.RoomTypes.AsNoTracking().Select(t => new { t.TypeId, t.TypeName, t.BasePrice }).ToListAsync())
            .OrderBy(t => t.BasePrice).Select(t => new ThemeOption(t.TypeId, t.TypeName)).ToList();

        // 1) เลือกฐานห้อง: ถ้าเลือกวันถูกต้อง ใช้เฉพาะห้องว่างในช่วงนั้น ไม่เช่นนั้นใช้ห้องที่เปิดให้จองทั้งหมด
        IQueryable<Room> rooms;
        if (search.CheckIn.HasValue || search.CheckOut.HasValue)
        {
            var today = DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today);
            page.DateError = BookingRules.ValidateDates(search.CheckIn, search.CheckOut, today);
            rooms = page.DateError == null
                ? BookingQueries.Available(_db, search.CheckIn!.Value, search.CheckOut!.Value)
                : BookingQueries.Bookable(_db);
        }
        else
        {
            rooms = BookingQueries.Bookable(_db);
        }

        // 2) แล้วค่อยใส่ตัวกรอง (ต้องใส่หลังเลือกฐานเสมอ ไม่เช่นนั้นตัวกรองจะหาย)
        rooms = rooms.AsNoTracking();
        if (search.Guests is > 0) rooms = rooms.Where(r => r.Type.MaxGuests >= search.Guests);
        var datedSearch = page.DateError == null && search.CheckIn.HasValue && search.CheckOut.HasValue;
        if (search.MaxPrice is > 0 && !datedSearch) rooms = rooms.Where(r => r.Type.BasePrice <= search.MaxPrice);
        if (search.ThemeId is > 0) rooms = rooms.Where(r => r.ThemeId == search.ThemeId);
        if (search.TypeId is > 0) rooms = rooms.Where(r => r.TypeId == search.TypeId);

        // เรียงในหน่วยความจำ (จำนวนห้องของโรงแรมไม่มาก และฐานข้อมูลบางตัวเรียง decimal ไม่ได้)
        var cards = await rooms.OrderBy(r => r.RoomNo).Select(BookingQueries.ToCard).ToListAsync();
        if (page.DateError == null && search.CheckIn is { } sci && search.CheckOut is { } sco && sco > sci)
        {
            // Part 43: แสดงราคาตามวันที่เลือก (เฉลี่ยต่อคืน) และกรองราคาสูงสุดจากราคานั้น
            RatePlan.ApplyToCards(cards, sci, sco, await RatePlan.SeasonsAsync(_db, sci, sco));
            if (search.MaxPrice is > 0) cards = cards.Where(c => c.BasePrice <= search.MaxPrice).ToList();
        }
        page.Rooms = search.Sort switch
        {
            "price" => cards.OrderBy(r => r.BasePrice).ThenBy(r => r.RoomNo).ToList(),
            "price_desc" => cards.OrderByDescending(r => r.BasePrice).ThenBy(r => r.RoomNo).ToList(),
            "guests" => cards.OrderByDescending(r => r.MaxGuests).ThenBy(r => r.RoomNo).ToList(),
            _ => cards
        };

        return View(page);
    }
}

// ======================================================================
// ServicesController
// ======================================================================
[AllowAnonymous]
public class ServicesController : Controller
{
    private readonly Csi402dbContext _db;
    public ServicesController(Csi402dbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        return View(await BookingQueries.ActiveServices(_db).ToListAsync());
    }
}

// ======================================================================
// BookingController
// ======================================================================
/// <summary>จองห้องพักของลูกค้า (เฉพาะบทบาท customer)</summary>
[Authorize(Roles = RoleNames.Customer)]
public class BookingController : AppController
{
    private readonly Csi402dbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly BookingBuilder _builder;

    public BookingController(Csi402dbContext db, IWebHostEnvironment env) { _db = db; _env = env; _builder = new BookingBuilder(db); }

    private static DateOnly Today => DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today);

    // ---------------------------------------------------------------- ฟอร์มจอง
    [HttpGet]
    public async Task<IActionResult> Create(DateOnly? checkIn, DateOnly? checkOut, List<int>? roomIds, int? guests, string? code)
    {
        var ci = checkIn ?? Today.AddDays(1);
        var vm = new BookingFormViewModel
        {
            PromoCode = PricingService.NormalizeCode(code) is { Length: <= 30 } c ? c : null,
            CheckIn = ci,
            CheckOut = checkOut ?? ci.AddDays(1),
            RoomIds = roomIds ?? new List<int>(),
            Guests = guests is >= 1 and <= BookingRules.MaxGuests ? guests : 1
        };
        await _builder.FillListsAsync(vm);
        await CouponsAsync(vm);
        return View(vm);
    }

    /// <summary>Part 49: คูปองที่ใช้กับวันเข้าพักนี้ได้ — ที่เก็บไว้แล้ว (ระบบใช้ให้อัตโนมัติ) และที่ยังเก็บได้ (ปุ่ม "เก็บและใช้")</summary>
    private async Task CouponsAsync(BookingFormViewModel vm)
    {
        if (vm.CheckIn is not { } ci) return;
        var cards = await PromoCampaigns.CardsAsync(_db, Today, CurrentUserId ?? 0);
        bool Fits(PromoPublicViewModel p) => (p.Start == null || ci >= p.Start) && (p.End == null || ci <= p.End);
        vm.Coupons = cards.Where(p => p.Claimable && Fits(p) && (p.Claimed ? !p.UsedUp : p.ClaimState == "open")).ToList();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(BookingFormViewModel vm)
    {
        vm.Staff = false;   // ฟอร์มจองของลูกค้าต้องเป็นหน้าลูกค้าเสมอ: ห้ามเชื่อค่า Staff จาก hidden field (ไม่งั้นได้เลย์เอาต์/หน้าของแอดมิน)
        await _builder.FillListsAsync(vm);
        if (vm.Error == null) await _builder.QuoteAsync(vm, CurrentUserId ?? 0);
        await CouponsAsync(vm);
        return View("Create", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(BookingFormViewModel vm)
    {
        vm.Staff = false;   // เหตุผลเดียวกับ Preview
        var booking = await _builder.ConfirmAsync(vm, CurrentUserId ?? 0);
        if (booking == null) { await CouponsAsync(vm); return View("Create", vm); }

        Flash("จองห้องพักสำเร็จ กรุณาชำระเงินเพื่อยืนยันการจอง");
        return RedirectToAction(nameof(Pay), new { id = booking.BookingId });
    }

    // ---------------------------------------------------------------- การจองของฉัน
    [HttpGet]
    public async Task<IActionResult> MyBookings()
    {
        var uid = CurrentUserId ?? 0;
        await BookingExpiry.RunAsync(_db, pameka_7033_l005.Helpers.AppClock.Now, uid);   // แสดงสถานะล่าสุดเสมอ แม้งานเบื้องหลังยังไม่ถึงรอบ
        return View(await MyItemsAsync(_db, uid));
    }

    /// <summary>รายการการจองของลูกค้า (ใช้ทั้งหน้า "การจองของฉัน" และหน้าสมาชิก)</summary>
    internal static async Task<List<MyBookingItemViewModel>> MyItemsAsync(Csi402dbContext db, int uid)
    {
        return await db.Bookings.AsNoTracking()
            .Where(b => b.UserId == uid)
            .OrderByDescending(b => b.BookedAt)
            .Select(b => new MyBookingItemViewModel
            {
                BookingId = b.BookingId,
                CheckIn = b.CheckIn,
                CheckOut = b.CheckOut,
                BookedAt = b.BookedAt,
                Status = b.Status,
                Net = b.NetTotal,
                Discount = b.DiscountTotal,
                PromoName = b.Promo != null ? b.Promo.PromoName : null,
                Rooms = b.BookingRooms.Select(r => r.Room.RoomNo + " " + r.Room.Type.TypeName).ToList(),
                Paid = b.Payments.Any(p => p.PaymentType == BookingWorkflow.PaymentType && p.Status == "PAID"),
                PendingPayment = b.Payments.Any(p => p.PaymentType == BookingWorkflow.PaymentType && p.Status == "PENDING"),
                PendingRefund = b.Payments.Any(p => p.PaymentType == BookingWorkflow.RefundType && p.Status == "PENDING"),
                Refunded = b.Payments.Any(p => p.PaymentType == BookingWorkflow.RefundType && p.Status == "REFUNDED"),
                LastRejectedAt = b.Payments.Where(p => p.Status == "REJECTED").Max(p => p.PaidAt),
                Reviewed = b.Review != null
            })
            .ToListAsync();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var booking = await LoadOwnBooking(id);
        if (booking == null) return Stale(nameof(MyBookings));

        var flags = FlagsOf(booking);
        if (!BookingWorkflow.CanCancelDirect(booking.Status, flags))
        {
            Flash(flags.PendingPayment ? "รายการนี้แจ้งชำระเงินแล้ว กรุณารอฝ่ายบัญชีตรวจสอบหรือติดต่อพนักงาน"
                : flags.Paid ? "รายการที่ชำระแล้วให้กด \"ขอคืนเงิน\" แทนการยกเลิก"
                : "ยกเลิกรายการนี้ไม่ได้ในสถานะปัจจุบัน", true);
            return RedirectToAction(nameof(MyBookings));
        }

        booking.Status = "CANCELLED";
        await _db.SaveChangesAsync();
        await pameka_7033_l005.Helpers.Waitlist.CheckAsync(_db, DateOnly.FromDateTime(AppClock.Today));   // Part 51: ห้องว่างแล้ว → แจ้งคนที่รอทันที
        Flash("ยกเลิกการจองแล้ว");
        return RedirectToAction(nameof(MyBookings));
    }

    // ---------------------------------------------------------------- ชำระเงิน (QR) / ขอคืนเงิน
    [HttpGet]
    public async Task<IActionResult> Pay(int id)
    {
        var uid = CurrentUserId ?? 0;
        await BookingExpiry.RunAsync(_db, pameka_7033_l005.Helpers.AppClock.Now, uid);
        var b = await _db.Bookings.AsNoTracking().Where(x => x.BookingId == id && x.UserId == uid)
            .Select(x => new
            {
                x.BookingId, x.CheckIn, x.CheckOut, x.Status, x.BookedAt, Net = x.NetTotal, Discount = x.DiscountTotal,
                Promo = x.Promo != null ? x.Promo.PromoName : null,
                Rooms = x.BookingRooms.Select(r => r.Room.RoomNo + " " + r.Room.Type.TypeName).ToList(),
                Pays = x.Payments.Select(p => new { p.PaymentType, p.Status, p.PaidAt }).ToList()
            }).FirstOrDefaultAsync();
        if (b == null) return Stale(nameof(MyBookings));

        var flags = PaymentFlags.From(b.Pays.Select(p => (p.PaymentType, p.Status)));
        if (b.Status != "PENDING" || flags.Paid)
        {
            Flash(b.Status == "CANCELLED" ? "การจองนี้ถูกยกเลิกแล้ว (ยกเลิกเองหรือเกินกำหนดชำระเงิน) กรุณาจองใหม่" : "การจองนี้ไม่อยู่ในขั้นตอนชำระเงินแล้ว", true);
            return RedirectToAction(nameof(MyBookings));
        }

        // รูป QR จริงของโรงแรม (ถ้ามี) — wwwroot/images/payment-qr.png หรือ .jpg (ไม่ขึ้น GitHub)
        var qrName = new[] { "payment-qr.png", "payment-qr.jpg" }.FirstOrDefault(n => System.IO.File.Exists(Path.Combine(_env.WebRootPath ?? "", "images", n)));
        var pay = PaymentInfo.From(HttpContext.RequestServices.GetRequiredService<IConfiguration>());
        return View(new PayPageViewModel
        {
            Payment = pay, QrPayload = PromptPay.Payload(pay.PromptPayId, b.Net),
            BookingId = b.BookingId, CheckIn = b.CheckIn, CheckOut = b.CheckOut, Net = b.Net, Discount = b.Discount,
            PromoName = b.Promo, Rooms = b.Rooms, PendingPayment = flags.PendingPayment,
            PayBy = BookingExpiry.Deadline(b.BookedAt, b.CheckIn, b.Pays.Where(p => p.Status == "REJECTED").Max(p => p.PaidAt)),
            Rejected = !flags.PendingPayment && b.Pays.Any(p => p.Status == "REJECTED"),
            QrUrl = qrName != null ? "~/images/" + qrName : null
        });
    }

    /// <summary>ลูกค้ากด "ฉันชำระเงินแล้ว" → สร้างรายการรอตรวจสอบให้ฝ่ายบัญชี (ยอดอ่านจากการจองในฐานข้อมูล ไม่เชื่อค่าจากฟอร์ม)</summary>
    [HttpPost, ActionName("Pay"), ValidateAntiForgeryToken]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> PayConfirm(int id, IFormFile? slip, string? reference)
    {
        await BookingExpiry.RunAsync(_db, pameka_7033_l005.Helpers.AppClock.Now, CurrentUserId ?? 0);   // เกินกำหนดแล้วแจ้งชำระไม่ได้
        var b = await LoadOwnBooking(id);
        if (b == null) return Stale(nameof(MyBookings));
        if (!BookingWorkflow.CanPay(b.Status, FlagsOf(b)))
        {
            Flash(b.Status == "CANCELLED" ? "การจองนี้ถูกยกเลิกแล้ว (ยกเลิกเองหรือเกินกำหนดชำระเงิน) กรุณาจองใหม่"
                : "แจ้งชำระเงินไม่ได้: รายการนี้แจ้งไปแล้วหรือไม่อยู่ในขั้นตอนชำระเงิน", true);
            return RedirectToAction(nameof(MyBookings));
        }

        reference = reference?.Trim();
        if (reference?.Length > 100) { Flash("เลขอ้างอิงยาวเกิน 100 ตัวอักษร", true); return RedirectToAction(nameof(Pay), new { id }); }
        var (file, error) = await UploadStore.SaveSlipAsync(slip);
        if (error != null) { Flash(error, true); return RedirectToAction(nameof(Pay), new { id }); }

        b.Payments.Add(new Payment { Amount = b.NetTotal, PaymentType = BookingWorkflow.PaymentType, Method = "QR", Status = "PENDING",
            SlipPath = file, Reference = string.IsNullOrEmpty(reference) ? null : reference });
        try { await _db.SaveChangesAsync(); }
        catch { UploadStore.DeleteSlip(file); throw; }
        Flash("แจ้งชำระเงินพร้อมสลิปแล้ว รอฝ่ายบัญชีตรวจสอบและยืนยันการจอง");
        return RedirectToAction(nameof(MyBookings));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestRefund(int id)
    {
        var b = await LoadOwnBooking(id);
        if (b == null) return Stale(nameof(MyBookings));
        if (!BookingWorkflow.CanCustomerRequestRefund(b.Status, FlagsOf(b), b.CheckOut, Today))
        {
            Flash(b.Status == "NO_SHOW" || (b.CheckOut <= Today && b.Status == "CONFIRMED")
                ? "การจองนี้เลยวันเข้าพักแล้ว (ไม่มาเข้าพัก) ขอคืนเงินผ่านระบบไม่ได้ กรุณาติดต่อโรงแรม"
                : "ขอคืนเงินไม่ได้ในสถานะปัจจุบัน (ต้องเป็นรายการที่ชำระแล้วและยังไม่เช็คอิน)", true);
            return RedirectToAction(nameof(MyBookings));
        }

        var paid = b.Payments.First(p => p.PaymentType == BookingWorkflow.PaymentType && p.Status == "PAID");
        b.Payments.Add(new Payment { Amount = paid.Amount, PaymentType = BookingWorkflow.RefundType, Method = paid.Method, Status = "PENDING" });
        await _db.SaveChangesAsync();
        Flash("ส่งคำขอคืนเงินแล้ว รอฝ่ายบัญชีอนุมัติ");
        return RedirectToAction(nameof(MyBookings));
    }

    // ---------------------------------------------------------------- ตัวช่วย
    private Task<Booking?> LoadOwnBooking(int id)
    {
        var uid = CurrentUserId ?? 0;
        return _db.Bookings.Include(b => b.Payments).FirstOrDefaultAsync(b => b.BookingId == id && b.UserId == uid);
    }

    private static PaymentFlags FlagsOf(Booking b) => PaymentFlags.From(b.Payments.Select(p => (p.PaymentType, p.Status)));
}

// ======================================================================
// PromotionsController — หน้าโปรโมชั่นสำหรับทุกคน (Part 37)
// ======================================================================
public class PromotionsController : AppController
{
    private readonly Csi402dbContext _db;
    public PromotionsController(Csi402dbContext db) => _db = db;

    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> Index()
    {
        var today = DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today);
        var uid = User.IsInRole(RoleNames.Customer) ? CurrentUserId ?? 0 : 0;
        return View(await PromoCampaigns.CardsAsync(_db, today, uid));
    }

    /// <summary>Part 49: กดเก็บคูปอง (เฉพาะลูกค้า) · เรียกจากปุ่มในหน้าโปร/หน้าแรก/หน้าจอง · ตอบ JSON เมื่อเรียกด้วย fetch</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = RoleNames.Customer)]
    public async Task<IActionResult> Claim(int id, string? returnUrl)
    {
        var today = DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today);
        var (ok, msg) = await PromoCampaigns.ClaimAsync(_db, id, CurrentUserId ?? 0, today);
        if (Request.Headers.Accept.ToString().Contains("application/json"))
            return Json(new { ok, message = msg });
        Flash(msg, !ok);
        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToAction(nameof(Index));
    }
}

// ======================================================================
// MemberController — หน้าสมาชิก (Part 36)
// ======================================================================
/// <summary>หน้าแรกของลูกค้าหลังเข้าสู่ระบบ: การเข้าพักครั้งถัดไป สิ่งที่ต้องทำ สถิติ สิทธิ์ส่วนลด และการแจ้งเตือนล่าสุด</summary>
[Authorize(Roles = RoleNames.Customer)]
public class MemberController : AppController
{
    private readonly Csi402dbContext _db;
    public MemberController(Csi402dbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var uid = CurrentUserId ?? 0;
        await BookingExpiry.RunAsync(_db, pameka_7033_l005.Helpers.AppClock.Now, uid);
        await pameka_7033_l005.Helpers.Waitlist.CheckAsync(_db, DateOnly.FromDateTime(AppClock.Today), uid);   // Part 51: ห้องที่รอว่างแล้วหรือยัง
        var me = await _db.Users.AsNoTracking().Where(u => u.UserId == uid)
            .Select(u => new { u.FirstName, u.CreatedAt, u.BirthDate, u.AnniversaryDate }).FirstOrDefaultAsync();
        if (me == null) return RedirectToAction("Index", "Home");
        var today = DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today);
        var items = await BookingController.MyItemsAsync(_db, uid);

        var active = items.Where(b => b.Status is "PENDING" or "CONFIRMED" or "CHECKED_IN" && b.CheckOut >= today)
            .OrderBy(b => b.Status == "CHECKED_IN" ? 0 : 1).ThenBy(b => b.CheckIn).ToList();
        var stayed = items.Where(b => b.Status is "CHECKED_OUT" or "CHECKED_IN").ToList();
        var paidMoney = await _db.Payments.AsNoTracking()
            .Where(p => p.Booking.UserId == uid && p.PaymentType == BookingWorkflow.PaymentType && p.Status == "PAID")
            .Select(p => p.Amount).ToListAsync();

        var vm = new MemberDashboardViewModel
        {
            FirstName = me.FirstName, MemberSince = me.CreatedAt, Today = today,
            Next = active.FirstOrDefault(b => b.Status != "PENDING") ?? active.FirstOrDefault(),
            NeedAction = items.Where(b => BookingWorkflow.CanPay(b.Status, b.Flags)).OrderBy(b => b.CheckIn).ToList(),
            ToReview = items.Where(b => BookingWorkflow.CanReview(b.Status, b.Reviewed)).OrderByDescending(b => b.CheckOut).Take(3).ToList(),
            Stays = items.Count(b => b.Status == "CHECKED_OUT"),
            Nights = stayed.Sum(b => b.CheckOut.DayNumber - b.CheckIn.DayNumber),
            Spent = paidMoney.Sum(),
            Saved = items.Where(b => b.Paid).Sum(b => b.Discount),
            Unread = await _db.Notifications.CountAsync(n => n.UserId == uid && !n.IsRead),
            HasBirthDate = me.BirthDate.HasValue, HasAnniversary = me.AnniversaryDate.HasValue
        };
        // รหัสโปรโมชั่นที่โรงแรมส่งให้ และยังใช้ได้ (เปิดอยู่ ไม่หมดช่วง สิทธิ์ไม่เต็ม ยังไม่ใช้ครบ)
        foreach (var r in await PromoCatalog.ActiveRulesAsync(_db, uid))
        {
            if (r.Type != "CODE" || r.Code == null || !r.IsRecipient) continue;
            if (r.End is { } e && e < today) continue;
            if (r.MaxUses is { } mu && r.UsedCount >= mu) continue;
            if (r.MaxPerUser is { } mpu && r.UserUsedCount >= mpu) continue;
            var promo = await _db.Promotions.AsNoTracking().FirstAsync(x => x.PromoId == r.PromoId);
            vm.MyCodes.Add((r.Code, r.Name, r.Percent, ThaiLabels.PromoCondition(promo, r.UsedCount)));
        }
        vm.Upcoming = active.Where(b => b != vm.Next).Take(4).ToList();
        vm.Notes = (await _db.Notifications.AsNoTracking().Where(n => n.UserId == uid).OrderByDescending(n => n.NotificationId).Take(4)
            .Select(n => new { n.Title, n.Link, n.CreatedAt, n.IsRead }).ToListAsync())
            .Select(n => (n.Title, n.Link, n.CreatedAt, n.IsRead)).ToList();

        // สิทธิ์ส่วนลดที่เปิดอยู่: บอกว่าพร้อมใช้หรือขาดอะไร (ไม่ต้องเดาเอง)
        foreach (var (p, used) in await PromoCatalog.PublicAsync(_db, today))
        {
            var (ready, hint) = p.PromoType switch
            {
                "MEMBER" => (true, (string?)"ใช้ได้อัตโนมัติทุกการจอง"),
                "BIRTHDAY" => me.BirthDate is { } b ? (true, $"ใช้ได้เมื่อเข้าพักตรงวันเกิด {b:dd/MM}") : (false, "เพิ่มวันเกิดในหน้าตั้งค่าเพื่อรับสิทธิ์"),
                "ANNIVERSARY" => me.AnniversaryDate is { } a ? (true, $"ใช้ได้เมื่อเข้าพักตรงวันครบรอบ {a:dd/MM}") : (false, "เพิ่มวันครบรอบในหน้าตั้งค่าเพื่อรับสิทธิ์"),
                _ => (true, null)
            };
            vm.Perks.Add((p.PromoName, p.DiscountPercent, ThaiLabels.PromoCondition(p, used), ready, hint));
        }
        vm.Waiting = await _db.Waitlist.AsNoTracking().Where(w => w.UserId == uid && (w.Status == "WAITING" || (w.Status == "NOTIFIED" && w.CheckIn >= today)))
            .OrderBy(w => w.CheckIn).Take(10)
            .Select(w => new WaitlistItemViewModel { WaitId = w.WaitId, What = w.Type != null ? w.Type.TypeName : "ห้องใดก็ได้", CheckIn = w.CheckIn,
                CheckOut = w.CheckOut, Guests = w.Guests, Status = w.Status, CreatedAt = w.CreatedAt }).ToListAsync();

        // Part 53: กำลังเข้าพัก → ขอของใช้เพิ่มได้ และเห็นสถานะของที่ขอ
        var stay = await _db.Bookings.AsNoTracking().Where(b => b.UserId == uid && b.Status == "CHECKED_IN")
            .OrderByDescending(b => b.CheckIn).Select(b => new { b.BookingId, b.DoNotDisturb, Rooms = b.BookingRooms.Select(r => new { r.RoomId, r.Room.RoomNo }).ToList() })
            .FirstOrDefaultAsync();
        if (stay != null)
        {
            vm.StayBookingId = stay.BookingId;
            vm.StayRooms = stay.Rooms.OrderBy(r => r.RoomNo).Select(r => (r.RoomId, r.RoomNo)).ToList();
            vm.Requests = (await _db.ItemRequests.AsNoTracking().Where(r => r.BookingId == stay.BookingId)
                .OrderByDescending(r => r.RequestId).Take(20)
                .Select(r => new { r.RequestId, r.Item, r.Qty, r.Room.RoomNo, r.Note, r.Urgent, r.Status, r.Missed, r.CreatedAt, r.DoneAt }).ToListAsync())
                .Select(r => new ItemRequestRowViewModel { Id = r.RequestId, What = $"{RoomRequests.ItemName(r.Item)} × {r.Qty}", RoomNo = r.RoomNo,
                    Note = r.Note, Urgent = r.Urgent, Status = r.Status, Missed = r.Missed, CreatedAt = r.CreatedAt, DoneAt = r.DoneAt }).ToList();
            // Part 54: ทำความสะอาดระหว่างพัก + ป้ายห้ามรบกวน
            vm.DoNotDisturb = stay.DoNotDisturb;
            // Part 60: สั่งบริการเสริมเพิ่มระหว่างพัก + ยอดที่ต้องชำระตอนเช็คเอาท์
            vm.StayLines = await StayBill.LinesAsync(_db, stay.BookingId);
            vm.StayServices = (await _db.ExtraServices.AsNoTracking().Where(s => s.IsActive != false).OrderBy(s => s.ServiceName)
                .Select(s => new { s.ServiceId, s.ServiceName, s.Price }).ToListAsync()).Select(s => (s.ServiceId, s.ServiceName, s.Price)).ToList();
            vm.Cleanings = await _db.HousekeepingTasks.AsNoTracking().Where(t => t.BookingId == stay.BookingId && t.Kind == "REQUEST")
                .OrderByDescending(t => t.TaskId).Take(10)
                .Select(t => new ItemRequestRowViewModel { Id = t.TaskId, What = "ทำความสะอาดห้อง", RoomNo = t.Room.RoomNo, Note = t.PrefTime,
                    Status = t.Status, Missed = t.Missed, CreatedAt = t.CreatedAt ?? DateTime.MinValue, DoneAt = t.DoneAt }).ToListAsync();
        }
        return View(vm);
    }

    /// <summary>Part 53: ขอของใช้เพิ่มระหว่างเข้าพัก (เฉพาะการจองของตัวเองที่เช็คอินแล้ว) → เข้าคิวฝ่ายส่งของทันที</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestItem(int bookingId, int roomId, string? item, int qty = 1, string? note = null, bool urgent = false)
    {
        var uid = CurrentUserId ?? 0;
        var b = await _db.Bookings.AsNoTracking().Where(x => x.BookingId == bookingId && x.UserId == uid && x.Status == "CHECKED_IN")
            .Select(x => new { x.BookingId, Rooms = x.BookingRooms.Select(r => r.RoomId).ToList() }).FirstOrDefaultAsync();
        if (b == null) { Flash("ขอของใช้ได้เฉพาะระหว่างเข้าพัก (หลังเช็คอิน)", true); return Redirect("/Member#mb-req"); }
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (!RoomRequests.IsItem(item) || !b.Rooms.Contains(roomId) || qty < 1 || qty > RoomRequests.MaxQty || note?.Length > 200)
        { Flash($"ข้อมูลไม่ถูกต้อง: เลือกรายการและห้อง จำนวน 1–{RoomRequests.MaxQty} หมายเหตุไม่เกิน 200 ตัวอักษร", true); return Redirect("/Member#mb-req"); }
        if (item == "OTHER" && note == null) { Flash("เลือก \"อื่น ๆ\" กรุณาระบุสิ่งที่ต้องการในหมายเหตุ", true); return Redirect("/Member#mb-req"); }
        var open = await _db.ItemRequests.CountAsync(r => r.BookingId == bookingId && (r.Status == "PENDING" || r.Status == "DELIVERING"));
        if (open >= RoomRequests.MaxOpenPerBooking)
        { Flash($"มีคำขอที่ยังไม่ได้ส่ง {open} รายการแล้ว รอให้ส่งก่อนแล้วค่อยขอเพิ่ม", true); return Redirect("/Member#mb-req"); }

        _db.ItemRequests.Add(new ItemRequest { BookingId = bookingId, UserId = uid, RoomId = roomId, Item = item!, Qty = (byte)qty, Note = note,
            Urgent = urgent, Status = "PENDING", CreatedAt = AppClock.Now });
        await _db.SaveChangesAsync();
        Flash($"ส่งคำขอ{RoomRequests.ItemName(item)}แล้ว พนักงานจะนำไปส่งที่ห้องโดยเร็ว");
        return Redirect("/Member#mb-req");
    }

    /// <summary>Part 60: สั่งบริการเสริมเพิ่มระหว่างพัก (ชำระที่เคาน์เตอร์ตอนเช็คเอาท์)</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> OrderService(int bookingId, int roomId, int serviceId, int qty = 1, bool urgent = false)
    {
        var uid = CurrentUserId ?? 0;
        if (!await _db.Bookings.AnyAsync(b => b.BookingId == bookingId && b.UserId == uid))
        { Flash("ไม่พบการจองนี้", true); return Redirect("/Member#mb-svc"); }
        var (ok, msg) = await StayBill.AddServiceAsync(_db, bookingId, roomId, serviceId, qty, urgent);
        Flash(msg, !ok);
        return Redirect("/Member#mb-svc");
    }

    /// <summary>Part 60: ยกเลิกบริการที่สั่งระหว่างพัก (ก่อนพนักงานรับงาน)</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelService(int bsId)
    {
        var uid = CurrentUserId ?? 0;
        var bid = await _db.BookingServices.Where(s => s.BsId == bsId && s.Booking.UserId == uid).Select(s => (int?)s.BookingId).FirstOrDefaultAsync();
        var ok = bid != null && await StayBill.CancelServiceAsync(_db, bsId, bid.Value);
        Flash(ok ? "ยกเลิกรายการแล้ว" : "ยกเลิกไม่ได้ (พนักงานกำลังนำส่งแล้ว)", !ok);
        return Redirect("/Member#mb-svc");
    }

    /// <summary>Part 54: ขอทำความสะอาดห้องระหว่างพัก (เลือกช่วงเวลาที่สะดวก) → เข้าคิวแม่บ้าน · ห้องละ 1 งานค้าง</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestCleaning(int bookingId, int roomId, string? slot, string? note = null)
    {
        var uid = CurrentUserId ?? 0;
        var b = await _db.Bookings.Where(x => x.BookingId == bookingId && x.UserId == uid && x.Status == "CHECKED_IN")
            .Select(x => new { x.BookingId, Rooms = x.BookingRooms.Select(r => r.RoomId).ToList() }).FirstOrDefaultAsync();
        if (b == null) { Flash("ขอทำความสะอาดได้เฉพาะระหว่างเข้าพัก (หลังเช็คอิน)", true); return Redirect("/Member#mb-clean"); }
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (!b.Rooms.Contains(roomId) || !HousekeepingOps.Slots.Contains(slot) || note?.Length > 200)
        { Flash("เลือกห้องและช่วงเวลาที่สะดวก (หมายเหตุไม่เกิน 200 ตัวอักษร)", true); return Redirect("/Member#mb-clean"); }
        if (await _db.HousekeepingTasks.AnyAsync(t => t.RoomId == roomId && t.Status != "DONE"))
        { Flash("ห้องนี้มีคำขอทำความสะอาดที่ยังไม่เสร็จอยู่แล้ว", true); return Redirect("/Member#mb-clean"); }

        _db.HousekeepingTasks.Add(new HousekeepingTask { RoomId = roomId, BookingId = bookingId, Status = "PENDING", Kind = "REQUEST",
            PrefTime = slot, Note = note, Urgent = slot == HousekeepingOps.Slots[0], CreatedAt = AppClock.Now });
        // ขอทำความสะอาด = พร้อมให้เข้า → ปลดป้ายห้ามรบกวนให้อัตโนมัติ
        await _db.Bookings.Where(x => x.BookingId == bookingId).ExecuteUpdateAsync(x => x.SetProperty(y => y.DoNotDisturb, false));
        await _db.SaveChangesAsync();
        Flash($"ส่งคำขอทำความสะอาดแล้ว (สะดวก {slot}) แม่บ้านจะเข้าไปตามเวลา");
        return Redirect("/Member#mb-clean");
    }

    /// <summary>Part 54: ติด/ปลดป้าย "ห้ามรบกวน" — แม่บ้านเห็นในคิวงาน</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DoNotDisturb(int bookingId, bool on)
    {
        var uid = CurrentUserId ?? 0;
        var n = await _db.Bookings.Where(x => x.BookingId == bookingId && x.UserId == uid && x.Status == "CHECKED_IN")
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.DoNotDisturb, on));
        if (n == 0) { Flash("ตั้งป้ายได้เฉพาะระหว่างเข้าพัก", true); return Redirect("/Member#mb-clean"); }
        Flash(on ? "ติดป้าย \"ห้ามรบกวน\" แล้ว แม่บ้านจะไม่เข้าห้องจนกว่าจะปลด" : "ปลดป้าย \"ห้ามรบกวน\" แล้ว");
        return Redirect("/Member#mb-clean");
    }

    /// <summary>Part 53: ยกเลิกคำขอที่ยังไม่มีพนักงานรับงาน</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelRequest(int id)
    {
        var uid = CurrentUserId ?? 0;
        var n = await _db.ItemRequests.Where(r => r.RequestId == id && r.UserId == uid && r.Status == "PENDING")
            .ExecuteUpdateAsync(x => x.SetProperty(r => r.Status, "CANCELLED"));
        Flash(n > 0 ? "ยกเลิกคำขอแล้ว" : "ยกเลิกไม่ได้: พนักงานรับงานไปแล้วหรือส่งเรียบร้อยแล้ว", n == 0);
        return Redirect("/Member#mb-req");
    }

    /// <summary>Part 51: ยกเลิกการลงชื่อรอห้องว่าง (เฉพาะรายการของตัวเอง)</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelWait(int id)
    {
        var uid = CurrentUserId ?? 0;
        var w = await _db.Waitlist.FirstOrDefaultAsync(x => x.WaitId == id && x.UserId == uid);
        if (w == null) return NotFound();
        if (w.Status != "CANCELLED") { w.Status = "CANCELLED"; await _db.SaveChangesAsync(); }
        Flash("ยกเลิกการรอห้องว่างแล้ว");
        return RedirectToAction(nameof(Index));
    }

    // ---------------- Part 44: ข้อความของฉัน — ข้อความที่ส่งถึงโรงแรมพร้อมคำตอบ (เฉพาะของตัวเอง)
    [HttpGet]
    public async Task<IActionResult> Messages()
    {
        var uid = CurrentUserId ?? 0;
        var items = await _db.ContactMessages.AsNoTracking().Where(m => m.UserId == uid)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.MessageId).Take(100)
            .Select(m => new MyMessageViewModel
            {
                Id = m.MessageId, Topic = m.Topic, Message = m.Message, Status = m.Status, CreatedAt = m.CreatedAt,
                // คำตอบที่ส่งถึงลูกค้าแล้ว = บันทึกตอนปิดเรื่อง (เหมือนที่แจ้งเตือนไป) · ระหว่างดำเนินการไม่แสดงบันทึกภายใน
                Reply = m.Status == "DONE" ? m.StaffNote : null, RepliedAt = m.Status == "DONE" ? m.HandledAt : null
            }).ToListAsync();
        return View(items);
    }

    // ---------------- Part 44: รีวิวของฉัน — รีวิวที่เขียนแล้ว + การเข้าพักที่รอรีวิว
    [HttpGet]
    public async Task<IActionResult> Reviews()
    {
        var uid = CurrentUserId ?? 0;
        var vm = new MyReviewsViewModel
        {
            Written = await _db.Reviews.AsNoTracking().Where(r => r.UserId == uid).OrderByDescending(r => r.CreatedAt)
                .Select(r => new MyReviewItem { BookingId = r.BookingId, Rating = r.Rating, Comment = r.Comment, CreatedAt = r.CreatedAt,
                    CheckIn = r.Booking.CheckIn, CheckOut = r.Booking.CheckOut }).ToListAsync(),
            ToReview = (await BookingController.MyItemsAsync(_db, uid))
                .Where(b => BookingWorkflow.CanReview(b.Status, b.Reviewed)).OrderByDescending(b => b.CheckOut).ToList()
        };
        return View(vm);
    }
}


// ======================================================================
// BookingDetailsController
// ======================================================================
/// <summary>
/// รายละเอียดการจอง/ใบเสร็จ หน้าเดียวใช้ร่วม: ลูกค้าเห็นเฉพาะของตัวเอง (ของคนอื่น = ไม่พบ ไม่บอกว่ามีอยู่),
/// ผู้จัดการ/ฝ่ายบริการ/ฝ่ายบัญชีเห็นทุกการจอง — ปุ่มที่แสดงขึ้นกับบทบาทและสถานะ
/// </summary>
[Authorize(Roles = RoleNames.Customer + "," + RoleNames.Admin + "," + RoleNames.FrontDesk + "," + RoleNames.Accounting)]
public class BookingDetailsController : AppController
{
    private readonly Csi402dbContext _db;
    public BookingDetailsController(Csi402dbContext db) => _db = db;

    [HttpGet("/Booking/Details/{id:int}")]
    public async Task<IActionResult> Index(int id)
    {
        var isCustomer = User.IsInRole(RoleNames.Customer);
        var vm = await BookingQueries.LoadDetailAsync(_db, id, isCustomer ? CurrentUserId ?? 0 : null);
        if (vm != null) return View(vm);

        Flash($"ไม่พบการจอง #{id}", true);
        if (isCustomer) return RedirectToAction("MyBookings", "Booking");
        if (User.IsInRole(RoleNames.Admin)) return RedirectToAction("Index", "AdminBookings");
        var (ctl, act) = RoleNames.HomeFor(User.FindFirstValue(ClaimTypes.Role));
        return RedirectToAction(act, ctl);
    }

    /// <summary>
    /// Part 50: เช็คอินออนไลน์ล่วงหน้า — ลูกค้าแจ้งเวลาที่จะมาถึง + คำขอพิเศษ (ฝ่ายบริการเห็นในรายการเข้าพักวันนี้)
    /// ลูกค้าแก้ได้เฉพาะการจองของตัวเอง · ฝ่ายบริการ/ผู้จัดการแก้แทนลูกค้าได้ (เช่น ลูกค้าโทรมาแจ้ง)
    /// </summary>
    [HttpPost("/Booking/PreArrival/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> PreArrival(int id, string? arrivalTime, string? request)
    {
        if (User.IsInRole(RoleNames.Accounting)) return Forbid();
        var isCustomer = User.IsInRole(RoleNames.Customer);
        var b = await _db.Bookings.FirstOrDefaultAsync(x => x.BookingId == id && (!isCustomer || x.UserId == CurrentUserId));
        if (b == null) return await Index(id);
        if (b.Status is not ("PENDING" or "CONFIRMED")) { Flash("การจองนี้เช็คอินแล้วหรือไม่อยู่ในสถานะที่แจ้งข้อมูลล่วงหน้าได้", true); return Redirect($"/Booking/Details/{id}"); }
        arrivalTime = string.IsNullOrWhiteSpace(arrivalTime) ? null : arrivalTime.Trim();
        if (arrivalTime != null && !System.Text.RegularExpressions.Regex.IsMatch(arrivalTime, "^([01][0-9]|2[0-3]):[0-5][0-9]$"))
        { Flash("รูปแบบเวลาไม่ถูกต้อง (เช่น 15:30)", true); return Redirect($"/Booking/Details/{id}"); }
        var req = new string((request ?? "").Where(ch => !char.IsControl(ch) || ch == '\n').ToArray()).Trim();
        if (req.Length > 300) { Flash("คำขอพิเศษยาวเกิน 300 ตัวอักษร", true); return Redirect($"/Booking/Details/{id}"); }
        b.ArrivalTime = arrivalTime; b.GuestRequest = req.Length == 0 ? null : req;
        await _db.SaveChangesAsync();
        Flash(arrivalTime == null && b.GuestRequest == null ? "ล้างข้อมูลเช็คอินล่วงหน้าแล้ว"
            : "บันทึกข้อมูลเช็คอินล่วงหน้าแล้ว ฝ่ายบริการจะเตรียมห้องตามเวลาที่แจ้ง" + (b.GuestRequest != null ? " (คำขอพิเศษขึ้นอยู่กับความพร้อมของโรงแรม)" : ""));
        return Redirect($"/Booking/Details/{id}");
    }

    /// <summary>Part 50: เพิ่มการเข้าพักลงปฏิทินของลูกค้า (.ics ใช้ได้กับ Google / Apple / Outlook)</summary>
    [HttpGet("/Booking/Calendar/{id:int}")]
    public async Task<IActionResult> Calendar(int id, [FromServices] IConfiguration cfg)
    {
        var isCustomer = User.IsInRole(RoleNames.Customer);
        var vm = await BookingQueries.LoadDetailAsync(_db, id, isCustomer ? CurrentUserId ?? 0 : null);
        if (vm == null || vm.Status is "CANCELLED" or "NO_SHOW") return await Index(id);
        var hotel = HotelInfo.From(cfg);
        static string E(string? s) => (s ?? "").Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r", "").Replace("\n", "\\n");
        var rooms = string.Join(", ", vm.Rooms.Select(r => $"{r.RoomNo} {r.Type}"));
        var ics = string.Join("\r\n", new[]
        {
            "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//AurelleStay//Booking//TH", "CALSCALE:GREGORIAN", "METHOD:PUBLISH",
            "BEGIN:VEVENT",
            $"UID:booking-{vm.BookingId}@aurellestay",
            $"DTSTAMP:{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}",
            $"DTSTART;VALUE=DATE:{vm.CheckIn:yyyyMMdd}",
            $"DTEND;VALUE=DATE:{vm.CheckOut:yyyyMMdd}",
            $"SUMMARY:{E($"เข้าพัก {hotel.Name} · การจอง #{vm.BookingId}")}",
            $"DESCRIPTION:{E($"ห้อง {rooms}\nเช็คอิน {hotel.CheckInTime} น. · เช็คเอาท์ {hotel.CheckOutTime} น.\nสถานะ {ThaiLabels.BookingStatus(vm.Status)} · ยอดสุทธิ {vm.Net:N2} บาท")}",
            hotel.Address != null ? $"LOCATION:{E(hotel.Address)}" : null,
            "BEGIN:VALARM", "TRIGGER:-P1D", "ACTION:DISPLAY", $"DESCRIPTION:{E($"พรุ่งนี้เข้าพักที่ {hotel.Name}")}", "END:VALARM",
            "END:VEVENT", "END:VCALENDAR"
        }.Where(l => l != null)) + "\r\n";
        return File(System.Text.Encoding.UTF8.GetBytes(ics), "text/calendar; charset=utf-8", $"aurellestay-booking-{vm.BookingId}.ics");
    }

    /// <summary>
    /// เอกสารสำหรับพิมพ์ / บันทึกเป็น PDF (ขนาด A4): kind=confirmation ใบยืนยันการจอง · kind=receipt ใบเสร็จรับเงิน
    /// (ใบเสร็จออกได้เมื่อฝ่ายบัญชียืนยันรับเงินแล้วเท่านั้น) สิทธิ์เหมือนหน้ารายละเอียดการจอง
    /// </summary>
    [HttpGet("/Booking/Document/{id:int}")]
    public async Task<IActionResult> Document(int id, string? kind, [FromServices] IConfiguration cfg)
    {
        var isCustomer = User.IsInRole(RoleNames.Customer);
        var vm = await BookingQueries.LoadDetailAsync(_db, id, isCustomer ? CurrentUserId ?? 0 : null);
        if (vm == null) return await Index(id);   // ใช้ข้อความ "ไม่พบการจอง" และปลายทางเดียวกับหน้ารายละเอียด
        kind = kind == "receipt" ? "receipt" : "confirmation";
        if (kind == "receipt" && !vm.Payments.Any(p => p.Type == BookingWorkflow.PaymentType && p.Status is "PAID" or "REFUNDED"))
        {
            Flash("ยังออกใบเสร็จไม่ได้ ต้องรอฝ่ายบัญชียืนยันการรับเงินก่อน", true);
            return RedirectToAction(nameof(Index), new { id });
        }
        Response.Headers.CacheControl = "private, no-store";
        ViewData["Kind"] = kind;
        ViewData["Hotel"] = HotelInfo.From(cfg);
        return View(vm);
    }

    /// <summary>เปิดสลิปการโอน: ลูกค้าเห็นเฉพาะของตัวเอง พนักงานบัญชี/ฝ่ายบริการ/ผู้จัดการเห็นทุกรายการ</summary>
    [HttpGet("/Booking/Slip/{paymentId:int}")]
    public async Task<IActionResult> Slip(int paymentId)
    {
        var uid = User.IsInRole(RoleNames.Customer) ? CurrentUserId ?? 0 : (int?)null;
        var name = await _db.Payments.AsNoTracking()
            .Where(p => p.PaymentId == paymentId && (uid == null || p.Booking.UserId == uid))
            .Select(p => p.SlipPath).FirstOrDefaultAsync();
        var file = UploadStore.OpenSlip(name);
        if (file == null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        return PhysicalFile(file.Value.Path, file.Value.ContentType);
    }
}

// ======================================================================
// NotificationsController
// ======================================================================
/// <summary>การแจ้งเตือนของลูกค้า: เปิดหน้ารายการ = อ่านแล้วทั้งหมด (แสดง 50 รายการล่าสุด)</summary>
[Authorize(Roles = RoleNames.Customer)]
public class NotificationsController : AppController
{
    private readonly Csi402dbContext _db;
    public NotificationsController(Csi402dbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var uid = CurrentUserId ?? 0;
        await BookingExpiry.RunAsync(_db, pameka_7033_l005.Helpers.AppClock.Now, uid);   // การจองที่หมดเวลาชำระ แจ้งทันทีไม่ต้องรอรอบงานเบื้องหลัง
        var items = await _db.Notifications.AsNoTracking().Where(n => n.UserId == uid)
            .OrderByDescending(n => n.NotificationId).Take(50).ToListAsync();
        await _db.Notifications.Where(n => n.UserId == uid && !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        return View(items);   // รายการที่ส่งไปยังมีสถานะ "ยังไม่อ่าน" เดิม ใช้ไฮไลต์รายการใหม่
    }

    /// <summary>Part 56: ลบการแจ้งเตือน 1 รายการ (เฉพาะของตัวเอง)</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var uid = CurrentUserId ?? 0;
        var n = await _db.Notifications.Where(x => x.NotificationId == id && x.UserId == uid).ExecuteDeleteAsync();
        if (Request.Headers.Accept.ToString().Contains("application/json")) return Json(new { ok = n > 0 });
        Flash(n > 0 ? "ลบการแจ้งเตือนแล้ว" : "ไม่พบการแจ้งเตือนนี้", n == 0);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Part 56: ล้างการแจ้งเตือนทั้งหมด (เฉพาะของตัวเอง)</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Clear()
    {
        var uid = CurrentUserId ?? 0;
        var n = await _db.Notifications.Where(x => x.UserId == uid).ExecuteDeleteAsync();
        Flash(n > 0 ? $"ล้างการแจ้งเตือนทั้งหมดแล้ว {n} รายการ" : "ไม่มีการแจ้งเตือนให้ล้าง");
        return RedirectToAction(nameof(Index));
    }
}

// ======================================================================
// ReviewsController
// ======================================================================
/// <summary>รีวิวจากผู้เข้าพัก: ทุกคนอ่านได้ ลูกค้าเขียนได้เฉพาะการจองของตัวเองที่เช็คเอาท์แล้ว (1 การจอง = 1 รีวิว)</summary>
public class ReviewsController : AppController
{
    private readonly Csi402dbContext _db;
    public ReviewsController(Csi402dbContext db) => _db = db;

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Index()
    {
        var count = await _db.Reviews.CountAsync();
        var avg = count == 0 ? 0 : await _db.Reviews.AverageAsync(r => (double)r.Rating);
        var rows = await _db.Reviews.AsNoTracking().OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.ReviewId).Take(50)
            .Select(r => new { r.ReviewId, r.BookingId, r.User.FirstName, r.User.LastName, r.Rating, r.Comment, r.CreatedAt }).ToListAsync();
        return View(new ReviewsPageViewModel
        {
            Count = count, Average = avg,
            Items = rows.Select(r => new ReviewItemViewModel
            {
                Id = r.ReviewId, BookingId = r.BookingId, Name = MaskName(r.FirstName, r.LastName), Rating = r.Rating, Comment = r.Comment, CreatedAt = r.CreatedAt
            }).ToList()
        });
    }

    /// <summary>แสดงชื่อแบบไม่เปิดเผยนามสกุลเต็ม เช่น "สมชาย ก."</summary>
    public static string MaskName(string first, string last) => string.IsNullOrWhiteSpace(last) ? first : $"{first} {last.Trim()[0]}.";

    [Authorize(Roles = RoleNames.Customer), HttpGet]
    public async Task<IActionResult> Create(int id)
    {
        var form = await LoadFormAsync(id);
        if (form == null)
        {
            Flash("ไม่พบการจอง หรือรีวิวไม่ได้ (ต้องเป็นการจองของคุณที่เช็คเอาท์แล้วและยังไม่เคยรีวิว)", true);
            return RedirectToAction("MyBookings", "Booking");
        }
        return View(form);
    }

    [Authorize(Roles = RoleNames.Customer), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ReviewFormViewModel f)
    {
        var uid = CurrentUserId ?? 0;
        var b = await _db.Bookings.AsNoTracking().Where(x => x.BookingId == f.BookingId && x.UserId == uid)
            .Select(x => new { x.Status, Reviewed = x.Review != null }).FirstOrDefaultAsync();
        if (b == null || !BookingWorkflow.CanReview(b.Status, b.Reviewed))
        {
            Flash("รีวิวไม่ได้: ต้องเป็นการจองของคุณที่เช็คเอาท์แล้ว และยังไม่เคยรีวิว", true);
            return RedirectToAction("MyBookings", "Booking");
        }
        if (!ModelState.IsValid)
        {
            f.Summary = (await LoadFormAsync(f.BookingId))?.Summary ?? "";
            return View(f);
        }

        _db.Reviews.Add(new Review
        {
            BookingId = f.BookingId, UserId = uid, Rating = (sbyte)f.Rating,
            Comment = string.IsNullOrWhiteSpace(f.Comment) ? null : f.Comment.Trim(), CreatedAt = pameka_7033_l005.Helpers.AppClock.Now
        });
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException) { Flash("การจองนี้ถูกรีวิวไปแล้ว", true); return RedirectToAction("MyBookings", "Booking"); }

        Flash("ขอบคุณสำหรับรีวิว");
        return RedirectToAction(nameof(Index));
    }

    private async Task<ReviewFormViewModel?> LoadFormAsync(int bookingId)
    {
        var uid = CurrentUserId ?? 0;
        var b = await _db.Bookings.AsNoTracking().Where(x => x.BookingId == bookingId && x.UserId == uid)
            .Select(x => new { x.BookingId, x.Status, x.CheckIn, x.CheckOut, Reviewed = x.Review != null, Rooms = x.BookingRooms.Select(r => r.Room.RoomNo).ToList() })
            .FirstOrDefaultAsync();
        if (b == null || !BookingWorkflow.CanReview(b.Status, b.Reviewed)) return null;
        return new ReviewFormViewModel
        {
            BookingId = b.BookingId, Rating = 5,
            Summary = $"การจอง #{b.BookingId} · {b.CheckIn:dd/MM/yyyy} – {b.CheckOut:dd/MM/yyyy} · ห้อง {string.Join(", ", b.Rooms)}"
        };
    }
}

// ======================================================================
// Part 52: ActivitiesController — กิจกรรมของโรงแรม (ทุกคนดูได้ · ผู้เข้าพักจองรอบเวลาได้)
// ======================================================================
[Authorize(Roles = RoleNames.Customer)]
public class ActivitiesController : AppController
{
    private readonly Csi402dbContext _db;
    public ActivitiesController(Csi402dbContext db) => _db = db;

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Index(string? cat)
    {
        var now = AppClock.Now;
        var today = DateOnly.FromDateTime(now);
        var isCustomer = User.IsInRole(RoleNames.Customer);
        var uid = isCustomer ? CurrentUserId ?? 0 : 0;
        var vm = new ActivitiesPageViewModel { Today = today, IsCustomer = isCustomer, Category = HotelActivities.IsCategory(cat) ? cat : null };

        var acts = await _db.Activities.AsNoTracking().Where(a => a.IsActive).OrderBy(a => a.Category).ThenBy(a => a.Name).ToListAsync();
        var taken = await HotelActivities.TakenAsync(_db, today, today.AddDays(6));
        var stays = uid == 0 ? new() : await _db.Bookings.AsNoTracking()
            .Where(b => b.UserId == uid && (b.Status == "CONFIRMED" || b.Status == "CHECKED_IN") && b.CheckOut >= today)
            .Select(b => new { b.CheckIn, b.CheckOut }).ToListAsync();
        vm.HasStay = stays.Count > 0;
        var stayDays = new SortedSet<DateOnly>();
        foreach (var s in stays)
            for (var d = s.CheckIn < today ? today : s.CheckIn; d <= s.CheckOut && d <= today.AddDays(HotelActivities.BookAheadDays); d = d.AddDays(1)) stayDays.Add(d);

        foreach (var a in acts)
        {
            var card = new ActivityCardViewModel
            {
                Id = a.ActivityId, Name = a.Name, Category = a.Category, Description = a.Description, Price = a.Price,
                DurationMin = a.DurationMin, Capacity = a.Capacity, Times = HotelActivities.ParseTimes(a.Times), Days = a.Days, Location = a.Location
            };
            for (var d = today; d <= today.AddDays(6); d = d.AddDays(1))
            {
                if (!HotelActivities.OpenOn(a, d)) continue;
                foreach (var t in card.Times)
                {
                    if (d.ToDateTime(TimeOnly.ParseExact(t, "HH:mm")) <= now.AddMinutes(30)) continue;
                    card.Sessions.Add((d, t, Math.Max(0, a.Capacity - taken.GetValueOrDefault((a.ActivityId, d, t)))));
                }
            }
            card.BookableDates = stayDays.Where(d => HotelActivities.OpenOn(a, d)).ToList();
            vm.Cards.Add(card);
        }
        if (uid != 0)
        {
            var mine = await _db.ActivityBookings.AsNoTracking().Include(b => b.Activity)
                .Where(b => b.UserId == uid && b.ActDate >= today.AddDays(-14)).OrderBy(b => b.ActDate).ThenBy(b => b.StartTime).Take(50).ToListAsync();
            vm.Mine = mine.Select(b => new ActivityBookingRowViewModel
            {
                AbId = b.AbId, ActivityId = b.ActivityId, ActivityName = b.Activity.Name, Location = b.Activity.Location, Date = b.ActDate, Time = b.StartTime,
                People = b.People, Total = b.Total, Status = b.Status, CanCancel = HotelActivities.CanCancel(b, now)
            }).ToList();
        }
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Book(int activityId, DateOnly? date, string? time, int people = 1)
    {
        var uid = CurrentUserId ?? 0;
        var (ok, msg, id) = await HotelActivities.BookAsync(_db, uid, activityId, date, time, people, AppClock.Now);
        Flash(msg, !ok);
        if (ok) await AuditTrail.WriteAsync(HttpContext, "Activities.Book", id, true, msg);
        return Redirect(Url.Action(nameof(Index)) + (ok ? "#mine" : $"#act-{activityId}"));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var uid = CurrentUserId ?? 0;
        var b = await _db.ActivityBookings.Include(x => x.Activity).FirstOrDefaultAsync(x => x.AbId == id && x.UserId == uid);
        if (b == null) return NotFound();
        if (!HotelActivities.CanCancel(b, AppClock.Now))
        {
            Flash($"ยกเลิกไม่ได้ (ยกเลิกได้ก่อนเริ่มกิจกรรมอย่างน้อย {HotelActivities.CancelBeforeHours} ชั่วโมง)", true);
            return Redirect(Url.Action(nameof(Index)) + "#mine");
        }
        b.Status = "CANCELLED"; b.CancelledAt = AppClock.Now;
        Notify.Add(_db, uid, $"ยกเลิกกิจกรรมแล้ว: {b.Activity.Name}", $"{b.ActDate:dd/MM/yyyy} เวลา {b.StartTime} น.", "/Activities");
        await _db.SaveChangesAsync();
        await AuditTrail.WriteAsync(HttpContext, "Activities.Cancel", id, true, $"{b.Activity.Name} {b.ActDate:yyyy-MM-dd} {b.StartTime}");
        Flash("ยกเลิกกิจกรรมแล้ว");
        return Redirect(Url.Action(nameof(Index)) + "#mine");
    }
}
