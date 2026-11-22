using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UserEntity = pameka_7033_l005.Models.User;
using pameka_7033_l005.Helpers;
using pameka_7033_l005.Models;
using pameka_7033_l005.ViewModels;

namespace pameka_7033_l005.Controllers;

// บัญชีผู้ใช้: ฐานของทุก controller (AppController), เข้าสู่ระบบ/สมัคร/จัดการผู้ใช้ (Account), หน้าตั้งค่า (Settings)

// ======================================================================
// AppController
// ======================================================================
/// <summary>ฐานร่วมของ controller ที่ต้องรู้ว่าใครล็อกอินอยู่ และแจ้งผลผ่านแถบข้อความ (Status/StatusError)</summary>
public abstract class AppController : Controller
{
    protected int? CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>(ต้อง Include(u =&gt; u.Role) มาก่อน) สร้าง claims ของผู้ใช้ — ใช้ทั้งตอนล็อกอินและตอนแก้ข้อมูลตัวเอง</summary>
    protected Task SignInUserAsync(UserEntity user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.FirstName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.RoleName),
            new(SessionStamp.ClaimType, user.SecurityStamp ?? "")
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }

    protected void Flash(string message, bool error = false)
    {
        TempData["Status"] = message;
        TempData["StatusError"] = error;
    }

    /// <summary>แจ้งข้อความผิดพลาดจากการตรวจสอบข้อมูลของฟอร์ม (ModelState) ทั้งหมดในแถบแจ้งผล</summary>
    protected void FlashModelErrors() => Flash(string.Join(" / ",
        ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct()), true);

    /// <summary>รายการหาไม่เจอ หรือมีคนเปลี่ยนสถานะไปก่อนแล้ว (เช่น ฝ่ายอื่นกดไปแล้ว) — แจ้งแล้วกลับหน้ารายการ</summary>
    protected IActionResult Stale(string action = "Index")
    {
        Flash("ไม่พบรายการ หรือสถานะเปลี่ยนไปแล้ว (อาจมีผู้อื่นดำเนินการไปก่อน) กรุณาตรวจสอบอีกครั้ง", true);
        return RedirectToAction(action);
    }
}

// ======================================================================
// AccountController
// ======================================================================
public class AccountController : AppController
{

    //สร้าง
    private readonly Csi402dbContext _db;
    private readonly LoginThrottle _throttle;
    public AccountController(Csi402dbContext db, IMemoryCache cache)
    {
        _db = db;
        _throttle = new LoginThrottle(cache);
    }

        

  


    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null, string? tab = null)
    {
        var page = new AuthPageViewModel { ReturnUrl = returnUrl, ActiveTab = tab == "register" ? "register" : "login" };
        if (TempData["Success"] is string ok)
        {
            page.Message = ok;
            page.Login.Email = TempData["Email"] as string ?? "";
        }
        return View(page);
    }


    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login([Bind(Prefix = "Login")] LoginViewModel data, string? returnUrl)
    {
        var page = new AuthPageViewModel { Login = data, ReturnUrl = returnUrl };
        if (!ModelState.IsValid) return View(page);

        var email = data.Email.Trim().ToLowerInvariant();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (_throttle.IsBlocked(email, ip))
        {
            page.Message = "ลองเข้าสู่ระบบผิดหลายครั้งเกินไป กรุณารอ 15 นาทีแล้วลองใหม่";
            page.IsError = true;
            await AuditTrail.WriteAsync(HttpContext, "Account.Login", null, false, "ถูกระงับชั่วคราว: ลองผิดหลายครั้ง", null, email, data.Role);
            return View(page);
        }

        // SELECT * FROM users WHERE email = @email LIMIT 1  (+ JOIN roles)
        var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Email == email);


        if (user == null || !PasswordHelper.Verify(data.Password, user.PasswordHash))
        {
            _throttle.Fail(email, ip);
            await AuditTrail.WriteAsync(HttpContext, "Account.Login", null, false,
                user == null ? "ไม่พบบัญชีที่ใช้อีเมลนี้" : "รหัสผ่านไม่ถูกต้อง", user?.UserId, email, data.Role);
            page.Message = "อีเมลหรือรหัสผ่านไม่ถูกต้อง";
            page.IsError = true;
            return View(page);
        }

        // Part 49: ไม่ต้องเลือกบทบาท — เข้าสู่ระบบตามบทบาทของบัญชีนี้ (ผู้จัดการกำหนดบทบาทให้แต่ละบัญชีอยู่แล้ว)
        if (!RoleNames.IsKnown(user.Role.RoleName))
        {
            await AuditTrail.WriteAsync(HttpContext, "Account.Login", null, false, "บัญชีไม่มีบทบาทที่ระบบรู้จัก", user.UserId, email, user.Role.RoleName);
            page.Message = "บัญชีนี้ยังไม่ได้กำหนดบทบาทที่ใช้งานได้ กรุณาติดต่อผู้จัดการ";
            page.IsError = true;
            return View(page);
        }

        _throttle.Reset(email, ip);
        _db.LoginHistories.Add(new LoginHistory
        {
            UserId = user.UserId,
            Email = user.Email,
            LoggedInAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await SignInUserAsync(user);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        var (controller, action) = RoleNames.HomeFor(user.Role.RoleName);
        return RedirectToAction(action, controller);
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied() => View();

    /// <summary>
    /// Part 50: ตัวเลขสด (เรียกทุก 30 วินาทีจากหน้าเว็บ) — งานที่รอในเมนูของพนักงาน + การแจ้งเตือนที่ยังไม่อ่านของลูกค้า
    /// ใช้อัปเดตป้ายตัวเลขและเด้งบอก "มีงานใหม่" โดยไม่ต้องรีเฟรชหน้า · อ่านอย่างเดียว ไม่เปลี่ยนข้อมูล
    /// </summary>
    [Authorize, HttpGet("/live")]
    public async Task<IActionResult> Live()
    {
        Response.Headers.CacheControl = "no-store";
        var role = User.FindFirstValue(ClaimTypes.Role);
        if (role == RoleNames.Customer)
        {
            var uid = CurrentUserId ?? 0;
            var unread = await _db.Notifications.CountAsync(n => n.UserId == uid && !n.IsRead);
            var latest = unread == 0 ? null : await _db.Notifications.AsNoTracking().Where(n => n.UserId == uid && !n.IsRead)
                .OrderByDescending(n => n.NotificationId).Select(n => new { n.NotificationId, n.Title }).FirstOrDefaultAsync();
            return Json(new { unread, latestId = latest?.NotificationId, latest = latest?.Title });
        }
        return Json(new { menu = await BookingQueries.MenuCountsAsync(_db, role) });
    }

    //ดู
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register([Bind(Prefix = "Register")] RegisterViewModel data)
    {
        var page = new AuthPageViewModel { Register = data, ActiveTab = "register" };


        if (!ModelState.IsValid) return View("Login", page);

        var email = data.Email.Trim().ToLowerInvariant();
        var phone = data.Phone.Trim();


        var dup = await BookingQueries.FindDuplicateUserAsync(_db, email, phone);
        if (dup != null)
        {
            if (dup.Email == email) ModelState.AddModelError("Register.Email", "อีเมลนี้ถูกใช้สมัครแล้ว");
            else ModelState.AddModelError("Register.Phone", "เบอร์โทรนี้ถูกใช้สมัครแล้ว");
            return View("Login", page);
        }

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName == "customer");
        if (role == null)
        {
            role = new Role { RoleName = "customer" };
            _db.Roles.Add(role);
        }


        var newUser = new UserEntity
        {
            FirstName = data.FirstName.Trim(),
            LastName = data.LastName.Trim(),
            Phone = phone,
            Email = email,
            PasswordHash = PasswordHelper.Hash(data.Password), SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = role,
            IsMember = true,
            CreatedAt = pameka_7033_l005.Helpers.AppClock.Now
        };
        _db.Users.Add(newUser);

        try
        {
            await _db.SaveChangesAsync();   // INSERT INTO users (...) VALUES (...)
        }
        catch (DbUpdateException)
        {
            page.Message = "บันทึกข้อมูลไม่สำเร็จ (อีเมลหรือเบอร์อาจซ้ำ) กรุณาลองใหม่";
            page.IsError = true;
            return View("Login", page);
        }

        await AuditTrail.WriteAsync(HttpContext, "Account.Register", newUser.UserId, true, "สมัครสมาชิกใหม่", newUser.UserId, email, RoleNames.Customer);
        TempData["Success"] = "สมัครสมาชิกสำเร็จ กรุณาเข้าสู่ระบบ";
        TempData["Email"] = email;
        return RedirectToAction(nameof(Login));
    }


    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> UserList(string? q, string? role, int page = 1)
    {
        const int PageSize = 10;

        IQueryable<UserEntity> query = _db.Users.AsNoTracking();


        q = q?.Trim();
        if (!string.IsNullOrEmpty(q))
        {
            query = query.Where(u => u.FirstName.Contains(q) || u.LastName.Contains(q)
                                  || u.Email.Contains(q) || u.Phone.Contains(q));
        }

        if (role == "staff") query = query.Where(u => u.Role.RoleName != RoleNames.Customer);
        else if (RoleNames.IsKnown(role)) query = query.Where(u => u.Role.RoleName == role);
        else role = null;

        var total = await query.CountAsync();                     
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, totalPages);

        // SELECT ... FROM users JOIN roles ORDER BY user_id LIMIT 10 OFFSET ...
        var items = await query
            .OrderBy(u => u.UserId)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(u => new UserListItemViewModel
            {
                UserId = u.UserId,
                FullName = u.FirstName + " " + u.LastName,
                Email = u.Email,
                Phone = u.Phone,
                RoleName = u.Role.RoleName,
                IsMember = u.IsMember,
                CreatedAt = u.CreatedAt
            })
            .ToListAsync();

        ViewBag.MeId = CurrentUserId ?? 0;
        return View(new UserListPageViewModel
        {
            Items = items, Query = q, Role = role, Page = page, TotalPages = totalPages, TotalCount = total
        });
    }

    // ผู้จัดการแก้ข้อมูลผู้ใช้คนอื่น (ของตัวเองให้ไปที่หน้า "ตั้งค่า" ซึ่งต้องยืนยันรหัสเดิมเวลาเปลี่ยนรหัสผ่าน)
    [Authorize(Roles = RoleNames.Admin)]
    [HttpGet]
    public async Task<IActionResult> EditUser(int? userId)
    {
        if (userId == null) return RedirectToAction(nameof(UserList));
        if (userId == CurrentUserId) return RedirectToAction("Index", "Settings");

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId.Value);
        if (user == null)
        {
            Flash("ไม่พบผู้ใช้", true);
            return RedirectToAction(nameof(UserList));
        }

        return View(new EditUserViewModel
        {
            UserId = user.UserId, FirstName = user.FirstName, LastName = user.LastName, Phone = user.Phone, Email = user.Email,
            BirthDate = user.BirthDate, AnniversaryDate = user.AnniversaryDate, IsMember = user.IsMember
        });
    }

    [Authorize(Roles = RoleNames.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditUser(EditUserViewModel data)
    {
        if (data.UserId == CurrentUserId) return RedirectToAction("Index", "Settings");
        if (!ModelState.IsValid) return View(data);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == data.UserId);
        if (user == null)
        {
            Flash("ไม่พบผู้ใช้", true);
            return RedirectToAction(nameof(UserList));
        }

        var email = data.Email.Trim().ToLowerInvariant();
        var phone = data.Phone.Trim();
        var dup = await BookingQueries.FindDuplicateUserAsync(_db, email, phone, data.UserId);
        if (dup != null)
        {
            if (dup.Email == email) ModelState.AddModelError(nameof(data.Email), "อีเมลนี้ถูกใช้งานแล้ว");
            else ModelState.AddModelError(nameof(data.Phone), "เบอร์โทรนี้ถูกใช้งานแล้ว");
            return View(data);
        }

        user.ApplyProfile(data);
        user.IsMember = data.IsMember;
        if (!string.IsNullOrWhiteSpace(data.NewPassword)) user.SetPassword(data.NewPassword);

        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            ModelState.AddModelError("", "บันทึกไม่สำเร็จ (อีเมลหรือเบอร์โทรอาจซ้ำกับผู้ใช้อื่น)");
            return View(data);
        }

        Flash("บันทึกข้อมูลผู้ใช้แล้ว");
        return RedirectToAction(nameof(UserList));
    }

    [Authorize(Roles = RoleNames.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(int userId)    
    {

        // SELECT * FROM users WHERE user_id = @userId LIMIT 1
        var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null)
        {
            Flash("ไม่พบผู้ใช้ที่ต้องการลบ", true);
            return RedirectToAction(nameof(UserList));
        }

        // บัญชีพนักงานลบได้เฉพาะผู้จัดการที่หน้าจัดการพนักงาน (ห้ามลบตัวเองผ่านทางนี้)
        if (RoleNames.IsStaff(user.Role.RoleName)) return Forbid();


        if (!await _db.TryDeleteAsync(user))
        {
            Flash("ลบไม่สำเร็จ: ผู้ใช้นี้มีข้อมูลที่เกี่ยวข้อง (เช่น การจอง/การชำระเงิน)", true);
            return RedirectToAction(nameof(UserList));
        }

        Flash("ลบผู้ใช้สำเร็จ");
        return RedirectToAction(nameof(UserList));
    }



    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> LoginHistory(int page = 1)
    {
        const int PageSize = 30;
        var total = await _db.LoginHistories.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, totalPages);

        var items = await _db.LoginHistories.AsNoTracking()
            .OrderByDescending(h => h.LoggedInAtUtc).ThenByDescending(h => h.LoginHistoryId)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(h => new LoginHistoryRowViewModel
            {
                Id = h.LoginHistoryId, UserId = h.UserId, FullName = h.User.FirstName + " " + h.User.LastName,
                Email = h.Email, LoggedInAtUtc = h.LoggedInAtUtc
            }).ToListAsync();
        return View(new LoginHistoryPageViewModel { Items = items, Page = page, TotalPages = totalPages, TotalCount = total });
    }

    // ล้างประวัติเข้าสู่ระบบ: keepDays = 0 ล้างทั้งหมด, 30/90 = เก็บไว้เฉพาะช่วงล่าสุด
    // การล้างถูกบันทึกในบันทึกกิจกรรม (Audit) เสมอ จึงตรวจย้อนได้ว่าใครล้างเมื่อไร
    [Authorize(Roles = RoleNames.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearLoginHistory(int keepDays)
    {
        if (keepDays is not (0 or 30 or 90))
        {
            Flash("เลือกช่วงที่จะล้างไม่ถูกต้อง", true);
            return RedirectToAction(nameof(LoginHistory));
        }
        var q = _db.LoginHistories.AsQueryable();
        if (keepDays > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-keepDays);
            q = q.Where(h => h.LoggedInAtUtc < cutoff);
        }
        var n = await q.ExecuteDeleteAsync();
        Flash(n == 0 ? (keepDays > 0 ? $"ไม่มีประวัติที่เก่ากว่า {keepDays} วัน — เลือก \"ทั้งหมด\" ถ้าต้องการล้างทุกรายการ" : "ไม่มีประวัติที่ต้องล้าง")
            : keepDays == 0 ? $"ล้างประวัติเข้าสู่ระบบทั้งหมดแล้ว {n:N0} รายการ"
            : $"ล้างประวัติที่เก่ากว่า {keepDays} วันแล้ว {n:N0} รายการ");
        return RedirectToAction(nameof(LoginHistory));
    }

    // ---------------------------------------------------------------- Part 51: ลืมรหัสผ่าน (รีเซ็ตผ่านอีเมล)
    [AllowAnonymous, HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    /// <summary>
    /// ส่งลิงก์ตั้งรหัสใหม่ทางอีเมล — ตอบข้อความเดียวกันเสมอไม่ว่าจะมีบัญชีหรือไม่ (กันการไล่เดาอีเมลของสมาชิก)
    /// จำกัด 3 ครั้ง / อีเมล และ 10 ครั้ง / IP ใน 15 นาที · ลิงก์ใช้ได้ครั้งเดียว หมดอายุ 30 นาที
    /// </summary>
    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel f, [FromServices] IMemoryCache cache)
    {
        if (!ModelState.IsValid) return View(f);
        var email = f.Email.Trim().ToLowerInvariant();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "-";
        int Hit(string key, int max)
        {
            var n = cache.TryGetValue(key, out int c) ? c + 1 : 1;
            cache.Set(key, n, TimeSpan.FromMinutes(15));
            return n > max ? -1 : n;
        }
        if (Hit("reset-ip:" + ip, 10) < 0 || Hit("reset-mail:" + email, 3) < 0)
        {
            f.Error = "ขอรีเซ็ตรหัสผ่านบ่อยเกินไป กรุณารอ 15 นาทีแล้วลองใหม่";
            return View(f);
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user != null)
        {
            var token = await PasswordResets.CreateAsync(_db, user.UserId, ip);
            var link = Url.Action(nameof(ResetPassword), "Account", new { token }, Request.Scheme) ?? "";
            Mailer.Queue(_db, user.Email, "ตั้งรหัสผ่านใหม่ — AurelleStay",
                $"เรียนคุณ{user.FirstName}\nมีคำขอตั้งรหัสผ่านใหม่สำหรับบัญชีนี้ กดปุ่มด้านล่างภายใน 30 นาทีเพื่อตั้งรหัสผ่านใหม่ (ใช้ได้ครั้งเดียว)\nถ้าคุณไม่ได้ขอ ไม่ต้องทำอะไร รหัสผ่านเดิมยังใช้ได้ตามปกติ",
                link, user.UserId);
            await _db.SaveChangesAsync();
        }
        await AuditTrail.WriteAsync(HttpContext, "Account.ForgotPassword", user?.UserId, user != null,
            user != null ? "ส่งลิงก์ตั้งรหัสผ่านใหม่ทางอีเมล" : "ไม่พบบัญชีที่ใช้อีเมลนี้", user?.UserId, email);
        return View(new ForgotPasswordViewModel { Email = email, Sent = true });
    }

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> ResetPassword(string? token)
    {
        var r = await PasswordResets.FindValidAsync(_db, token);
        if (r == null) return View(new ResetPasswordViewModel());
        return View(new ResetPasswordViewModel { Token = token!, Email = r.User.Email });
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel f)
    {
        var r = await PasswordResets.FindValidAsync(_db, f.Token);
        if (r == null) return View(new ResetPasswordViewModel());
        f.Email = r.User.Email;
        if (!ModelState.IsValid) return View(f);

        r.UsedAt = AppClock.Now;
        r.User.PasswordHash = PasswordHelper.Hash(f.NewPassword);
        r.User.SecurityStamp = Guid.NewGuid().ToString("N");   // ออกจากระบบทุกอุปกรณ์ที่เคยล็อกอินไว้
        Notify.Add(_db, r.UserId, "รหัสผ่านของคุณถูกเปลี่ยนแล้ว",
            "รหัสผ่านถูกตั้งใหม่ผ่านลิงก์ลืมรหัสผ่าน ถ้าไม่ใช่คุณ กรุณาติดต่อโรงแรมทันที", null);
        await _db.SaveChangesAsync();
        _throttle.Reset(r.User.Email, HttpContext.Connection.RemoteIpAddress?.ToString());
        await AuditTrail.WriteAsync(HttpContext, "Account.ResetPassword", r.UserId, true, "ตั้งรหัสผ่านใหม่ผ่านลิงก์อีเมล", r.UserId, r.User.Email);
        TempData["Success"] = "ตั้งรหัสผ่านใหม่แล้ว กรุณาเข้าสู่ระบบด้วยรหัสผ่านใหม่";
        TempData["Email"] = r.User.Email;
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }
}

// ======================================================================
// SettingsController
// ======================================================================
/// <summary>
/// หน้า "ตั้งค่า" ของผู้ใช้ทุกบทบาท (แก้ได้เฉพาะของตัวเอง — id มาจากคุกกี้ล็อกอินเสมอ ไม่รับจากฟอร์ม):
/// บัญชี / ความปลอดภัย / การแสดงผล / ข้อมูลของฉัน
/// </summary>
[Authorize]
public class SettingsController : AppController
{
    private static readonly string[] Tabs = { "profile", "security", "appearance", "data" };
    private readonly Csi402dbContext _db;
    private readonly LoginThrottle _throttle;

    public SettingsController(Csi402dbContext db, IMemoryCache cache)
    {
        _db = db;
        _throttle = new LoginThrottle(cache);
    }

    private IActionResult ToTab(string tab) => RedirectToAction(nameof(Index), new { tab });
    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    [HttpGet]
    public async Task<IActionResult> Index(string? tab)
    {
        var uid = CurrentUserId ?? 0;
        var u = await _db.Users.AsNoTracking().Include(x => x.Role).FirstOrDefaultAsync(x => x.UserId == uid);
        if (u == null) return Forbid();

        return View(new SettingsPageViewModel
        {
            Tab = Tabs.Contains(tab ?? "") ? tab! : "profile",
            FullName = u.FirstName + " " + u.LastName,
            RoleName = u.Role.RoleName, IsMember = u.IsMember, CreatedAt = u.CreatedAt,
            Profile = new ProfileEditViewModel
            {
                FirstName = u.FirstName, LastName = u.LastName, Phone = u.Phone, Email = u.Email,
                BirthDate = u.BirthDate, AnniversaryDate = u.AnniversaryDate
            },
            RecentLogins = await _db.LoginHistories.AsNoTracking().Where(h => h.UserId == uid)
                .OrderByDescending(h => h.LoggedInAtUtc).Take(8).Select(h => h.LoggedInAtUtc).ToListAsync(),
            BookingCount = await _db.Bookings.CountAsync(b => b.UserId == uid),
            Ui = UiPrefs.From(Request)
        });
    }

    // ---------------------------------------------------------------- บัญชี
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProfile(ProfileEditViewModel f)
    {
        if (!ModelState.IsValid) { FlashModelErrors(); return ToTab("profile"); }

        var uid = CurrentUserId ?? 0;
        var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == uid);
        if (user == null) return Forbid();

        var email = f.Email.Trim().ToLowerInvariant();
        var phone = f.Phone.Trim();
        var dup = await BookingQueries.FindDuplicateUserAsync(_db, email, phone, uid);
        if (dup != null)
        {
            Flash(dup.Email == email ? "อีเมลนี้ถูกใช้งานแล้ว" : "เบอร์โทรนี้ถูกใช้งานแล้ว", true);
            return ToTab("profile");
        }

        // วันเกิด/วันครบรอบใช้รับส่วนลด: ลูกค้าตั้งเองได้ครั้งเดียว แก้ภายหลังต้องให้โรงแรมแก้ (กันเปลี่ยนวันเพื่อรับส่วนลดซ้ำ)
        var locked = false;
        if (user.Role.RoleName == RoleNames.Customer)
        {
            if (user.BirthDate != null && f.BirthDate != user.BirthDate) { f.BirthDate = user.BirthDate; locked = true; }
            if (user.AnniversaryDate != null && f.AnniversaryDate != user.AnniversaryDate) { f.AnniversaryDate = user.AnniversaryDate; locked = true; }
        }
        user.ApplyProfile(f);
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException) { Flash("บันทึกไม่สำเร็จ (อีเมลหรือเบอร์โทรอาจซ้ำกับผู้ใช้อื่น)", true); return ToTab("profile"); }

        await SignInUserAsync(user);   // ชื่อ/อีเมลในคุกกี้ให้ตรงกับข้อมูลใหม่
        Flash(locked ? "บันทึกข้อมูลแล้ว · วันเกิด/วันครบรอบที่ตั้งไว้แล้วแก้เองไม่ได้ หากต้องการแก้ไขกรุณาติดต่อโรงแรม" : "บันทึกข้อมูลแล้ว");
        return ToTab("profile");
    }

    // ---------------------------------------------------------------- ความปลอดภัย
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel f)
    {
        if (!ModelState.IsValid) { FlashModelErrors(); return ToTab("security"); }

        var uid = CurrentUserId ?? 0;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == uid);
        if (user == null) return Forbid();

        // ใส่รหัสปัจจุบันผิดซ้ำ ๆ นับเข้าตัวจำกัดเดียวกับหน้าล็อกอิน (กันคนที่ได้เครื่อง/เซสชันไปเดารหัส)
        var key = "pw:" + user.Email;
        if (_throttle.IsBlocked(key, Ip)) { Flash("ใส่รหัสผ่านปัจจุบันผิดหลายครั้งเกินไป กรุณารอ 15 นาทีแล้วลองใหม่", true); return ToTab("security"); }
        if (!PasswordHelper.Verify(f.CurrentPassword, user.PasswordHash))
        {
            _throttle.Fail(key, Ip);
            Flash("รหัสผ่านปัจจุบันไม่ถูกต้อง", true);
            return ToTab("security");
        }
        if (f.NewPassword == f.CurrentPassword) { Flash("รหัสผ่านใหม่ต้องไม่ซ้ำกับรหัสผ่านเดิม", true); return ToTab("security"); }

        user.SetPassword(f.NewPassword);
        await _db.SaveChangesAsync();
        _throttle.Reset(key, Ip);
        await SignInUserAsync(await _db.Users.Include(u => u.Role).FirstAsync(u => u.UserId == uid));   // เซสชันนี้ใช้ต่อได้ อุปกรณ์อื่นถูกออกจากระบบ
        Flash("เปลี่ยนรหัสผ่านแล้ว อุปกรณ์อื่นที่เข้าสู่ระบบไว้ถูกออกจากระบบแล้ว");
        return ToTab("security");
    }

    /// <summary>เปลี่ยนตราประจำเซสชัน: เซสชันบนอุปกรณ์อื่นทั้งหมดถูกตัดในคำขอถัดไป เครื่องนี้ลงชื่อใหม่ด้วยตราใหม่</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SignOutOthers()
    {
        var uid = CurrentUserId ?? 0;
        var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == uid);
        if (user == null) return Forbid();
        user.RotateStamp();
        await _db.SaveChangesAsync();
        await SignInUserAsync(user);
        Flash("ออกจากระบบอุปกรณ์อื่นทั้งหมดแล้ว");
        return ToTab("security");
    }

    // ---------------------------------------------------------------- การแสดงผล
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult SaveAppearance(string? font, bool reduceMotion)
    {
        var prefs = UiPrefs.Create(font, reduceMotion);   // ค่านอกรายการอนุญาต → ค่าเริ่มต้น
        Response.Cookies.Append(UiPrefs.CookieName, prefs.ToCookie(), new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1), HttpOnly = true, IsEssential = true, SameSite = SameSiteMode.Lax, Path = "/"
        });
        Flash("บันทึกการแสดงผลแล้ว");
        return ToTab("appearance");
    }

    // ---------------------------------------------------------------- ข้อมูลของฉัน
    /// <summary>ดาวน์โหลดข้อมูลของตัวเองทั้งหมดเป็น JSON (ไม่รวมรหัสผ่าน/ข้อมูลของคนอื่น)</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportData()
    {
        var uid = CurrentUserId ?? 0;
        var u = await _db.Users.AsNoTracking().Include(x => x.Role).FirstOrDefaultAsync(x => x.UserId == uid);
        if (u == null) return Forbid();

        var bookings = await _db.Bookings.AsNoTracking().Where(b => b.UserId == uid).OrderBy(b => b.BookingId).Select(b => new
        {
            b.BookingId, b.Status, b.CheckIn, b.CheckOut, b.BookedAt, b.Subtotal, b.DiscountTotal, b.NetTotal,
            Promotion = b.Promo != null ? b.Promo.PromoName : null,
            Rooms = b.BookingRooms.Select(r => new { r.Room.RoomNo, Type = r.Room.Type.TypeName, r.PricePerNight, r.Nights }).ToList(),
            Services = b.BookingServices.Select(s => new { s.Room.RoomNo, Service = s.Service.ServiceName, s.Quantity, s.UnitPrice }).ToList(),
            Payments = b.Payments.Select(p => new { p.PaymentType, p.Method, p.Status, p.Amount, p.PaidAt }).ToList(),
            Review = b.Review != null ? new { b.Review.Rating, b.Review.Comment, b.Review.CreatedAt } : null
        }).ToListAsync();
        var logins = await _db.LoginHistories.AsNoTracking().Where(h => h.UserId == uid).OrderByDescending(h => h.LoggedInAtUtc).Take(200).Select(h => h.LoggedInAtUtc).ToListAsync();

        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            exportedAtUtc = DateTime.UtcNow,
            profile = new { u.UserId, u.FirstName, u.LastName, u.Email, u.Phone, Role = u.Role.RoleName, u.IsMember, u.BirthDate, u.AnniversaryDate, u.CreatedAt },
            bookings,
            loginsUtc = logins
        }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        Response.Headers.CacheControl = "no-store";
        return File(json, "application/json; charset=utf-8", $"aurellestay_my_data_{pameka_7033_l005.Helpers.AppClock.Today:yyyyMMdd}.json");
    }

    /// <summary>ลูกค้าลบบัญชีตัวเอง: ต้องยืนยันรหัสผ่าน และลบไม่ได้ถ้ามีประวัติการจอง (ต้องเก็บให้ฝ่ายบัญชี) พนักงานลบไม่ได้</summary>
    [Authorize(Roles = RoleNames.Customer)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAccount(string? password)
    {
        var uid = CurrentUserId ?? 0;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == uid);
        if (user == null) return Forbid();

        var key = "pw:" + user.Email;
        if (_throttle.IsBlocked(key, Ip)) { Flash("ใส่รหัสผ่านผิดหลายครั้งเกินไป กรุณารอ 15 นาทีแล้วลองใหม่", true); return ToTab("data"); }
        if (string.IsNullOrEmpty(password) || !PasswordHelper.Verify(password, user.PasswordHash))
        {
            _throttle.Fail(key, Ip);
            Flash("รหัสผ่านไม่ถูกต้อง ไม่ได้ลบบัญชี", true);
            return ToTab("data");
        }
        if (await _db.Bookings.AnyAsync(b => b.UserId == uid))
        {
            Flash("ลบบัญชีไม่ได้ เพราะมีประวัติการจองที่ต้องเก็บไว้ (ติดต่อเจ้าหน้าที่หากต้องการความช่วยเหลือ)", true);
            return ToTab("data");
        }
        if (!await _db.TryDeleteAsync(user)) { Flash("ลบบัญชีไม่สำเร็จ", true); return ToTab("data"); }

        _throttle.Reset(key, Ip);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        Flash("ลบบัญชีของคุณแล้ว");
        return RedirectToAction("Index", "Home");
    }
}
