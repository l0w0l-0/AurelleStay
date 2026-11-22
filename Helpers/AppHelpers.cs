using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using pameka_7033_l005.Models;
using pameka_7033_l005.ViewModels;

namespace pameka_7033_l005.Helpers;

// ตัวช่วยของระบบ: ตั้งค่าแอป/ความปลอดภัย, บทบาท, รหัสผ่าน, จำกัดการเดารหัส, ข้อมูลผู้ใช้, การแสดงผล

// ======================================================================
// AppSetup
// ======================================================================
/// <summary>
/// การตั้งค่าความปลอดภัยของแอปที่เดียว (Program.cs และชุดทดสอบเรียกใช้ตัวเดียวกัน)
/// - "ปิดไว้ก่อน": ทุกหน้าต้องล็อกอิน เว้นแต่ใส่ [AllowAnonymous] ชัดเจน (กันลืมใส่สิทธิ์หน้าใหม่)
/// - ฟอร์ม POST ทุกอันตรวจ anti-forgery token อัตโนมัติ
/// - คุกกี้: HttpOnly, SameSite=Lax, ตรวจซ้ำกับฐานข้อมูลทุกคำขอ
/// </summary>
public static class AppSetup
{
    public static void AddAppServices(this IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        services.AddControllersWithViews(o =>
        {
            o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
            o.Filters.Add<AuditFilter>();   // บันทึกกิจกรรมทุกคำสั่งที่เปลี่ยนข้อมูล (POST) ของผู้ใช้ที่ล็อกอิน
        });
        services.AddMemoryCache();
        AppClock.Configure(config["App:TimeZone"]);

        // ส่งตัวอักษรไทยใน HTML ตรง ๆ (เดิม Razor แปลงเป็น &#xE2D; ทุกตัว ทำให้หน้าใหญ่ขึ้นหลายเท่า) — ยังเข้ารหัส < > & " ' ตามปกติ
        services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(o =>
            o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));

        // ไฟล์สลิปเก็บนอก wwwroot (เปิดได้ผ่าน /Booking/Slip ตามสิทธิ์เท่านั้น)
        UploadStore.SlipRoot = config["Uploads:SlipsPath"] is { Length: > 0 } sp ? sp : Path.Combine(env.ContentRootPath, "App_Data", "slips");
        UploadStore.RoomImageRoot = Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), "images", "rooms");
        UploadStore.ServiceImageRoot = Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), "images", "services");

        // ยกเลิกการจองค้างชำระอัตโนมัติ + ตรวจสุขภาพระบบที่ /health (ตอบ Healthy/Unhealthy เท่านั้น ไม่เปิดเผยรายละเอียด)
        services.AddHostedService<BookingExpiryService>();
        services.AddHostedService<OutboxSender>();
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

        // เก็บคีย์เข้ารหัสคุกกี้ลงดิสก์: รีสตาร์ท/deploy ใหม่แล้วผู้ใช้ไม่หลุดล็อกอิน และฟอร์มที่เปิดค้างไว้ยังส่งได้
        var keysPath = config["DataProtection:KeysPath"];
        if (string.IsNullOrWhiteSpace(keysPath)) keysPath = Path.Combine(env.ContentRootPath, "App_Data", "keys");
        var dp = services.AddDataProtection().SetApplicationName("AurelleStay").PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        if (OperatingSystem.IsWindows()) dp.ProtectKeysWithDpapi(protectToLocalMachine: true);

        // อยู่หลัง reverse proxy (nginx/IIS ARR): ใช้ IP จริงของผู้ใช้และรู้ว่าเป็น https
        // ค่าเริ่มต้นเชื่อเฉพาะ proxy บนเครื่องเดียวกัน, proxy เครื่องอื่นใส่ IP ที่ Proxy:KnownProxies คั่นด้วย ,
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var ip in (config["Proxy:KnownProxies"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (System.Net.IPAddress.TryParse(ip, out var addr)) o.KnownProxies.Add(addr);
        });
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/Account/Login";
                options.AccessDeniedPath = "/Account/AccessDenied";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;   // เครื่องจริงส่งคุกกี้ผ่าน https เท่านั้น

                // ตรวจซ้ำทุกคำขอว่าผู้ใช้ยังมีอยู่และบทบาทในคุกกี้ตรงกับฐานข้อมูล
                // (ถูกลบ/ถูกเปลี่ยนบทบาท = สิทธิ์เดิมใช้ไม่ได้ทันที ต้องล็อกอินใหม่)
                options.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = async ctx =>
                    {
                        if (Path.HasExtension(ctx.Request.Path)) return;   // ไฟล์ static (css/js/img) ไม่ต้องคิวรีฐานข้อมูล

                        var role = ctx.Principal?.FindFirstValue(ClaimTypes.Role);
                        var valid = false;
                        if (int.TryParse(ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) && role != null)
                        {
                            var db = ctx.HttpContext.RequestServices.GetRequiredService<Csi402dbContext>();
                            // ตราว่าง = บัญชี/คุกกี้จากก่อนมีระบบนี้ (ไม่บังคับออกจากระบบตอนอัปเกรด)
                            var stamp = ctx.Principal?.FindFirstValue(SessionStamp.ClaimType) ?? "";
                            valid = await db.Users.AsNoTracking().AnyAsync(u => u.UserId == uid && u.Role.RoleName == role
                                                                              && (u.SecurityStamp ?? "") == stamp);
                        }
                        if (!valid)
                        {
                            ctx.RejectPrincipal();
                            await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions
                                .SignOutAsync(ctx.HttpContext, CookieAuthenticationDefaults.AuthenticationScheme);
                        }
                    }
                };
            });
    }

    /// <summary>หัวความปลอดภัยพื้นฐาน (กัน clickjacking / MIME sniffing / ลดการรั่วของ referrer)</summary>
    /// <summary>รหัสสถานะ เช่น 404 แสดงหน้าภาษาไทยของเว็บ (แทนหน้าว่าง)</summary>
    public static void UseErrorPages(this WebApplication app) => app.UseStatusCodePagesWithReExecute("/Home/Error", "?code={0}");

    public static void UseSecurityHeaders(this WebApplication app) => app.Use(async (ctx, next) =>
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        // CSP ใส่เฉพาะหน้า HTML (ไม่ใส่กับไฟล์สลิป PDF/รูป เพื่อให้ตัวแสดงไฟล์ของเบราว์เซอร์ทำงานได้)
        ctx.Response.OnStarting(() =>
        {
            if (ctx.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
                ctx.Response.Headers["Content-Security-Policy"] =
                    "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; " +
                    "font-src 'self'; script-src 'self' 'unsafe-inline'; object-src 'none'; " +
                    "base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
            return Task.CompletedTask;
        });
        await next();
    });
}

// ======================================================================
// RoleNames
// ======================================================================
/// <summary>ชื่อบทบาท (roles.role_name) และข้อมูลที่เกี่ยวข้อง ใช้ร่วมกันทั้งระบบ</summary>
public static class RoleNames
{
    public const string Admin = "admin";               // ผู้จัดการ / แอดมิน
    public const string Accounting = "accounting";     // ฝ่ายบัญชี
    public const string FrontDesk = "frontdesk";       // ฝ่ายบริการ / พนักงานต้อนรับ
    public const string Delivery = "delivery";         // ฝ่ายส่งของ
    public const string Housekeeping = "housekeeping"; // ฝ่ายทำความสะอาด
    public const string Customer = "customer";         // ลูกค้า

    /// <summary>ทุกบทบาท (ลำดับนี้ใช้แสดงในหน้าเข้าสู่ระบบ)</summary>
    public static readonly IReadOnlyList<string> All =
        [Customer, Admin, Accounting, FrontDesk, Delivery, Housekeeping];

    /// <summary>เฉพาะบทบาทพนักงาน (ผู้จัดการออกบัญชีให้)</summary>
    public static readonly IReadOnlyList<string> Staff =
        [Admin, Accounting, FrontDesk, Delivery, Housekeeping];

    public static bool IsKnown(string? role) =>
        role != null && All.Contains(role, StringComparer.OrdinalIgnoreCase);

    public static bool IsStaff(string? role) =>
        role != null && Staff.Contains(role, StringComparer.OrdinalIgnoreCase);

    public static string Label(string? role) => role?.ToLowerInvariant() switch
    {
        Admin => "ผู้จัดการ",
        Accounting => "ฝ่ายบัญชี",
        FrontDesk => "ฝ่ายบริการ",
        Delivery => "ฝ่ายส่งของ",
        Housekeeping => "ฝ่ายทำความสะอาด",
        Customer => "ลูกค้า",
        _ => role ?? ""
    };

    /// <summary>หน้าแรกหลังเข้าสู่ระบบของแต่ละบทบาท</summary>
    public static (string Controller, string Action) HomeFor(string? role) => role?.ToLowerInvariant() switch
    {
        Admin => ("Admin", "Index"),
        Accounting => ("Accounting", "Index"),
        FrontDesk => ("FrontDesk", "Index"),
        Delivery => ("Delivery", "Index"),
        Housekeeping => ("Housekeeping", "Index"),
        Customer => ("Member", "Index"),
        _ => ("Home", "Index")
    };

    /// <summary>เมนูข้างของพนักงานแต่ละฝ่าย: (ชื่อ action ใน controller ของฝ่ายนั้น, ข้อความ) — รายการแรกคือหน้าหลัก</summary>
    public static IReadOnlyList<(string Action, string Label)> Menu(string? role) => role?.ToLowerInvariant() switch
    {
        Accounting => [("Index", "ตรวจสอบการชำระเงิน"), ("Refunds", "คำขอคืนเงิน"), ("History", "ประวัติการเงิน"), ("Expenses", "รายจ่าย / กำไร")],
        FrontDesk => [("Index", "เช็คอิน / เช็คเอาท์"), ("WalkIn", "จองห้องให้ลูกค้า"), ("Activities", "กิจกรรมวันนี้"), ("Availability", "ปฏิทินห้องว่าง"), ("Rooms", "สถานะห้อง")],
        Delivery => [("Index", "ห้องที่ต้องส่ง"), ("Done", "ห้องที่ส่งสำเร็จ")],
        Housekeeping => [("Index", "ห้องที่ต้องทำความสะอาด"), ("Done", "ห้องที่ทำไปแล้ว"), ("Issues", "แจ้งซ่อม")],
        _ => []
    };
}

// ======================================================================
// PasswordHelper
// ======================================================================
public static class PasswordHelper
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"PBKDF2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "PBKDF2" || !int.TryParse(parts[1], out var iter)) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException) { return false; }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);   
    }

    /// <summary>สร้างรหัสผ่านสุ่ม (ตัดตัวอักษรที่สับสนง่าย เช่น 0/O 1/l/I) สำหรับผู้จัดการออกให้พนักงาน</summary>
    public static string Generate(int length = 10)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        return string.Create(length, chars, static (span, set) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = set[RandomNumberGenerator.GetInt32(set.Length)];
        });
    }
}

// ======================================================================
// PasswordPolicyAttribute
// ======================================================================
/// <summary>นโยบายรหัสผ่านที่เดียว: ยาวอย่างน้อย 8 ตัว และมีทั้งตัวอักษรและตัวเลข (ค่าว่าง = ผ่าน ให้ใช้ [Required] แยกต่างหาก)</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PasswordPolicyAttribute : ValidationAttribute
{
    public const string Message = "รหัสผ่านต้องยาวอย่างน้อย 8 ตัว และมีทั้งตัวอักษรและตัวเลข";
    public static bool IsStrong(string s) => s.Length >= 8 && s.Any(char.IsLetter) && s.Any(char.IsDigit);

    protected override ValidationResult? IsValid(object? value, ValidationContext ctx) =>
        value is not string s || s.Length == 0 || IsStrong(s) ? ValidationResult.Success : new ValidationResult(Message);
}

// ======================================================================
// LoginThrottle
// ======================================================================
/// <summary>จำกัดการเดารหัสผ่าน: ผิดครบ 5 ครั้งใน 15 นาที (ต่อ อีเมล+IP) จะถูกล็อก 15 นาที นับจากครั้งที่ผิดครั้งที่ 5</summary>
public sealed class LoginThrottle
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private sealed class Entry { public int Count; }

    private readonly IMemoryCache _cache;
    public LoginThrottle(IMemoryCache cache) => _cache = cache;

    private static string K(string email, string? ip) => $"login-fail:{email.Trim().ToLowerInvariant()}|{ip}";

    public bool IsBlocked(string email, string? ip) => _cache.TryGetValue(K(email, ip), out Entry? e) && e!.Count >= MaxFailures;

    public void Fail(string email, string? ip)
    {
        var key = K(email, ip);
        var e = _cache.TryGetValue(key, out Entry? old) ? old! : new Entry();
        e.Count++;
        _cache.Set(key, e, Window);   // ผิดครั้งล่าสุดเริ่มนับ 15 นาทีใหม่ (ครั้งที่ 5 = เริ่มล็อก)
    }

    public void Reset(string email, string? ip) => _cache.Remove(K(email, ip));
}

// ======================================================================
// UserProfile
// ======================================================================
public static class UserProfile
{
    /// <summary>ลบผู้ใช้พร้อมประวัติเข้าสู่ระบบในธุรกรรมเดียว คืน false ถ้ามีข้อมูลอ้างอิง (เช่น การจอง) ลบไม่ได้ ข้อมูลไม่เปลี่ยน</summary>
    public static async Task<bool> TryDeleteAsync(this Csi402dbContext db, User user)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            await db.LoginHistories.Where(h => h.UserId == user.UserId).ExecuteDeleteAsync();
            db.Users.Remove(user);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync();
            db.Entry(user).State = EntityState.Unchanged;
            return false;
        }
    }

    /// <summary>ใส่ข้อมูลส่วนตัวจากฟอร์มลงผู้ใช้ (ใช้ร่วมกันระหว่างหน้า "ตั้งค่า" ของตัวเอง และผู้จัดการแก้ให้ผู้อื่น)</summary>
    public static void ApplyProfile(this User user, ProfileEditViewModel d)
    {
        user.FirstName = d.FirstName.Trim();
        user.LastName = d.LastName.Trim();
        user.Phone = d.Phone.Trim();
        user.Email = d.Email.Trim().ToLowerInvariant();
        user.BirthDate = d.BirthDate;
        user.AnniversaryDate = d.AnniversaryDate;
    }
}

// ======================================================================
// UiPrefs
// ======================================================================
/// <summary>
/// การตั้งค่าการแสดงผลของผู้ใช้ (ขนาดตัวอักษร / ลดการเคลื่อนไหว) เก็บในคุกกี้ของเบราว์เซอร์ ไม่ต้องใช้ตารางใหม่
/// อ่านกลับมาต้องผ่านรายการค่าที่อนุญาตเสมอ — ค่าแปลกปลอมในคุกกี้จะกลายเป็นค่าเริ่มต้น ไม่ถูกพิมพ์ลงหน้าเว็บ
/// </summary>
public readonly record struct UiPrefs(string Font, bool ReduceMotion)
{
    public const string CookieName = "as_ui";
    public static readonly IReadOnlyList<string> Fonts = new[] { "md", "lg", "xl" };
    public static UiPrefs Default => new("md", false);

    public string Motion => ReduceMotion ? "reduce" : "normal";
    public string ToCookie() => $"{Font}|{Motion}";

    public static UiPrefs Create(string? font, bool reduceMotion) => new(Fonts.Contains(font ?? "") ? font! : "md", reduceMotion);

    public static UiPrefs Parse(string? raw)
    {
        var parts = (raw ?? "").Split('|');
        return parts.Length == 2 ? Create(parts[0], parts[1] == "reduce") : Default;
    }

    public static UiPrefs From(HttpRequest request) => Parse(request.Cookies[CookieName]);

    public static string FontLabel(string font) => font switch { "lg" => "ใหญ่", "xl" => "ใหญ่มาก", _ => "ปกติ" };
}

/// <summary>/health: เชื่อมต่อฐานข้อมูลได้หรือไม่ (ใช้กับ load balancer / ระบบเฝ้าระวัง)</summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly Csi402dbContext _db;
    public DatabaseHealthCheck(Csi402dbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try { return await _db.Database.CanConnectAsync(ct) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy(); }
        catch { return HealthCheckResult.Unhealthy(); }
    }
}

/// <summary>
/// รับไฟล์อัปโหลด: ตรวจขนาดและ "เนื้อไฟล์จริง" (ไม่เชื่อนามสกุล/Content-Type ที่ส่งมา) แล้วตั้งชื่อใหม่แบบสุ่ม
/// สลิป: jpg/png/webp/pdf ไม่เกิน 5 MB เก็บใน App_Data/slips | รูปห้อง: jpg/png/webp ไม่เกิน 3 MB เก็บใน wwwroot/images/rooms
/// </summary>
public static class UploadStore
{
    public static string SlipRoot { get; set; } = Path.Combine(AppContext.BaseDirectory, "App_Data", "slips");
    public static string RoomImageRoot { get; set; } = Path.Combine(AppContext.BaseDirectory, "wwwroot", "images", "rooms");
    public static string ServiceImageRoot { get; set; } = Path.Combine(AppContext.BaseDirectory, "wwwroot", "images", "services");
    public const long SlipMaxBytes = 5 * 1024 * 1024, ImageMaxBytes = 3 * 1024 * 1024;

    private static readonly Dictionary<string, string> Types = new()
    {
        [".jpg"] = "image/jpeg", [".png"] = "image/png", [".webp"] = "image/webp", [".pdf"] = "application/pdf"
    };

    /// <summary>ดูนามสกุลจากเนื้อไฟล์ (magic bytes) คืน null ถ้าไม่ใช่ชนิดที่รับ</summary>
    private static string? Sniff(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return ".jpg";
        if (h.Length >= 8 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47) return ".png";
        if (h.Length >= 12 && h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F' && h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P') return ".webp";
        if (h.Length >= 5 && h[0] == '%' && h[1] == 'P' && h[2] == 'D' && h[3] == 'F' && h[4] == '-') return ".pdf";
        return null;
    }

    public static Task<(string? Name, string? Error)> SaveSlipAsync(IFormFile? f) => SaveAsync(f, SlipRoot, SlipMaxBytes, allowPdf: true, "สลิป");
    public static Task<(string? Name, string? Error)> SaveRoomImageAsync(IFormFile? f) => SaveAsync(f, RoomImageRoot, ImageMaxBytes, allowPdf: false, "รูปห้อง");
    public static Task<(string? Name, string? Error)> SaveServiceImageAsync(IFormFile? f) => SaveAsync(f, ServiceImageRoot, ImageMaxBytes, allowPdf: false, "รูปบริการ");

    private static async Task<(string? Name, string? Error)> SaveAsync(IFormFile? f, string root, long max, bool allowPdf, string label)
    {
        if (f == null || f.Length == 0) return (null, $"กรุณาแนบไฟล์{label}");
        if (f.Length > max) return (null, $"ไฟล์{label}ใหญ่เกิน {max / 1024 / 1024} MB");

        var head = new byte[12];
        await using (var peek = f.OpenReadStream()) { var n = await peek.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false); head = head[..n]; }
        var ext = Sniff(head);
        if (ext == null || (ext == ".pdf" && !allowPdf))
            return (null, allowPdf ? "รับเฉพาะไฟล์รูป JPG / PNG / WEBP หรือ PDF" : "รับเฉพาะไฟล์รูป JPG / PNG / WEBP");

        Directory.CreateDirectory(root);
        var name = $"{Guid.NewGuid():N}{ext}";
        await using (var dst = new FileStream(Path.Combine(root, name), FileMode.CreateNew))
            await f.CopyToAsync(dst);
        return (name, null);
    }

    /// <summary>ชื่อไฟล์ที่ระบบสร้างเท่านั้น (กันการส่ง ../ หรือชื่ออื่นเข้ามา)</summary>
    public static bool IsSafeName(string? name) =>
        name != null && name.Length is > 32 and <= 40 && name[..32].All(Uri.IsHexDigit) && Types.ContainsKey(name[32..]);

    public static (string Path, string ContentType)? OpenSlip(string? name)
    {
        if (!IsSafeName(name)) return null;
        var path = Path.Combine(SlipRoot, name!);
        return File.Exists(path) ? (path, Types[name![32..]]) : null;
    }

    public static void DeleteSlip(string? name) { if (IsSafeName(name)) TryDelete(Path.Combine(SlipRoot, name!)); }

    /// <summary>ลบรูปห้องเดิมที่ระบบเคยอัปโหลด (ไม่แตะรูปตั้งต้นที่มากับโปรเจกต์)</summary>
    public static void DeleteRoomImage(string? url)
    {
        var name = url?.Split('/').LastOrDefault();
        if (url != null && url.StartsWith("/images/rooms/") && IsSafeName(name)) TryDelete(Path.Combine(RoomImageRoot, name!));
    }

    /// <summary>ลบรูปบริการที่ระบบเคยอัปโหลด (ไม่แตะรูปตั้งต้นที่มากับโปรเจกต์)</summary>
    public static void DeleteServiceImage(string? url)
    {
        var name = url?.Split('/').LastOrDefault();
        if (url != null && url.StartsWith("/images/services/") && IsSafeName(name)) TryDelete(Path.Combine(ServiceImageRoot, name!));
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}

/// <summary>
/// เตรียมฐานข้อมูลตอนเปิดระบบ (ปลอดภัย รันทุกครั้งได้ เพิ่มอย่างเดียว ไม่ลบ/ไม่แก้ข้อมูลเดิม)
/// 1) MySQL: เพิ่มคอลัมน์/ตาราง/สถานะที่เวอร์ชันใหม่ต้องใช้ ถ้าฐานข้อมูลยังเป็นเวอร์ชันเก่า
///    (กันปัญหา "Unknown column" ที่ทำให้หน้าห้องพัก/การจองล้ม เมื่อยังไม่ได้รันสคริปต์อัปเกรด)
/// 2) บทบาทที่ระบบต้องใช้
/// 3) ถ้ายังไม่มีประเภทห้องเลย → ใส่ข้อมูลตัวอย่างจาก Database/aurellestay_sample_catalog.sql
///    (ปิดได้ด้วย Database:SeedSampleCatalog = false) แก้ราคา/ห้องจริงได้ที่หน้าผู้จัดการ
/// </summary>
public static class DbSetup
{
    private static readonly (string Table, string Column, string Ddl)[] Columns =
    {
        ("room_types", "description", "text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL"),
        // Part 53: ฝ่ายส่งของ — เวลาเข้าคิว/รับงาน งานด่วน และจำนวนครั้งที่ไม่พบผู้เข้าพัก
        ("delivery_tasks", "created_at", "datetime NULL DEFAULT CURRENT_TIMESTAMP"),
        ("delivery_tasks", "picked_at", "datetime NULL"),
        ("delivery_tasks", "urgent", "tinyint(1) NOT NULL DEFAULT 0"),
        ("delivery_tasks", "missed", "tinyint unsigned NOT NULL DEFAULT 0"),
        // Part 54: ฝ่ายแม่บ้าน
        ("housekeeping_tasks", "kind", "varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL"),
        ("housekeeping_tasks", "created_at", "datetime NULL DEFAULT CURRENT_TIMESTAMP"),
        ("housekeeping_tasks", "picked_at", "datetime NULL"),
        ("housekeeping_tasks", "urgent", "tinyint(1) NOT NULL DEFAULT 0"),
        ("housekeeping_tasks", "missed", "tinyint unsigned NOT NULL DEFAULT 0"),
        ("housekeeping_tasks", "pref_time", "varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL"),
        ("housekeeping_tasks", "note", "varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL"),
        ("bookings", "do_not_disturb", "tinyint(1) NOT NULL DEFAULT 0"),
        ("room_types", "amenities", "varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL"),
        ("room_types", "image_url", "varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL"),
        ("room_types", "weekend_price", "decimal(10,2) NULL"),
        ("bookings", "guests", "tinyint NULL"),
        ("payments", "slip_path", "varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL"),
        ("payments", "reference", "varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL"),
        ("users", "security_stamp", "varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL"),
        ("extra_services", "category", "varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL"),
        ("extra_services", "description", "varchar(300) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL"),
        ("extra_services", "image_url", "varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL"),
        ("promotions", "promo_code", "varchar(30) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL"),
        ("promotions", "description", "varchar(300) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL"),
        ("promotions", "min_nights", "smallint NULL"),
        ("promotions", "max_uses", "int NULL"),
        ("promotions", "max_discount", "decimal(10,2) NULL"),
        ("promotions", "max_per_user", "smallint NULL"),
        ("promotions", "recipients_only", "tinyint(1) NOT NULL DEFAULT 0"),
        // Part 49: คูปองกดเก็บ / ช่วงเวลาเก็บ / แสดงเป็นแบนเนอร์ / แคมเปญเทศกาลที่ระบบสร้างให้
        ("promotions", "claimable", "tinyint(1) NOT NULL DEFAULT 0"),
        ("promotions", "claim_from", "date NULL"),
        ("promotions", "claim_until", "date NULL"),
        ("promotions", "featured", "tinyint(1) NOT NULL DEFAULT 0"),
        // Part 50: เช็คอินออนไลน์ล่วงหน้า — เวลาที่จะมาถึง + คำขอพิเศษ
        ("bookings", "arrival_time", "varchar(5) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL"),
        ("bookings", "guest_request", "varchar(300) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL"),
        ("promotions", "auto_key", "varchar(40) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL, ADD UNIQUE KEY `ux_promo_auto` (`auto_key`)"),
    };

    private static readonly (string Name, string Category, string Image, string Description)[] SampleServices =
    {
        ("อาหารเช้า", "DINE", "/images/services/breakfast.jpg", "อาหารเช้าสำหรับผู้เข้าพัก เสิร์ฟทุกเช้าระหว่างการเข้าพัก"),
        ("อาหารเย็น", "DINE", "/images/services/dinner.jpg", "อาหารเย็นสำหรับผู้เข้าพัก"),
        ("เตียงเสริม", "ROOM", "/images/services/extra-bed.jpg", "เตียงเสริมพร้อมชุดเครื่องนอน จัดเตรียมในห้องก่อนเช็คอิน"),
        ("นวดแผนไทย (60 นาที)", "SPA", "/images/services/thai-massage.jpg", "นวดผ่อนคลาย 60 นาที"),
        ("สปาอโรมา (90 นาที)", "SPA", "/images/services/aroma-spa.jpg", "สปาน้ำมันหอมระเหย 90 นาที"),
    };

    /// <summary>ข้อความช่วยเหลือเมื่อเริ่มระบบแล้วต่อ MySQL ไม่ได้ (ไม่แสดงรหัสผ่าน)</summary>
    public static void PrintConnectionHelp(string? connectionString, MySqlConnector.MySqlException ex)
    {
        string server = "?", user = "?", database = "?"; uint port = 3306;
        try
        {
            var cs = new MySqlConnector.MySqlConnectionStringBuilder(connectionString ?? "");
            server = cs.Server; port = cs.Port; user = cs.UserID; database = cs.Database;
        }
        catch (ArgumentException) { /* connection string รูปแบบผิด: แสดงค่าเริ่มต้น */ }

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var w = Console.Error;
        w.WriteLine();
        w.WriteLine("==============================================================");
        w.WriteLine($" AurelleStay เริ่มระบบไม่ได้: เชื่อมต่อ MySQL ที่ {server}:{port} ไม่สำเร็จ");
        w.WriteLine($" ({ex.Message})");
        w.WriteLine("==============================================================");
        if (ex.ErrorCode == MySqlConnector.MySqlErrorCode.AccessDenied)
        {
            w.WriteLine($" ผู้ใช้ '{user}' หรือรหัสผ่านของ MySQL ไม่ถูกต้อง");
            w.WriteLine(" แก้ที่ ConnectionStrings:DefaultConnection ใน appsettings.json");
        }
        else if (ex.ErrorCode == MySqlConnector.MySqlErrorCode.UnknownDatabase)
        {
            w.WriteLine($" ยังไม่มีฐานข้อมูล '{database}' — รัน .\\setup.ps1 หรือไฟล์ Database\\aurellestay_mysql.sql");
        }
        else
        {
            w.WriteLine(" สาเหตุที่พบบ่อย: บริการ MySQL ยังไม่ได้เปิด (เช่น หลังรีสตาร์ทเครื่อง)");
            w.WriteLine(" วิธีแก้บน Windows: เปิด PowerShell แบบ Run as administrator แล้วพิมพ์");
            w.WriteLine("     Get-Service MySQL*                          (ดูชื่อบริการ เช่น MySQL80)");
            w.WriteLine("     Start-Service MySQL80                       (เปิดบริการ)");
            w.WriteLine("     Set-Service MySQL80 -StartupType Automatic  (ให้เปิดเองทุกครั้งที่เปิดเครื่อง)");
            w.WriteLine($" ตรวจว่าบริการนั้นใช้พอร์ต {port} ตรงกับ appsettings.json แล้วรัน dotnet run อีกครั้ง");
        }
        w.WriteLine();
    }

    public static async Task PrepareDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Csi402dbContext>();
        var log = app.Logger;
        try
        {
            if (db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true)
                await UpgradeMySqlAsync(db, log);

            var existing = await db.Roles.Select(r => r.RoleName).ToListAsync();
            foreach (var name in RoleNames.All.Except(existing, StringComparer.OrdinalIgnoreCase))
                db.Roles.Add(new Role { RoleName = name });
            await db.SaveChangesAsync();

            // ลบบันทึกกิจกรรมที่เก่ากว่าระยะเก็บ (Audit:RetentionDays ค่าเริ่มต้น 365 วัน, 0 = เก็บตลอด)
            var keepDays = app.Configuration.GetValue("Audit:RetentionDays", 365);
            if (keepDays > 0)
            {
                var cutoff = pameka_7033_l005.Helpers.AppClock.Now.AddDays(-keepDays);
                var removed = await db.AuditLogs.Where(a => a.CreatedAt < cutoff).ExecuteDeleteAsync();
                if (removed > 0) log.LogInformation("ลบบันทึกกิจกรรมเก่ากว่า {Days} วัน {Count} รายการ", keepDays, removed);
            }

            if (app.Configuration.GetValue("Database:SeedSampleCatalog", true) && !await db.RoomTypes.AnyAsync())
                await SeedCatalogAsync(db, app.Environment.ContentRootPath, log);
            // Part 52: กิจกรรมตัวอย่าง (ครั้งแรกเท่านั้น)
            if (app.Configuration.GetValue("Database:SeedSampleCatalog", true) && await HotelActivities.SeedAsync(db) is > 0 and var na)
                log.LogInformation("เพิ่มกิจกรรมตัวอย่าง {Count} รายการ", na);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "เตรียมฐานข้อมูลไม่สำเร็จ: ตรวจสอบว่า MySQL เปิดอยู่ และ ConnectionStrings:DefaultConnection (ชื่อฐานข้อมูล ผู้ใช้ รหัสผ่าน) ถูกต้อง");
        }
    }

    private static async Task UpgradeMySqlAsync(Csi402dbContext db, ILogger log)
    {
        var have = (await db.Database.SqlQueryRaw<string>(
                "SELECT CONCAT(TABLE_NAME, '.', COLUMN_NAME) AS Value FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE()").ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!have.Contains("bookings.booking_id"))
            throw new InvalidOperationException("ไม่พบตารางของระบบ กรุณารัน Database/aurellestay_mysql.sql ก่อนเปิดใช้งานครั้งแรก");

        foreach (var (table, column, ddl) in Columns.Where(c => !have.Contains($"{c.Table}.{c.Column}")))
        {
            // ชื่อตาราง/คอลัมน์มาจากรายการคงที่ในโค้ด (ไม่ใช่ข้อมูลผู้ใช้)
            var alter = $"ALTER TABLE `{table}` ADD COLUMN `{column}` {ddl}";
            await db.Database.ExecuteSqlRawAsync(alter);
            log.LogWarning("อัปเกรดฐานข้อมูล: เพิ่มคอลัมน์ {Table}.{Column}", table, column);
        }
        // คอลัมน์รูปบริการเพิ่งถูกสร้าง: เติมหมวด/คำอธิบาย/รูปตั้งต้นให้บริการตัวอย่างที่ชื่อตรงกัน (เฉพาะช่องที่ยังว่าง)
        if (!have.Contains("extra_services.image_url"))
        {
            foreach (var (name, cat, img, desc) in SampleServices)
                await db.Database.ExecuteSqlRawAsync(
                    "UPDATE `extra_services` SET `category` = COALESCE(`category`, {0}), `image_url` = COALESCE(`image_url`, {1}), `description` = COALESCE(`description`, {2}) WHERE `service_name` = {3}",
                    cat, img, desc, name);
        }

        if (!have.Contains("expenses.expense_id"))
        {
            await db.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS `expenses` (
                `expense_id` int NOT NULL AUTO_INCREMENT,
                `category` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
                `amount` decimal(10,2) NOT NULL,
                `spent_on` date NOT NULL,
                `note` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
                `created_by` int NULL,
                `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (`expense_id`),
                CONSTRAINT `expenses_ibfk_1` FOREIGN KEY (`created_by`) REFERENCES `users` (`user_id`) ON DELETE SET NULL,
                KEY `ix_spent_on` (`spent_on`), KEY `ix_created_by` (`created_by`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
            log.LogWarning("อัปเกรดฐานข้อมูล: สร้างตาราง expenses");
        }

        if (!have.Contains("notifications.notification_id"))
        {
            await db.Database.ExecuteSqlRawAsync(Notify.CreateTableSql);
            log.LogWarning("อัปเกรดฐานข้อมูล: สร้างตาราง notifications");
        }

        if (!have.Contains("contact_messages.message_id"))
        {
            await db.Database.ExecuteSqlRawAsync(ContactInbox.CreateTableSql);
            log.LogWarning("อัปเกรดฐานข้อมูล: สร้างตาราง contact_messages");
        }

        if (!have.Contains("rate_seasons.season_id"))
        {
            await db.Database.ExecuteSqlRawAsync(RatePlan.SeasonsTableSql);
            log.LogWarning("อัปเกรดฐานข้อมูล: สร้างตาราง rate_seasons (ช่วงราคาพิเศษ)");
        }
        else if (!have.Contains("rate_seasons.auto_key"))
        {
            // Part 47: เพิ่มคอลัมน์ (ไม่แตะข้อมูลเดิม) สำหรับช่วงราคาเทศกาลที่ระบบสร้างอัตโนมัติ
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `rate_seasons` ADD COLUMN `auto_key` varchar(40) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL, ADD UNIQUE KEY `ux_season_auto` (`auto_key`)");
            log.LogWarning("อัปเกรดฐานข้อมูล: เพิ่มคอลัมน์ rate_seasons.auto_key (ราคาตามเทศกาลอัตโนมัติ)");
        }

        if (!have.Contains("promo_recipients.recipient_id"))
        {
            await db.Database.ExecuteSqlRawAsync(PromoCatalog.RecipientsTableSql);
            log.LogWarning("อัปเกรดฐานข้อมูล: สร้างตาราง promo_recipients");
        }

        // Part 51: อีเมลขาออก / รีเซ็ตรหัสผ่าน / รายชื่อรอห้องว่าง (สร้างใหม่อย่างเดียว ไม่แตะข้อมูลเดิม)
        if (!have.Contains("email_outbox.email_id"))
        {
            await db.Database.ExecuteSqlRawAsync(Mailer.OutboxTableSql);
            log.LogWarning("อัปเกรดฐานข้อมูล: สร้างตาราง email_outbox");
        }
        if (!have.Contains("password_resets.reset_id"))
        {
            await db.Database.ExecuteSqlRawAsync(Mailer.ResetsTableSql);
            log.LogWarning("อัปเกรดฐานข้อมูล: สร้างตาราง password_resets");
        }
        if (!have.Contains("room_waitlist.wait_id"))
        {
            await db.Database.ExecuteSqlRawAsync(Waitlist.TableSql);
            log.LogWarning("อัปเกรดฐานข้อมูล: สร้างตาราง room_waitlist");
        }

        // Part 52: กิจกรรมของโรงแรม
        if (!have.Contains("activities.activity_id"))
        {
            await db.Database.ExecuteSqlRawAsync(HotelActivities.ActivitiesTableSql);
            log.LogWarning("อัปเกรดฐานข้อมูล: สร้างตาราง activities");
        }
        if (!have.Contains("activity_bookings.ab_id"))
        {
            await db.Database.ExecuteSqlRawAsync(HotelActivities.BookingsTableSql);
            log.LogWarning("อัปเกรดฐานข้อมูล: สร้างตาราง activity_bookings");
        }

        // Part 54: แจ้งซ่อม
        if (!have.Contains("room_issues.issue_id"))
        {
            await db.Database.ExecuteSqlRawAsync(HousekeepingOps.IssuesTableSql);
            log.LogWarning("อัปเกรดฐานข้อมูล: สร้างตาราง room_issues");
        }
        // Part 53: ของใช้ที่ผู้เข้าพักขอเพิ่ม
        if (!have.Contains("item_requests.request_id"))
        {
            await db.Database.ExecuteSqlRawAsync(RoomRequests.TableSql);
            log.LogWarning("อัปเกรดฐานข้อมูล: สร้างตาราง item_requests");
        }

        if (!have.Contains("audit_logs.audit_id"))
        {
            await db.Database.ExecuteSqlRawAsync(AuditTrail.CreateTableSql);
            log.LogWarning("อัปเกรดฐานข้อมูล: สร้างตาราง audit_logs");
        }

        var statusType = (await db.Database.SqlQueryRaw<string>(
            "SELECT COLUMN_TYPE AS Value FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'payments' AND COLUMN_NAME = 'status' LIMIT 1").ToListAsync()).FirstOrDefault();
        if (statusType != null && !statusType.Contains("REJECTED", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `payments` MODIFY COLUMN `status` enum('PENDING','PAID','REFUNDED','REJECTED') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'PENDING'");
            log.LogWarning("อัปเกรดฐานข้อมูล: เพิ่มสถานะการชำระเงิน REJECTED");
        }

        var bookingStatusType = (await db.Database.SqlQueryRaw<string>(
            "SELECT COLUMN_TYPE AS Value FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'bookings' AND COLUMN_NAME = 'status' LIMIT 1").ToListAsync()).FirstOrDefault();
        if (bookingStatusType != null && !bookingStatusType.Contains("NO_SHOW", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `bookings` MODIFY COLUMN `status` enum('PENDING','CONFIRMED','CHECKED_IN','CHECKED_OUT','CANCELLED','NO_SHOW') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'PENDING'");
            log.LogWarning("อัปเกรดฐานข้อมูล: เพิ่มสถานะการจอง NO_SHOW (ไม่มาเข้าพัก)");
        }

        var promoType = (await db.Database.SqlQueryRaw<string>(
            "SELECT COLUMN_TYPE AS Value FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'promotions' AND COLUMN_NAME = 'promo_type' LIMIT 1").ToListAsync()).FirstOrDefault();
        if (promoType != null && !promoType.Contains("'CODE'", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `promotions` MODIFY COLUMN `promo_type` enum('MEMBER','MOTHERS_DAY','EARLY_BIRD','BIRTHDAY','ANNIVERSARY','SEASONAL','LONG_STAY','CODE') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL");
            log.LogWarning("อัปเกรดฐานข้อมูล: เพิ่มประเภทโปรโมชั่น SEASONAL / LONG_STAY / CODE");
        }
    }

    private static async Task SeedCatalogAsync(Csi402dbContext db, string contentRoot, ILogger log)
    {
        var file = Path.Combine(contentRoot, "Database", "aurellestay_sample_catalog.sql");
        if (!File.Exists(file)) { log.LogWarning("ยังไม่มีประเภทห้องในระบบ และไม่พบไฟล์ข้อมูลตัวอย่าง {File} — เพิ่มห้องได้ที่ /AdminRooms", file); return; }
        // ตัดคำสั่ง USE (ใช้ฐานข้อมูลตาม connection string) และบรรทัดหมายเหตุ
        var lines = (await File.ReadAllLinesAsync(file)).Where(l => !l.TrimStart().StartsWith("--") && !l.TrimStart().StartsWith("USE ", StringComparison.OrdinalIgnoreCase));
        var sql = string.Join("\n", lines);
        if (db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) != true) return;
        await db.Database.ExecuteSqlRawAsync(sql.Replace("{", "{{").Replace("}", "}}"));
        log.LogWarning("ยังไม่มีห้องในระบบ: เพิ่มข้อมูลห้อง/บริการตัวอย่างจาก {File} แล้ว (แก้ราคาและห้องจริงได้ที่ /AdminRooms)", Path.GetFileName(file));
    }
}

// ======================================================================
// HotelInfo
// ======================================================================
/// <summary>ข้อมูลโรงแรมจาก appsettings หมวด Hotel/Booking — ใช้ที่ส่วนท้ายเว็บ หน้าติดต่อ หน้าเงื่อนไข และข้อมูลสำหรับเครื่องมือค้นหา (ค่าว่าง = ไม่แสดง)</summary>
public sealed record HotelInfo(string Name, string? Phone, string? Email, string? LineId, string? Address,
    string CheckInTime, string CheckOutTime, int PaymentHoldHours)
{
    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    public static HotelInfo From(IConfiguration c) => new(
        Blank(c["Hotel:Name"]) ?? "AurelleStay",
        Blank(c["Hotel:Phone"]), Blank(c["Hotel:Email"]), Blank(c["Hotel:LineId"]), Blank(c["Hotel:Address"]),
        Blank(c["Hotel:CheckInTime"]) ?? "14:00", Blank(c["Hotel:CheckOutTime"]) ?? "12:00",
        Math.Max(1, c.GetValue("Booking:PaymentHoldHours", 24)));

    public bool HasContact => Phone != null || Email != null || LineId != null || Address != null;
    public string? TelHref => Phone == null ? null : "tel:" + new string(Phone.Where(ch => char.IsDigit(ch) || ch == '+').ToArray());
}

// ======================================================================
// PaymentInfo / PromptPay (Part 49)
// ======================================================================
/// <summary>ช่องทางรับเงินของโรงแรมจาก appsettings หมวด Payment (หรือ .env ของ Docker) — ค่าว่าง = ไม่แสดง</summary>
public sealed record PaymentInfo(string? PromptPayId, string? AccountName, string? BankName, string? AccountNo)
{
    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    public static PaymentInfo From(IConfiguration c) => new(
        Blank(c["Payment:PromptPayId"]), Blank(c["Payment:AccountName"]), Blank(c["Payment:BankName"]), Blank(c["Payment:AccountNo"]));
    public bool HasPromptPay => PromptPay.Normalize(PromptPayId) != null;
    public bool HasBank => AccountNo != null && BankName != null;
}

/// <summary>
/// สร้างข้อความ QR พร้อมเพย์แบบระบุยอด (มาตรฐาน EMVCo / Thai QR Payment) — แอปธนาคารสแกนแล้วยอดขึ้นให้อัตโนมัติ
/// รองรับเบอร์มือถือ 10 หลัก, เลขบัตรประชาชน/นิติบุคคล 13 หลัก, e-Wallet 15 หลัก
/// </summary>
public static class PromptPay
{
    /// <summary>คืน (ชนิด tag, ค่า) หรือ null ถ้ารูปแบบไม่ถูกต้อง</summary>
    public static (string Tag, string Value)? Normalize(string? id)
    {
        var d = new string((id ?? "").Where(char.IsDigit).ToArray());
        if (d.Length == 10 && d[0] == '0') return ("01", "0066" + d[1..]);
        if (d.Length == 11 && d.StartsWith("66")) return ("01", "00" + d);
        if (d.Length == 13) return ("02", d);
        if (d.Length == 15) return ("03", d);
        return null;
    }

    private static string F(string tag, string value) => tag + value.Length.ToString("00") + value;

    public static string? Payload(string? id, decimal? amount)
    {
        if (Normalize(id) is not { } acc) return null;
        var merchant = F("00", "A000000677010111") + F(acc.Tag, acc.Value);
        var s = F("00", "01") + F("01", amount is > 0 ? "12" : "11") + F("29", merchant) + F("58", "TH") + F("53", "764");
        if (amount is > 0) s += F("54", amount.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        s += "6304";
        return s + Crc16(s).ToString("X4");
    }

    /// <summary>CRC-16/CCITT-FALSE (poly 0x1021, init 0xFFFF) ตามมาตรฐาน EMVCo</summary>
    public static int Crc16(string s)
    {
        var crc = 0xFFFF;
        foreach (var b in System.Text.Encoding.ASCII.GetBytes(s))
        {
            crc ^= b << 8;
            for (var i = 0; i < 8; i++) crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x1021) & 0xFFFF : (crc << 1) & 0xFFFF;
        }
        return crc;
    }
}

// ======================================================================
// AuditTrail / AuditFilter
// ======================================================================
/// <summary>
/// บันทึกกิจกรรม: ทุกคำสั่ง POST ของผู้ที่ล็อกอิน (ผ่าน AuditFilter) + เข้าสู่ระบบไม่สำเร็จ/สมัครสมาชิก (เรียกเองใน AccountController)
/// รายละเอียดใช้ข้อความแจ้งผลที่ผู้ใช้เห็น (ซ่อนรหัสผ่านชั่วคราวก่อนบันทึก) — ถ้าบันทึกไม่สำเร็จ จะไม่ทำให้คำสั่งหลักล้ม
/// </summary>
public static class AuditTrail
{
    public const string CreateTableSql = @"CREATE TABLE IF NOT EXISTS `audit_logs` (
    `audit_id` bigint NOT NULL AUTO_INCREMENT,
    `user_id` int NULL,
    `actor` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `actor_role` varchar(30) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `action` varchar(60) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `target_id` int NULL,
    `success` tinyint(1) NOT NULL,
    `detail` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `ip` varchar(45) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`audit_id`),
    CONSTRAINT `audit_logs_ibfk_1` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE SET NULL,
    KEY `ix_audit_created` (`created_at`),
    KEY `ix_audit_user` (`user_id`),
    KEY `ix_audit_action` (`action`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

    /// <summary>คำสั่ง POST ที่ไม่เปลี่ยนข้อมูล ไม่ต้องบันทึก</summary>
    public static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    { "Booking.Preview", "FrontDesk.Preview", "Account.Logout", "Settings.SaveAppearance", "Account.Login", "Account.Register" };

    /// <summary>ชื่อการกระทำ (Controller.Action) → (ชื่อภาษาไทย, ชนิดของรายการเป้าหมาย)</summary>
    public static readonly Dictionary<string, (string Label, string Target)> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Account.Login"] = ("เข้าสู่ระบบไม่สำเร็จ", ""), ["Account.Register"] = ("สมัครสมาชิก", "ผู้ใช้"),
        ["Account.EditUser"] = ("แก้ไขข้อมูลผู้ใช้", "ผู้ใช้"), ["Account.DeleteUser"] = ("ลบผู้ใช้", "ผู้ใช้"), ["Account.ClearLoginHistory"] = ("ล้างประวัติเข้าสู่ระบบ", "ผู้ใช้"),
        ["Settings.SaveProfile"] = ("แก้ไขข้อมูลส่วนตัว", ""), ["Settings.ChangePassword"] = ("เปลี่ยนรหัสผ่าน", ""),
        ["Settings.SignOutOthers"] = ("ออกจากระบบอุปกรณ์อื่นทั้งหมด", ""),
        ["Settings.ExportData"] = ("ดาวน์โหลดข้อมูลของตัวเอง", ""), ["Settings.DeleteAccount"] = ("ลบบัญชีของตัวเอง", ""),
        ["Booking.Confirm"] = ("จองห้อง", "การจอง"), ["Booking.Cancel"] = ("ยกเลิกการจอง", "การจอง"),
        ["Booking.PayConfirm"] = ("แนบหลักฐานการชำระเงิน", "การจอง"), ["Booking.Pay"] = ("แนบหลักฐานการชำระเงิน", "การจอง"),
        ["Promotions.Claim"] = ("เก็บคูปองส่วนลด", "โปรโมชั่น"), ["AdminRooms.GenerateFestivals"] = ("สร้างช่วงราคาเทศกาล", ""),
        ["AdminPromotions.GenerateCampaigns"] = ("สร้างแคมเปญโปรเทศกาล", ""),
        ["AdminAudit.Backup"] = ("ดาวน์โหลดไฟล์สำรองฐานข้อมูล", ""), ["AdminAudit.SeedDemo"] = ("สร้างข้อมูลลูกค้าตัวอย่าง", ""),
        ["AdminAudit.RemoveDemo"] = ("ลบข้อมูลลูกค้าตัวอย่าง", ""), ["BookingDetails.PreArrival"] = ("แจ้งเช็คอินล่วงหน้า", "การจอง"),
        ["Account.ForgotPassword"] = ("ขอลิงก์ตั้งรหัสผ่านใหม่", "ผู้ใช้"), ["Account.ResetPassword"] = ("ตั้งรหัสผ่านใหม่ผ่านอีเมล", "ผู้ใช้"),
        ["Rooms.Waitlist"] = ("ลงชื่อรอห้องว่าง", "ประเภทห้อง"), ["Activities.Book"] = ("จองกิจกรรม", "การจองกิจกรรม"), ["Activities.Cancel"] = ("ยกเลิกกิจกรรม", "การจองกิจกรรม"),
        ["FrontDesk.MarkActivity"] = ("บันทึกการเข้าร่วมกิจกรรม", "การจองกิจกรรม"), ["AdminActivities.Save"] = ("บันทึกกิจกรรม", "กิจกรรม"), ["AdminActivities.Delete"] = ("ลบกิจกรรม", "กิจกรรม"), ["AdminAudit.TestEmail"] = ("ส่งอีเมลทดสอบ", ""),
        ["Member.RequestItem"] = ("ขอของใช้เพิ่ม", "การจอง"), ["Member.CancelRequest"] = ("ยกเลิกคำขอของใช้", "คำขอของใช้"),
        ["Member.RequestCleaning"] = ("ขอทำความสะอาดห้อง", "การจอง"), ["Member.DoNotDisturb"] = ("ตั้งป้ายห้ามรบกวน", "การจอง"),
        ["Housekeeping.NotNow"] = ("แจ้งลูกค้าไม่สะดวก", "งานทำความสะอาด"), ["Housekeeping.Urgent"] = ("ตั้ง/ยกเลิกงานด่วน", "งานทำความสะอาด"),
        ["Housekeeping.ReportIssue"] = ("แจ้งซ่อม", "ห้อง"), ["AdminReports.FixIssue"] = ("ปิดเรื่องแจ้งซ่อม", "แจ้งซ่อม"),
        ["AdminReports.CloseRoom"] = ("ปิดห้องซ่อมบำรุง", "ห้อง"),
        ["Delivery.NotHome"] = ("แจ้งไม่พบผู้เข้าพัก", "งานส่งของ"), ["Delivery.Urgent"] = ("ตั้ง/ยกเลิกงานด่วน", "งานส่งของ"),
        ["Booking.RequestRefund"] = ("ขอคืนเงิน", "การจอง"), ["Reviews.Create"] = ("เขียนรีวิว", "การจอง"),
        ["Accounting.Confirm"] = ("ยืนยันการชำระเงิน", "การชำระ"), ["Accounting.Reject"] = ("ปฏิเสธหลักฐานการชำระเงิน", "การชำระ"),
        ["Accounting.ApproveRefund"] = ("อนุมัติคืนเงิน", "การชำระ"), ["Accounting.RejectRefund"] = ("ปฏิเสธคำขอคืนเงิน", "การชำระ"),
        ["Accounting.AddExpense"] = ("บันทึกรายจ่าย", "รายจ่าย"), ["Accounting.DeleteExpense"] = ("ลบรายจ่าย", "รายจ่าย"),
        ["FrontDesk.Confirm"] = ("จองห้องให้ลูกค้า", "การจอง"), ["FrontDesk.CheckIn"] = ("เช็คอิน", "การจอง"),
        ["AdminPromotions.Delete"] = ("ลบโปรโมชั่น", "โปรโมชั่น"), ["AdminPromotions.SendCode"] = ("ส่งรหัสโปรโมชั่นให้ลูกค้า", "โปรโมชั่น"),
        ["FrontDesk.MarkNoShow"] = ("บันทึกไม่มาเข้าพัก", "การจอง"), ["FrontDesk.UndoNoShow"] = ("ยกเลิกสถานะไม่มาเข้าพัก", "การจอง"),
        ["FrontDesk.CheckOut"] = ("เช็คเอาท์", "การจอง"), ["FrontDesk.SendDelivery"] = ("ส่งงานให้ฝ่ายส่งของ", "การจอง"),
        ["FrontDesk.SetRoomStatus"] = ("เปลี่ยนสถานะห้อง", "ห้อง"), ["FrontDesk.SendCleaning"] = ("ส่งงานทำความสะอาด", "ห้อง"),
        ["Delivery.Take"] = ("รับงานส่งของ", "งาน"), ["Delivery.Finish"] = ("ส่งของสำเร็จ", "งาน"),
        ["Housekeeping.Take"] = ("รับงานทำความสะอาด", "งาน"), ["Housekeeping.Finish"] = ("ทำความสะอาดเสร็จ", "งาน"),
        ["AdminBookings.Cancel"] = ("ผู้จัดการยกเลิกการจอง", "การจอง"),
        ["AdminRooms.SaveRoom"] = ("บันทึกห้อง", "ห้อง"), ["AdminRooms.DeleteRoom"] = ("ลบห้อง", "ห้อง"),
        ["AdminRooms.SaveTypeDetails"] = ("แก้รูป/รายละเอียดประเภทห้อง", "ประเภทห้อง"), ["AdminRooms.SaveType"] = ("บันทึกประเภทห้อง/ราคา", "ประเภทห้อง"),
        ["AdminRooms.SaveTheme"] = ("บันทึกธีมห้อง", "ธีม"),
        ["AdminRooms.SaveSeason"] = ("บันทึกช่วงราคาพิเศษ", "ราคาห้อง"), ["AdminRooms.ToggleSeason"] = ("เปิด/ปิดช่วงราคาพิเศษ", "ราคาห้อง"),
        ["AdminRooms.DeleteSeason"] = ("ลบช่วงราคาพิเศษ", "ราคาห้อง"),
        ["AdminServices.Save"] = ("บันทึกบริการเสริม", "บริการ"), ["AdminServices.Delete"] = ("ลบบริการเสริม", "บริการ"),
        ["AdminPromotions.Save"] = ("บันทึกโปรโมชั่น", "โปรโมชั่น"), ["AdminReviews.Delete"] = ("ลบรีวิว", "รีวิว"),
        ["AdminMessages.Update"] = ("จัดการข้อความติดต่อ", "ข้อความ"), ["Home.SendMessage"] = ("ส่งข้อความติดต่อโรงแรม", "ข้อความ"),
        ["AdminStaff.Create"] = ("สร้างบัญชีพนักงาน", "ผู้ใช้"), ["AdminStaff.ResetPassword"] = ("ออกรหัสผ่านใหม่", "ผู้ใช้"),
        ["AdminStaff.ChangeRole"] = ("เปลี่ยนบทบาทพนักงาน", "ผู้ใช้"), ["AdminStaff.Delete"] = ("ลบพนักงาน", "ผู้ใช้"),
    };

    public static string Label(string action) => Labels.TryGetValue(action, out var l) ? l.Label : action;
    public static string TargetKind(string action) => Labels.TryGetValue(action, out var l) ? l.Target : "";
    /// <summary>รายการเป้าหมายเป็นการจองหรือไม่ (ใช้ทำลิงก์ไปหน้ารายละเอียดการจอง)</summary>
    public static bool TargetsBooking(string action) => TargetKind(action) == "การจอง";

    /// <summary>ซ่อนรหัสผ่านชั่วคราวที่อาจอยู่ในข้อความแจ้งผล และตัดความยาว</summary>
    public static string? Scrub(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = System.Text.RegularExpressions.Regex.Replace(text, @"(รหัสผ่าน(?:ชั่วคราว)?[:\s]+)\S+", "$1••••••");
        return s.Length > 500 ? s[..497] + "..." : s;
    }

    public static async Task WriteAsync(HttpContext http, string action, int? targetId, bool success, string? detail,
        int? userId = null, string? actor = null, string? role = null)
    {
        try
        {
            var u = http.User;
            userId ??= int.TryParse(u.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
            actor ??= u.FindFirstValue(ClaimTypes.Email) ?? "-";
            role ??= u.FindFirstValue(ClaimTypes.Role) ?? "-";
            actor = actor.Length > 100 ? actor[..100] : actor;
            var ip = http.Connection.RemoteIpAddress?.ToString();
            var text = Scrub(detail);
            var db = http.RequestServices.GetRequiredService<Csi402dbContext>();
            // SQL ตรง (ไม่ผ่าน ChangeTracker ของคำขอ) และ user_id ใช้เฉพาะเมื่อบัญชียังอยู่ (เช่น ลูกค้าลบบัญชีตัวเอง → NULL)
            await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO audit_logs (user_id, actor, actor_role, action, target_id, success, detail, ip, created_at)
                VALUES ((SELECT x.user_id FROM (SELECT user_id FROM users WHERE user_id = {userId}) x), {actor}, {role}, {action}, {targetId}, {success}, {text}, {ip}, {pameka_7033_l005.Helpers.AppClock.Now})");
        }
        catch (Exception ex)
        {
            http.RequestServices.GetService<ILogger<AuditFilter>>()?.LogWarning(ex, "บันทึกกิจกรรมไม่สำเร็จ: {Action}", action);
        }
    }
}

/// <summary>หลังทำคำสั่ง POST เสร็จ: สำเร็จ = เปลี่ยนหน้า (PRG) และไม่มีข้อความแจ้งข้อผิดพลาด; แสดงฟอร์มเดิมซ้ำ = ข้อมูลไม่ผ่าน</summary>
public sealed class AuditFilter : Microsoft.AspNetCore.Mvc.Filters.IAsyncActionFilter
{
    private static readonly string[] IdKeys = { "id", "userId", "roomId", "paymentId", "bookingId" };

    public async Task OnActionExecutionAsync(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext ctx, Microsoft.AspNetCore.Mvc.Filters.ActionExecutionDelegate next)
    {
        var done = await next();
        var http = ctx.HttpContext;
        if (!HttpMethods.IsPost(http.Request.Method) || http.User.Identity?.IsAuthenticated != true) return;
        var key = $"{ctx.RouteData.Values["controller"]}.{ctx.RouteData.Values["action"]}";
        if (AuditTrail.Skip.Contains(key)) return;

        var tempData = (ctx.Controller as Controller)?.TempData;
        bool ok; string? detail;
        if (done.Exception != null && !done.ExceptionHandled) { ok = false; detail = "ระบบขัดข้องระหว่างทำรายการ"; }
        else
        {
            var moved = done.Result is RedirectResult or RedirectToActionResult or LocalRedirectResult or RedirectToRouteResult or FileResult;
            ok = moved && tempData?.Peek("StatusError") is not true;
            detail = tempData?.Peek("Status") as string;
            if (!moved && detail == null)
                detail = (done.Result as ViewResult)?.Model is BookingFormViewModel vm && vm.Error != null ? vm.Error : "ข้อมูลไม่ผ่านการตรวจสอบ";
        }
        await AuditTrail.WriteAsync(http, key, TargetOf(ctx, done.Result), ok, detail);
    }

    private static int? TargetOf(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext ctx, IActionResult? result)
    {
        foreach (var k in IdKeys)
            if (ctx.ActionArguments.TryGetValue(k, out var v) && v is int n && n > 0) return n;
        if (result is RedirectToActionResult ra && ra.RouteValues?.TryGetValue("id", out var rid) == true && int.TryParse(rid?.ToString(), out var r1)) return r1;
        if (result is RedirectResult rr)
        {
            var m = System.Text.RegularExpressions.Regex.Match(rr.Url, @"/(\d+)(?:[?#]|$)");
            if (m.Success) return int.Parse(m.Groups[1].Value);
        }
        return null;
    }
}

// ======================================================================
// Notify
// ======================================================================
/// <summary>
/// การแจ้งเตือนในระบบ: เพิ่มเข้า ChangeTracker ของคำสั่งนั้น จึงถูกบันทึกพร้อมการเปลี่ยนสถานะใน SaveChanges เดียวกัน
/// (สถานะเปลี่ยนแต่ไม่แจ้ง หรือแจ้งแต่สถานะไม่เปลี่ยน จะไม่เกิดขึ้น)
/// </summary>
/// <summary>กล่องข้อความติดต่อโรงแรม: หัวข้อ, สร้างตาราง, จำกัดจำนวนส่งต่อ IP</summary>
/// <summary>หมวดบริการเสริม (ใช้ทั้งหน้าลูกค้าและหน้าผู้จัดการ) — ถ้ายังไม่ได้เลือกหมวด เดาจากชื่อบริการ</summary>
public static class ServiceCategories
{
    public static readonly (string Key, string Label, string Eyebrow, string Lead)[] All =
    {
        ("SPA", "สปาและนวด", "Spa & Wellness", "ผ่อนคลายระหว่างการเข้าพัก"),
        ("DINE", "อาหาร", "Dining", "มื้ออาหารสำหรับผู้เข้าพัก"),
        ("ROOM", "ในห้องพัก", "In-room", "เพิ่มความสะดวกให้ห้องของคุณ"),
        ("OTHER", "บริการอื่น ๆ", "More", "บริการเพิ่มเติมจากโรงแรม"),
    };
    public static bool IsValid(string? key) => All.Any(c => c.Key == key);
    public static string Label(string? key) => All.FirstOrDefault(c => c.Key == key).Label ?? "เดาจากชื่อ";
    public static string Resolve(string? category, string name) =>
        IsValid(category) ? category! :
        name.Contains("นวด") || name.Contains("สปา") ? "SPA"
        : name.Contains("อาหาร") || name.Contains("เช้า") || name.Contains("เย็น") || name.Contains("กลางวัน") || name.Contains("เครื่องดื่ม") ? "DINE"
        : name.Contains("เตียง") || name.Contains("ห้อง") || name.Contains("ซักรีด") ? "ROOM"
        : "OTHER";
}

public static class ContactInbox
{
    public static readonly Dictionary<string, string> Topics = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GENERAL"] = "สอบถามทั่วไป", ["BOOKING"] = "เรื่องการจอง / การชำระเงิน",
        ["PASSWORD"] = "ลืมรหัสผ่าน / เข้าสู่ระบบไม่ได้", ["COMPLAINT"] = "ข้อเสนอแนะ / ร้องเรียน",
    };
    public static string TopicLabel(string? t) => t != null && Topics.TryGetValue(t, out var l) ? l : (t ?? "-");
    /// <summary>ส่งได้ไม่เกินกี่ข้อความต่อ IP ในช่วงเวลา (กันสแปม)</summary>
    public const int MaxPerWindow = 3;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public const string CreateTableSql = @"CREATE TABLE IF NOT EXISTS `contact_messages` (
    `message_id` bigint NOT NULL AUTO_INCREMENT,
    `user_id` int NULL,
    `name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `email` varchar(150) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `phone` varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `topic` varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'GENERAL',
    `message` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `status` varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'NEW',
    `staff_note` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `handled_by` varchar(150) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `ip` varchar(45) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `handled_at` datetime NULL,
    PRIMARY KEY (`message_id`),
    CONSTRAINT `contact_messages_ibfk_1` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE SET NULL,
    KEY `ix_contact_status` (`status`, `created_at`),
    KEY `ix_contact_ip` (`ip`, `created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";
}

public static class Notify
{
    public const string CreateTableSql = @"CREATE TABLE IF NOT EXISTS `notifications` (
    `notification_id` bigint NOT NULL AUTO_INCREMENT,
    `user_id` int NOT NULL,
    `title` varchar(150) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `body` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `link` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `is_read` tinyint(1) NOT NULL DEFAULT 0,
    `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`notification_id`),
    CONSTRAINT `notifications_ibfk_1` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE CASCADE,
    KEY `ix_notify_user` (`user_id`, `is_read`, `created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

    public static void Add(Csi402dbContext db, int userId, string title, string? body, string? link)
    {
        db.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = title.Length > 150 ? title[..150] : title,
            Body = body is { Length: > 500 } ? body[..500] : body,
            Link = link, CreatedAt = pameka_7033_l005.Helpers.AppClock.Now
        });
        // Part 51: แจ้งเตือนทุกรายการถูกส่งเป็นอีเมลด้วย (อีเมลของผู้รับอ่านตอนบันทึก — ผู้ใช้ที่ถูกลบจะไม่ได้รับ)
        var email = db.Users.Where(u => u.UserId == userId).Select(u => u.Email).FirstOrDefault();
        if (!string.IsNullOrEmpty(email)) Mailer.Queue(db, email, title, body ?? "", link, userId);
    }

    public static string BookingLink(int bookingId) => $"/Booking/Details/{bookingId}";
}

/// <summary>ชื่อ claim ของตราประจำเซสชัน (ดู User.SecurityStamp)</summary>
public static class SessionStamp
{
    public const string ClaimType = "aurelle:stamp";
}

/// <summary>
/// เวลาของระบบ (ค่าเริ่มต้น Asia/Bangkok) — ใช้แทน DateTime.Now / DateTime.Today ทุกจุด
/// เพื่อให้ "วันนี้" ถูกต้องแม้เซิร์ฟเวอร์ตั้งเวลาเป็น UTC (ตั้งโซนอื่นได้ที่ App:TimeZone)
/// </summary>
public static class AppClock
{
    private static TimeZoneInfo _tz = Resolve("Asia/Bangkok");

    public static void Configure(string? zoneId) => _tz = Resolve(string.IsNullOrWhiteSpace(zoneId) ? "Asia/Bangkok" : zoneId.Trim());

    private static TimeZoneInfo Resolve(string id)
    {
        foreach (var candidate in new[] { id, "Asia/Bangkok", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("ICT", TimeSpan.FromHours(7), "ICT", "ICT");
    }

    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz);
    public static DateTime Today => Now.Date;
}

/// <summary>กันสูตร Excel (CSV injection) โดยไม่ทำให้เบอร์โทร +66… หรือตัวเลขติดลบเพี้ยน</summary>
public static class CsvSafe
{
    private static readonly System.Text.RegularExpressions.Regex Numeric = new(@"^[+-]?[0-9][0-9 .,\-]*$");

    public static string Guard(string? s)
    {
        s ??= "";
        if (s.Length == 0) return s;
        if (s[0] is '+' or '-') return Numeric.IsMatch(s) ? s : "'" + s;
        return "=@\t\r".Contains(s[0]) ? "'" + s : s;
    }
}


// ======================================================================
// Part 51: อีเมล (กล่องจดหมายขาออก + ส่งผ่าน SMTP) และรีเซ็ตรหัสผ่าน
// ======================================================================
/// <summary>
/// ตั้งค่า SMTP ผ่าน Email:Host / Port / User / Password / From / FromName / EnableSsl (Docker: SMTP_HOST, SMTP_PORT, ...)
/// ไม่ตั้ง Host = ไม่ส่งจริง อีเมลถูกเก็บไว้ในกล่องจดหมาย (สถานะ SAVED) ให้ผู้จัดการเปิดดูได้ในหน้าสถานะระบบ
/// </summary>
public sealed record MailSettings(string? Host, int Port, string? User, string? Password, string From, string FromName, bool EnableSsl, string PublicUrl)
{
    public bool Enabled => !string.IsNullOrWhiteSpace(Host);
    public static MailSettings Load(IConfiguration cfg)
    {
        var host = cfg["Email:Host"]?.Trim();
        var user = cfg["Email:User"]?.Trim();
        var from = cfg["Email:From"]?.Trim();
        return new MailSettings(string.IsNullOrEmpty(host) ? null : host, cfg.GetValue("Email:Port", 587),
            string.IsNullOrEmpty(user) ? null : user, cfg["Email:Password"],
            string.IsNullOrEmpty(from) ? (user ?? "no-reply@aurellestay.local") : from,
            cfg["Email:FromName"] is { Length: > 0 } fn ? fn : "AurelleStay",
            cfg.GetValue("Email:EnableSsl", true),
            (cfg["App:PublicUrl"] is { Length: > 0 } pu ? pu : "http://localhost:8088").TrimEnd('/'));
    }
}

public static class Mailer
{
    public const string OutboxTableSql = @"CREATE TABLE IF NOT EXISTS `email_outbox` (
    `email_id` bigint NOT NULL AUTO_INCREMENT,
    `user_id` int NULL,
    `to_email` varchar(120) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `subject` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `body` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `link` varchar(300) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `status` varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'PENDING',
    `attempts` int NOT NULL DEFAULT 0,
    `last_error` varchar(300) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `sent_at` datetime NULL,
    PRIMARY KEY (`email_id`),
    KEY `ix_outbox_status` (`status`, `created_at`),
    KEY `ix_outbox_user` (`user_id`),
    CONSTRAINT `email_outbox_ibfk_1` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

    public const string ResetsTableSql = @"CREATE TABLE IF NOT EXISTS `password_resets` (
    `reset_id` bigint NOT NULL AUTO_INCREMENT,
    `user_id` int NOT NULL,
    `token_hash` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `expires_at` datetime NOT NULL,
    `used_at` datetime NULL,
    `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `ip` varchar(45) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    PRIMARY KEY (`reset_id`),
    UNIQUE KEY `ux_reset_token` (`token_hash`),
    KEY `ix_reset_user` (`user_id`, `created_at`),
    CONSTRAINT `password_resets_ibfk_1` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

    /// <summary>เพิ่มอีเมลเข้ากล่องขาออก (บันทึกพร้อม SaveChanges ของผู้เรียก) ตัวส่งเบื้องหลังจะส่งภายในไม่กี่วินาที</summary>
    public static void Queue(Csi402dbContext db, string toEmail, string subject, string body, string? link = null, int? userId = null)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) return;
        db.EmailOutbox.Add(new EmailOutbox
        {
            UserId = userId, ToEmail = toEmail.Trim(), Subject = Clip(subject, 200), Body = Clip(body, 4000),
            Link = link is { Length: > 300 } ? link[..300] : link, Status = "PENDING", CreatedAt = AppClock.Now
        });
    }

    private static string Clip(string s, int n) => s.Length > n ? s[..n] : s;

    /// <summary>หน้าตาอีเมล (HTML เรียบง่าย อ่านได้ทุกโปรแกรมอีเมล) — เนื้อหาถูก encode ทั้งหมด</summary>
    public static string Html(EmailOutbox m, string publicUrl)
    {
        static string E(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
        var link = m.Link is { Length: > 0 } l ? (l.StartsWith("http") ? l : publicUrl + l) : null;
        var paras = string.Join("", (m.Body ?? "").Split('\n').Select(p => $"<p style=\"margin:0 0 12px\">{E(p)}</p>"));
        return $@"<!doctype html><html lang=""th""><body style=""margin:0;background:#f4f1f8;font-family:Tahoma,Arial,sans-serif;color:#2b2240"">
<div style=""max-width:560px;margin:0 auto;padding:24px"">
<div style=""font-family:Georgia,serif;font-size:26px;color:#3d1a5c"">Aurelle<span style=""color:#b8913a;font-style:italic"">Stay</span></div>
<div style=""background:#fff;border-radius:14px;padding:24px;margin-top:14px;border:1px solid #e6dcf0"">
<h2 style=""margin:0 0 14px;font-size:19px;color:#3d1a5c"">{E(m.Subject)}</h2>{paras}
{(link == null ? "" : $@"<p style=""margin:18px 0 4px""><a href=""{E(link)}"" style=""display:inline-block;background:#3d1a5c;color:#fff;text-decoration:none;padding:11px 20px;border-radius:999px"">เปิดดูในเว็บไซต์</a></p>")}
</div><p style=""font-size:12px;color:#7a6f8a;margin-top:14px"">อีเมลนี้ส่งอัตโนมัติจากระบบ AurelleStay กรุณาอย่าตอบกลับ</p></div></body></html>";
    }

    public static string StatusLabel(string s) => s switch
    {
        "SENT" => "ส่งแล้ว", "FAILED" => "ส่งไม่สำเร็จ", "SAVED" => "เก็บไว้ (ยังไม่ตั้งค่า SMTP)", _ => "รอส่ง"
    };
}

/// <summary>ตัวส่งอีเมลเบื้องหลัง: อ่านกล่องขาออกทุก 15 วินาที ส่งผ่าน SMTP (ลองซ้ำสูงสุด 3 ครั้ง)</summary>
public sealed class OutboxSender : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<OutboxSender> _log;
    private readonly MailSettings _mail;
    public OutboxSender(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<OutboxSender> log)
    { _scopes = scopes; _log = log; _mail = MailSettings.Load(cfg); }

    public static async Task<int> RunOnceAsync(Csi402dbContext db, MailSettings mail, ILogger? log = null)
    {
        var batch = await db.EmailOutbox.Where(m => m.Status == "PENDING" || (m.Status == "FAILED" && m.Attempts < 3))
            .OrderBy(m => m.EmailId).Take(20).ToListAsync();
        if (batch.Count == 0) return 0;
        foreach (var m in batch)
        {
            // ที่อยู่ภายในระบบ (.local) และกรณีไม่ได้ตั้ง SMTP: เก็บไว้ให้ดูในเว็บ ไม่ส่งออก
            if (!mail.Enabled || m.ToEmail.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || m.ToEmail.EndsWith("@t.example", StringComparison.OrdinalIgnoreCase))
            {
                m.Status = "SAVED"; continue;
            }
            m.Attempts++;
            try
            {
                using var msg = new System.Net.Mail.MailMessage(new System.Net.Mail.MailAddress(mail.From, mail.FromName, System.Text.Encoding.UTF8),
                    new System.Net.Mail.MailAddress(m.ToEmail))
                {
                    Subject = m.Subject, SubjectEncoding = System.Text.Encoding.UTF8,
                    Body = Mailer.Html(m, mail.PublicUrl), BodyEncoding = System.Text.Encoding.UTF8, IsBodyHtml = true
                };
                using var smtp = new System.Net.Mail.SmtpClient(mail.Host!, mail.Port) { EnableSsl = mail.EnableSsl, Timeout = 20000 };
                if (mail.User != null) smtp.Credentials = new System.Net.NetworkCredential(mail.User, mail.Password);
                await smtp.SendMailAsync(msg);
                m.Status = "SENT"; m.SentAt = AppClock.Now; m.LastError = null;
            }
            catch (Exception ex)
            {
                m.Status = "FAILED";
                var err = ex.GetBaseException().Message;
                m.LastError = err.Length > 300 ? err[..300] : err;
                log?.LogWarning("ส่งอีเมล #{Id} ไม่สำเร็จ: {Err}", m.EmailId, m.LastError);
            }
        }
        await db.SaveChangesAsync();
        return batch.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            do
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<Csi402dbContext>();
                    await RunOnceAsync(db, _mail, _log);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogWarning(ex, "ตัวส่งอีเมลทำงานไม่สำเร็จรอบนี้ จะลองใหม่รอบถัดไป");
                }
            } while (await timer.WaitForNextTickAsync(stop));
        }
        catch (OperationCanceledException) { }
    }
}

public static class PasswordResets
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public static string HashToken(string token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>สร้างโทเคน (ส่งในลิงก์) และเก็บเฉพาะค่าแฮช · ยกเลิกโทเคนเก่าที่ยังไม่ได้ใช้ของผู้ใช้คนนี้</summary>
    public static async Task<string> CreateAsync(Csi402dbContext db, int userId, string? ip)
    {
        var now = AppClock.Now;
        await db.PasswordResets.Where(r => r.UserId == userId && r.UsedAt == null && r.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAt, now.AddMinutes(-1)));   // ย้อน 1 นาที กันปัดเศษวินาทีของ MySQL ทำให้ลิงก์เก่ายังใช้ได้ชั่วครู่
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        db.PasswordResets.Add(new PasswordReset { UserId = userId, TokenHash = HashToken(token), ExpiresAt = now + Lifetime, CreatedAt = now, Ip = ip });
        return token;
    }

    /// <summary>โทเคนที่ยังใช้ได้ (ไม่หมดอายุ ไม่เคยใช้) หรือ null</summary>
    public static Task<PasswordReset?> FindValidAsync(Csi402dbContext db, string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return Task.FromResult<PasswordReset?>(null);
        var hash = HashToken(token.Trim());
        var now = AppClock.Now;
        return db.PasswordResets.Include(r => r.User).FirstOrDefaultAsync(r => r.TokenHash == hash && r.UsedAt == null && r.ExpiresAt > now);
    }
}

// ======================================================================
// Part 55: dotnet run คำสั่งเดียว → เปิด Docker Desktop · ฐานข้อมูล MySQL ใน Docker · เว็บใน Docker (8088) · เว็บในเครื่อง
// ======================================================================
/// <summary>
/// ตอนรัน <c>dotnet run</c> (Development บนเครื่องนักพัฒนา): เปิด Docker Desktop ถ้ายังไม่เปิด → สร้าง .env ถ้ายังไม่มี
/// → เปิดฐานข้อมูลโปรเจกต์ "aurellestay" และรอให้พร้อม → ให้เว็บในเครื่องใช้ฐานข้อมูลเดียวกับ Docker
/// → อัปเดตเว็บใน Docker (http://localhost:8088) เบื้องหลัง. แตะเฉพาะโปรเจกต์ Docker ชื่อ aurellestay เท่านั้น
/// ปิดได้ด้วย DevDocker:Enabled=false ใน appsettings หรือ ตัวแปร AURELLE_NO_DOCKER=1
/// </summary>
public static class DevDocker
{
    private const string Project = "aurellestay";

    public static void Prepare(WebApplicationBuilder b)
    {
        if (!b.Environment.IsDevelopment()) return;
        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true") return;
        if (Environment.GetEnvironmentVariable("AURELLE_NO_DOCKER") == "1" || !b.Configuration.GetValue("DevDocker:Enabled", true)) return;
        var root = b.Environment.ContentRootPath;
        if (!File.Exists(Path.Combine(root, "docker-compose.yml"))) return;
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

        void Say(string t) => Console.WriteLine("[AurelleStay] " + t);
        if (!Run("docker", "info", root, 20, out _))
        {
            if (!StartDockerDesktop()) { Say("ไม่พบ Docker — รันเว็บด้วยฐานข้อมูลตาม appsettings.json"); return; }
            Say("กำลังเปิด Docker Desktop ...");
            var ok = false;
            for (var i = 0; i < 60 && !ok; i++) { Thread.Sleep(3000); ok = Run("docker", "info", root, 20, out _); }
            if (!ok) { Say("Docker ยังไม่พร้อม — รันเว็บด้วยฐานข้อมูลตาม appsettings.json"); return; }
        }

        var env = LoadEnv(root, Say);
        Say("เปิดฐานข้อมูล MySQL ใน Docker ...");
        if (!Run("docker", $"compose -p {Project} up -d db", root, 900, out var err))
        { Say("เปิดฐานข้อมูลใน Docker ไม่สำเร็จ: " + err.Trim()); return; }

        var port = env.GetValueOrDefault("DB_HOST_PORT", "3317");
        var cs = $"server=127.0.0.1;port={port};database=csi402db;user=aurelle;password={env.GetValueOrDefault("DB_PASSWORD", "")};SslMode=None;AllowPublicKeyRetrieval=True";
        var ready = false;
        for (var i = 0; i < 90 && !ready; i++)
        {
            try
            {
                using var c = new MySqlConnector.MySqlConnection(cs); c.Open();
                using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT 1 FROM users LIMIT 1"; cmd.ExecuteScalar(); ready = true;
            }
            catch { Thread.Sleep(2000); }
        }
        if (!ready) { Say("ฐานข้อมูลใน Docker ยังไม่พร้อม — รันเว็บด้วยฐานข้อมูลตาม appsettings.json"); return; }

        // ใช้ฐานข้อมูล + ค่าตั้งค่าเดียวกับเว็บใน Docker (เฉพาะค่าที่ตั้งไว้ใน .env)
        var map = new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = cs,
            // ไม่พิมพ์ SQL ทุกคำสั่งลงหน้าจอ (ตัวส่งอีเมล/งานเบื้องหลังรันทุก 15 วินาที ทำให้อ่านข้อความสำคัญไม่ออก)
            ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "Warning" };
        foreach (var (k, key) in new[] { ("HOTEL_NAME", "Hotel:Name"), ("HOTEL_PHONE", "Hotel:Phone"), ("HOTEL_EMAIL", "Hotel:Email"), ("HOTEL_LINE", "Hotel:LineId"),
                     ("HOTEL_ADDRESS", "Hotel:Address"), ("PROMPTPAY_ID", "Payment:PromptPayId"), ("PAY_ACCOUNT_NAME", "Payment:AccountName"),
                     ("PAY_BANK_NAME", "Payment:BankName"), ("PAY_ACCOUNT_NO", "Payment:AccountNo"), ("SMTP_HOST", "Email:Host"), ("SMTP_PORT", "Email:Port"),
                     ("SMTP_USER", "Email:User"), ("SMTP_PASSWORD", "Email:Password"), ("SMTP_FROM", "Email:From") })
            if (env.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v)) map[key] = v;
        b.Configuration.AddInMemoryCollection(map);
        Say($"ฐานข้อมูลพร้อม (Docker, พอร์ต {port}) — เว็บในเครื่องใช้ข้อมูลชุดเดียวกับ Docker");

        // อัปเดตเว็บใน Docker ให้เป็นโค้ดล่าสุด (เบื้องหลัง ไม่ต้องรอ)
        try
        {
            var log = Path.Combine(Path.GetTempPath(), "aurellestay-docker-build.log");
            var p = new System.Diagnostics.ProcessStartInfo("docker", $"compose -p {Project} up -d --build web")
            { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            var proc = System.Diagnostics.Process.Start(p)!;
            _ = Task.Run(async () =>
            {
                var o = await proc.StandardOutput.ReadToEndAsync(); var e = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();
                try { await File.WriteAllTextAsync(log, o + e); } catch { }
                Say(proc.ExitCode == 0 ? $"เว็บใน Docker อัปเดตแล้ว: http://localhost:{env.GetValueOrDefault("WEB_PORT", "8088")}"
                                       : "อัปเดตเว็บใน Docker ไม่สำเร็จ ดู " + log);
            });
            Say($"กำลังอัปเดตเว็บใน Docker เบื้องหลัง (http://localhost:{env.GetValueOrDefault("WEB_PORT", "8088")})");
        }
        catch { }
    }

    private static bool Run(string file, string args, string dir, int timeoutSec, out string error)
    {
        error = "";
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, args)
            { WorkingDirectory = dir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
            var err = p.StandardError.ReadToEndAsync(); _ = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(timeoutSec * 1000)) { try { p.Kill(true); } catch { } error = "หมดเวลา"; return false; }
            error = err.Result;
            return p.ExitCode == 0;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    private static bool StartDockerDesktop()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var exe = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DockerDesktop", "Docker Desktop.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "Docker Desktop.exe")
                }.FirstOrDefault(File.Exists);
                if (exe == null) return false;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
                return true;
            }
            if (OperatingSystem.IsMacOS()) { System.Diagnostics.Process.Start("open", "-a Docker"); return true; }
        }
        catch { }
        return false;
    }

    /// <summary>อ่าน .env (สร้างจาก .env.example พร้อมรหัสผ่านสุ่ม ถ้ายังไม่มี — เหมือน docker.ps1)</summary>
    private static Dictionary<string, string> LoadEnv(string root, Action<string> say)
    {
        var path = Path.Combine(root, ".env");
        if (!File.Exists(path) && File.Exists(Path.Combine(root, ".env.example")))
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
            string Pw() => new string(Enumerable.Range(0, 24).Select(_ => chars[System.Security.Cryptography.RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
            var text = File.ReadAllText(Path.Combine(root, ".env.example"));
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^DB_ROOT_PASSWORD=.*$", "DB_ROOT_PASSWORD=" + Pw());
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^DB_PASSWORD=.*$", "DB_PASSWORD=" + Pw());
            File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
            say("สร้างไฟล์ .env (รหัสผ่านฐานข้อมูลสุ่มใหม่ เก็บในเครื่องนี้เท่านั้น)");
        }
        var d = new Dictionary<string, string>();
        if (File.Exists(path))
            foreach (var line in File.ReadAllLines(path))
            {
                var t = line.Trim();
                if (t.Length == 0 || t[0] == '#') continue;
                var i = t.IndexOf('='); if (i <= 0) continue;
                d[t[..i].Trim()] = t[(i + 1)..].Trim();
            }
        return d;
    }
}
