using System.Net;
using System.Net.Mail;
using System.Threading.Channels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using pameka_7033_l005.Models;

namespace pameka_7033_l005.Helpers;

// Part 52: ระบบอีเมล (SMTP) — ลืมรหัสผ่านแล้วรีเซ็ตเอง, ส่งการแจ้งเตือนทางอีเมล, ตอบกลับข้อความติดต่อ
// ไม่ตั้งค่า Email:Host = ปิด (ระบบทำงานเหมือนเดิมทุกอย่าง ไม่มีอะไรพัง)

/// <summary>ค่าตั้ง SMTP จาก appsettings หมวด Email (หรือ .env ของ Docker: SMTP_HOST ...)</summary>
public sealed record EmailSettings(string? Host, int Port, string? User, string? Password, string? From, string FromName, bool UseSsl, string? PublicUrl)
{
    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    public static EmailSettings Load(IConfiguration c)
    {
        var user = Blank(c["Email:User"]);
        return new(Blank(c["Email:Host"]), c.GetValue("Email:Port", 587), user, c["Email:Password"],
            Blank(c["Email:From"]) ?? user, Blank(c["Email:FromName"]) ?? Blank(c["Hotel:Name"]) ?? "AurelleStay",
            c.GetValue("Email:UseSsl", true),
            Blank(c["App:PublicUrl"]) is { } u && Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                ? uri.GetLeftPart(UriPartial.Path).TrimEnd('/') : null);
    }

    /// <summary>เปิดใช้เมื่อมีเซิร์ฟเวอร์และอีเมลผู้ส่ง</summary>
    public bool Enabled => Host != null && From != null;
}

public sealed record EmailMessage(string To, string Subject, string Heading, string Body, string? LinkUrl = null, string? LinkText = null);

/// <summary>คิวอีเมล: คำขอเว็บใส่แล้วกลับทันที (ไม่รอ SMTP) — งานเบื้องหลังส่งให้</summary>
public sealed class EmailQueue
{
    private readonly Channel<EmailMessage> _ch = Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });
    public bool TryEnqueue(EmailMessage m) => _ch.Writer.TryWrite(m);
    public ChannelReader<EmailMessage> Reader => _ch.Reader;
}

/// <summary>ส่งอีเมลผ่าน SMTP (Gmail: smtp.gmail.com พอร์ต 587 + App Password)</summary>
public sealed class EmailSender
{
    private readonly EmailSettings _s;
    public EmailSender(EmailSettings s) => _s = s;

    public async Task SendAsync(EmailMessage m, CancellationToken ct = default)
    {
        if (!_s.Enabled) throw new InvalidOperationException("ยังไม่ได้ตั้งค่าอีเมล (Email:Host / Email:From)");
        using var msg = new MailMessage
        {
            From = new MailAddress(_s.From!, _s.FromName),
            Subject = m.Subject,
            SubjectEncoding = System.Text.Encoding.UTF8,
            BodyEncoding = System.Text.Encoding.UTF8
        };
        msg.To.Add(new MailAddress(m.To));
        // multipart/alternative: ข้อความล้วนก่อน แล้ว HTML เป็นส่วนสุดท้าย (โปรแกรมอีเมลแสดงส่วนสุดท้ายที่รองรับ)
        msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(Text(m), System.Text.Encoding.UTF8, "text/plain"));
        msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(Html(m), System.Text.Encoding.UTF8, "text/html"));
        using var client = new SmtpClient(_s.Host!, _s.Port) { EnableSsl = _s.UseSsl, DeliveryMethod = SmtpDeliveryMethod.Network, Timeout = 20000 };
        if (_s.User != null) client.Credentials = new NetworkCredential(_s.User, _s.Password ?? "");
        await client.SendMailAsync(msg, ct);
    }

    /// <summary>ลิงก์เต็มสำหรับใส่ในอีเมล (ต้องตั้ง App:PublicUrl ไม่เช่นนั้นไม่ใส่ลิงก์)</summary>
    public string? Absolute(string? path) =>
        path == null || _s.PublicUrl == null ? null : path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : _s.PublicUrl + (path.StartsWith('/') ? path : "/" + path);

    private string Text(EmailMessage m) =>
        $"{m.Heading}\n\n{m.Body}\n" + (m.LinkUrl != null ? $"\n{m.LinkText ?? "เปิดลิงก์"}: {m.LinkUrl}\n" : "") + $"\n— {_s.FromName}\n(อีเมลอัตโนมัติ กรุณาอย่าตอบกลับ)";

    private string Html(EmailMessage m)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        var body = E(m.Body).Replace("\n", "<br>");
        var button = m.LinkUrl == null ? "" :
            $@"<p style=""margin:28px 0""><a href=""{E(m.LinkUrl)}"" style=""background:#45296b;color:#fff;text-decoration:none;padding:12px 24px;border-radius:8px;display:inline-block;font-weight:600"">{E(m.LinkText ?? "เปิดดู")}</a></p>
<p style=""font-size:12px;color:#655f73;word-break:break-all"">ถ้าปุ่มกดไม่ได้ คัดลอกลิงก์นี้ไปเปิด: {E(m.LinkUrl)}</p>";
        return $@"<!doctype html><html lang=""th""><body style=""margin:0;background:#f3f0f6;font-family:Tahoma,'Segoe UI',sans-serif;color:#1d1726"">
<div style=""max-width:560px;margin:0 auto;padding:24px"">
<div style=""background:#2d1b47;color:#f3ecd9;padding:18px 24px;border-radius:10px 10px 0 0;font-size:22px;font-weight:600;border-bottom:3px solid #b08a35"">{E(_s.FromName)}</div>
<div style=""background:#fffdf8;padding:24px;border-radius:0 0 10px 10px;border:1px solid #ddd5e6;border-top:0"">
<h1 style=""font-size:19px;margin:0 0 14px;color:#2d1b47"">{E(m.Heading)}</h1>
<p style=""line-height:1.7;margin:0"">{body}</p>{button}
</div>
<p style=""font-size:12px;color:#655f73;text-align:center;margin-top:14px"">อีเมลอัตโนมัติจาก {E(_s.FromName)} กรุณาอย่าตอบกลับอีเมลนี้</p>
</div></body></html>";
    }
}

/// <summary>
/// งานเบื้องหลังส่งอีเมล: (1) ส่งจากคิว (รีเซ็ตรหัสผ่าน / ตอบกลับข้อความ / ทดสอบ)
/// (2) ทุก 20 วินาที ส่งการแจ้งเตือนในระบบ (ตาราง notifications) ที่ยังไม่ได้ส่งเป็นอีเมล — ส่งได้แล้วจึงบันทึก emailed_at
/// </summary>
public sealed class EmailWorker : BackgroundService
{
    private readonly EmailQueue _queue;
    private readonly EmailSender _sender;
    private readonly EmailSettings _settings;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<EmailWorker> _log;
    private readonly bool _notify;

    public EmailWorker(EmailQueue q, EmailSender sender, EmailSettings s, IServiceScopeFactory scopes, IConfiguration cfg, ILogger<EmailWorker> log)
    { _queue = q; _sender = sender; _settings = s; _scopes = scopes; _log = log; _notify = cfg.GetValue("Email:SendNotifications", true); }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!_settings.Enabled) { _log.LogInformation("ระบบอีเมลปิดอยู่ (ยังไม่ได้ตั้ง Email:Host / Email:From)"); return; }
        _log.LogInformation("ระบบอีเมลเปิดใช้งาน: {Host}:{Port} ผู้ส่ง {From}", _settings.Host, _settings.Port, _settings.From);
        var queueTask = Task.Run(() => DrainQueueAsync(stop), stop);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        try
        {
            do
            {
                if (!_notify) continue;
                try { await SendNotificationsAsync(stop); }
                catch (Exception ex) when (!stop.IsCancellationRequested) { _log.LogWarning(ex, "ส่งอีเมลแจ้งเตือนไม่สำเร็จ จะลองใหม่รอบถัดไป"); }
            } while (await timer.WaitForNextTickAsync(stop));
        }
        catch (OperationCanceledException) { }
        try { await queueTask; } catch (OperationCanceledException) { }
    }

    private async Task DrainQueueAsync(CancellationToken stop)
    {
        await foreach (var m in _queue.Reader.ReadAllAsync(stop))
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try { await _sender.SendAsync(m, stop); break; }
                catch (Exception ex) when (!stop.IsCancellationRequested)
                {
                    _log.LogWarning(ex, "ส่งอีเมล \"{Subject}\" ไม่สำเร็จ (ครั้งที่ {Attempt})", m.Subject, attempt);
                    await Task.Delay(TimeSpan.FromSeconds(5 * attempt), stop);
                }
            }
        }
    }

    private async Task SendNotificationsAsync(CancellationToken stop)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Csi402dbContext>();
        // เฉพาะของใหม่ (ไม่เกิน 2 ชม.) — เปิดระบบอีเมลครั้งแรกจะไม่ส่งการแจ้งเตือนเก่าย้อนหลังเป็นกอง
        var since = AppClock.Now.AddHours(-2);
        var rows = await db.Notifications.Where(n => n.EmailedAt == null && n.CreatedAt >= since)
            .OrderBy(n => n.NotificationId).Take(30)
            .Join(db.Users, n => n.UserId, u => u.UserId, (n, u) => new { n.NotificationId, n.Title, n.Body, n.Link, u.Email, u.FirstName })
            .ToListAsync(stop);
        foreach (var r in rows)
        {
            if (DemoData.IsDemoEmail(r.Email))   // บัญชีตัวอย่าง (โดเมนสมมติ) ไม่ต้องส่งจริง
            {
                await MarkAsync(db, r.NotificationId, stop); continue;
            }
            await _sender.SendAsync(new EmailMessage(r.Email, r.Title, r.Title, $"เรียน คุณ{r.FirstName}\n\n{r.Body}",
                _sender.Absolute(r.Link), "ดูรายละเอียด"), stop);
            await MarkAsync(db, r.NotificationId, stop);
        }
    }

    private static Task<int> MarkAsync(Csi402dbContext db, long id, CancellationToken ct) =>
        db.Notifications.Where(n => n.NotificationId == id).ExecuteUpdateAsync(s => s.SetProperty(n => n.EmailedAt, AppClock.Now), ct);
}

/// <summary>โทเคนรีเซ็ตรหัสผ่าน: เข้ารหัส (userId|ตราเซสชัน) หมดอายุ 30 นาที · ใช้ได้ครั้งเดียว (ตั้งรหัสใหม่ = ตราเปลี่ยน โทเคนเก่าใช้ไม่ได้)</summary>
public static class PasswordReset
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private const string Purpose = "AurelleStay.PasswordReset.v1";

    private static ITimeLimitedDataProtector P(IServiceProvider sp) =>
        sp.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose).ToTimeLimitedDataProtector();

    public static string Create(IServiceProvider sp, User u) => P(sp).Protect($"{u.UserId}|{u.SecurityStamp ?? ""}", Lifetime);

    /// <summary>คืนผู้ใช้ (tracked) ถ้าโทเคนถูกต้อง ยังไม่หมดอายุ และยังไม่ถูกใช้</summary>
    public static async Task<User?> ValidateAsync(IServiceProvider sp, Csi402dbContext db, string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 1000) return null;
        string payload;
        try { payload = P(sp).Unprotect(token); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
        var parts = payload.Split('|', 2);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var uid)) return null;
        var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == uid);
        return user != null && (user.SecurityStamp ?? "") == parts[1] ? user : null;
    }
}
