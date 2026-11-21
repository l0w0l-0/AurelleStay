using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using pameka_7033_l005.Models;

namespace pameka_7033_l005.Helpers;

// Part 53: เชื่อมบริการภายนอก — ทุกตัว "ปิด" จนกว่าจะใส่ค่าใน .env (ไม่ใส่ = ระบบทำงานแบบเดิมครบ)
//   SlipOK     ตรวจสลิปอัตโนมัติ → ยอด/บัญชีผู้รับ/สลิปไม่ซ้ำ ผ่านครบ = ยืนยันการจองทันที (ไม่ผ่าน = ส่งให้ฝ่ายบัญชีตรวจเองเหมือนเดิม)
//   Opn/Omise  QR พร้อมเพย์ผ่าน payment gateway → เงินเข้าแล้วยืนยันการจองเอง (ตรวจผลจาก Omise เสมอ ไม่เชื่อข้อมูลจากเบราว์เซอร์/webhook ตรง ๆ)
//   LINE       แจ้งเตือนพนักงานในกลุ่ม LINE (สลิปใหม่ / ยืนยันอัตโนมัติ / ข้อความติดต่อ / คำขอคืนเงิน)
//   Maps       แผนที่ Google ในหน้าเกี่ยวกับเรา (แบบฝัง ไม่ต้องใช้ key)

public sealed record IntegrationSettings(
    string? SlipOkBranch, string? SlipOkKey, bool SlipOkAutoApprove,
    string? OmiseSecretKey,
    string? LineToken, string? LineTo,
    string? MapQuery,
    string SlipOkBaseUrl = "https://api.slipok.com", string OmiseBaseUrl = "https://api.omise.co", string LineBaseUrl = "https://api.line.me")
{
    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    public static IntegrationSettings Load(IConfiguration c) => new(
        Blank(c["SlipOk:BranchId"]), Blank(c["SlipOk:ApiKey"]), c.GetValue("SlipOk:AutoApprove", true),
        Blank(c["Omise:SecretKey"]),
        Blank(c["Line:ChannelAccessToken"]), Blank(c["Line:To"]),
        Blank(c["Hotel:MapQuery"]),
        (Blank(c["SlipOk:BaseUrl"]) ?? "https://api.slipok.com").TrimEnd('/'),
        (Blank(c["Omise:BaseUrl"]) ?? "https://api.omise.co").TrimEnd('/'),
        (Blank(c["Line:BaseUrl"]) ?? "https://api.line.me").TrimEnd('/'));

    public bool SlipOk => SlipOkBranch != null && SlipOkKey != null;
    public bool Omise => OmiseSecretKey != null;
    public bool Line => LineToken != null && LineTo != null;
    public bool Map => MapQuery != null;
    public string? MapEmbedUrl => MapQuery == null ? null : "https://www.google.com/maps?output=embed&q=" + Uri.EscapeDataString(MapQuery);
}

// ======================================================================
// ยืนยันการชำระ (ใช้ร่วม: ฝ่ายบัญชีกดเอง / SlipOK อัตโนมัติ / gateway)
// ======================================================================
public static class PaymentConfirm
{
    /// <summary>ตั้งรายการชำระเป็น PAID + การจองเป็น CONFIRMED + แจ้งลูกค้า (ยังไม่ SaveChanges) · handledBy = null คือระบบอัตโนมัติ</summary>
    public static void Apply(Csi402dbContext db, Payment p, int? handledBy, string? via = null)
    {
        p.Status = "PAID";
        p.PaidAt = AppClock.Now;
        p.HandledBy = handledBy;
        p.Booking.Status = "CONFIRMED";
        p.Booking.ConfirmedBy = handledBy;
        Notify.Add(db, p.Booking.UserId, $"ยืนยันการชำระเงินแล้ว · การจอง #{p.BookingId}",
            $"ได้รับยอด {p.Amount:N2} บาท{(via == null ? "" : $" ({via})")} การจองของคุณได้รับการยืนยันแล้ว เข้าพัก {p.Booking.CheckIn:dd/MM/yyyy}",
            Notify.BookingLink(p.BookingId));
    }
}

// ======================================================================
// SlipOK — ตรวจสลิปอัตโนมัติ
// ======================================================================
public sealed record SlipCheck(bool Valid, decimal? Amount, string? TransRef, string Message);

public sealed class SlipVerifier
{
    private readonly IHttpClientFactory _http;
    private readonly IntegrationSettings _s;
    private readonly ILogger<SlipVerifier> _log;
    public SlipVerifier(IHttpClientFactory http, IntegrationSettings s, ILogger<SlipVerifier> log) { _http = http; _s = s; _log = log; }

    /// <summary>
    /// ส่งรูปสลิปให้ SlipOK ตรวจ (log=true: SlipOK ตรวจบัญชีผู้รับที่ผูกกับสาขา และกันสลิปซ้ำ) พร้อมยอดที่คาดไว้
    /// ผลที่ใช้ยืนยันได้ต้อง success = true ทั้งชั้นนอกและ data — นอกนั้นถือว่า "ไม่ผ่าน" ให้คนตรวจ
    /// </summary>
    public async Task<SlipCheck> CheckAsync(string filePath, decimal expected, CancellationToken ct)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            var bytes = await File.ReadAllBytesAsync(filePath, ct);
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(Path.GetExtension(filePath).ToLowerInvariant() switch
            { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" });
            form.Add(file, "files", Path.GetFileName(filePath));
            form.Add(new StringContent("true"), "log");
            form.Add(new StringContent(expected.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)), "amount");

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_s.SlipOkBaseUrl}/api/line/apikey/{Uri.EscapeDataString(_s.SlipOkBranch!)}") { Content = form };
            req.Headers.Add("x-authorization", _s.SlipOkKey);
            using var res = await _http.CreateClient("slipok").SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var data = root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? d : default;
            decimal? amount = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("amount", out var a) && a.TryGetDecimal(out var av) ? av : null;
            string? transRef = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("transRef", out var t) ? t.GetString() : null;
            var ok = res.IsSuccessStatusCode && root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True
                     && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("success", out var ds) && ds.ValueKind == JsonValueKind.True;
            var msg = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()! : ok ? "สลิปถูกต้อง" : "ตรวจไม่ผ่าน";
            var code = root.TryGetProperty("code", out var c) ? c.ToString() : null;
            return new SlipCheck(ok, amount, transRef, code == null ? msg : $"{msg} (รหัส {code})");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            _log.LogWarning(ex, "ตรวจสลิปกับ SlipOK ไม่สำเร็จ");
            return new SlipCheck(false, null, null, "เชื่อมต่อระบบตรวจสลิปไม่สำเร็จ");
        }
    }

    /// <summary>
    /// ตรวจสลิปของรายการรอตรวจ แล้วยืนยันการจองให้เอง ถ้า: สลิปถูกต้อง + ยอดตรงทุกสตางค์ + เลขธุรกรรมไม่เคยใช้ในระบบ + การจองยังรอชำระ + ยังไม่เลยวันออก
    /// คืนข้อความสำหรับลูกค้า · ไม่ผ่าน = ไม่ปฏิเสธเอง (ปล่อยให้ฝ่ายบัญชีตรวจ พร้อมบันทึกผลตรวจไว้ในรายการ)
    /// </summary>
    public async Task<(bool Approved, string Message)> VerifyPaymentAsync(Csi402dbContext db, int paymentId, CancellationToken ct)
    {
        var p = await db.Payments.Include(x => x.Booking).FirstOrDefaultAsync(x => x.PaymentId == paymentId, ct);
        if (p?.SlipPath == null || p.Status != "PENDING" || p.SlipPath.EndsWith(".pdf")) return (false, "");
        var file = UploadStore.OpenSlip(p.SlipPath);
        if (file == null) return (false, "");

        var r = await CheckAsync(file.Value.Path, p.Amount, ct);
        string note;
        var approve = false;
        if (!r.Valid) note = $"SlipOK: {r.Message}";
        else if (r.Amount is not { } amt || Math.Abs(amt - p.Amount) >= 0.01m) note = $"SlipOK: ยอดในสลิป {r.Amount:N2} ไม่ตรงกับยอดจอง {p.Amount:N2}";
        else if (r.TransRef != null && await db.Payments.AnyAsync(x => x.TransRef == r.TransRef && x.PaymentId != p.PaymentId && x.Status != "REJECTED", ct))
            note = $"SlipOK: เลขธุรกรรม {r.TransRef} เคยใช้กับรายการอื่นแล้ว (สลิปซ้ำ)";
        else if (p.Booking.Status != "PENDING" || p.Booking.CheckOut <= DateOnly.FromDateTime(AppClock.Today)) note = "SlipOK: สลิปถูกต้อง แต่การจองไม่อยู่ในสถานะรอชำระ";
        else { note = $"SlipOK: ตรวจผ่าน ยอด {r.Amount:N2} เลขธุรกรรม {r.TransRef}"; approve = _s.SlipOkAutoApprove; }

        p.VerifyNote = note.Length > 300 ? note[..300] : note;
        p.TransRef = r.TransRef is { Length: > 64 } tr ? tr[..64] : r.TransRef;
        if (approve) PaymentConfirm.Apply(db, p, null, "ตรวจสลิปอัตโนมัติ");
        await db.SaveChangesAsync(ct);
        return approve ? (true, "ตรวจสลิปอัตโนมัติผ่านแล้ว การจองของคุณได้รับการยืนยันทันที")
                       : (false, "ส่งสลิปแล้ว ระบบตรวจอัตโนมัติยังยืนยันไม่ได้ ฝ่ายบัญชีจะตรวจสอบและยืนยันให้");
    }
}

// ======================================================================
// Opn Payments (Omise) — QR พร้อมเพย์ผ่าน gateway
// ======================================================================
public sealed record GatewayCharge(string Id, string Status, long AmountSatang, int? BookingId, string? QrUri, DateTime? ExpiresAt);

public sealed class OmiseGateway
{
    private readonly IHttpClientFactory _http;
    private readonly IntegrationSettings _s;
    public OmiseGateway(IHttpClientFactory http, IntegrationSettings s) { _http = http; _s = s; }

    public static bool IsChargeId(string? id) => id != null && System.Text.RegularExpressions.Regex.IsMatch(id, "^chrg_[A-Za-z0-9_]{6,80}$");

    private HttpClient Client()
    {
        var c = _http.CreateClient("omise");
        // Basic auth: secret key เป็นชื่อผู้ใช้ รหัสผ่านว่าง
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(_s.OmiseSecretKey + ":")));
        return c;
    }

    /// <summary>สร้าง charge พร้อมเพย์ (ยอดเป็นสตางค์) ผูก booking_id ไว้ใน metadata · หมดอายุพร้อมกำหนดชำระของการจอง</summary>
    public async Task<GatewayCharge> CreatePromptPayAsync(int bookingId, decimal amount, DateTime expiresLocal, CancellationToken ct)
    {
        var satang = (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
        var expiresUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(expiresLocal, DateTimeKind.Unspecified), BangkokOrLocal());
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["amount"] = satang.ToString(), ["currency"] = "thb",
            ["source[type]"] = "promptpay", ["source[amount]"] = satang.ToString(), ["source[currency]"] = "thb",
            ["description"] = $"AurelleStay booking #{bookingId}",
            ["metadata[booking_id]"] = bookingId.ToString(),
            ["expires_at"] = expiresUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")
        });
        using var res = await Client().PostAsync($"{_s.OmiseBaseUrl}/charges", form, ct);
        return await ParseAsync(res, ct);
    }

    public async Task<GatewayCharge> GetAsync(string chargeId, CancellationToken ct)
    {
        if (!IsChargeId(chargeId)) throw new ArgumentException("charge id");
        using var res = await Client().GetAsync($"{_s.OmiseBaseUrl}/charges/{chargeId}", ct);
        return await ParseAsync(res, ct);
    }

    /// <summary>ดาวน์โหลดรูป QR (ผ่านเซิร์ฟเวอร์เรา เพราะ CSP ไม่ให้โหลดรูปจากโดเมนอื่น) — รับเฉพาะ https ของ omise</summary>
    public async Task<(byte[] Bytes, string ContentType)?> DownloadQrAsync(string uri, CancellationToken ct)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || (u.Scheme != "https" && !uri.StartsWith(_s.OmiseBaseUrl)) ||
            !(u.Host.Equals("omise.co", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".omise.co", StringComparison.OrdinalIgnoreCase)
              || u.Host.EndsWith(".opn.ooo", StringComparison.OrdinalIgnoreCase)
              || u.Authority.Equals(new Uri(_s.OmiseBaseUrl).Authority, StringComparison.OrdinalIgnoreCase))) return null;
        using var res = await Client().GetAsync(u, ct);
        if (!res.IsSuccessStatusCode) return null;
        var type = res.Content.Headers.ContentType?.MediaType ?? "image/png";
        if (!type.StartsWith("image/")) return null;
        return (await res.Content.ReadAsByteArrayAsync(ct), type);
    }

    private static async Task<GatewayCharge> ParseAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var json = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (!res.IsSuccessStatusCode || r.TryGetProperty("object", out var o) && o.GetString() == "error")
            throw new HttpRequestException("Omise: " + (r.TryGetProperty("message", out var m) ? m.GetString() : res.StatusCode.ToString()));
        int? bookingId = r.TryGetProperty("metadata", out var md) && md.ValueKind == JsonValueKind.Object && md.TryGetProperty("booking_id", out var b)
            && int.TryParse(b.ToString(), out var bid) ? bid : null;
        string? qr = r.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.Object
            && src.TryGetProperty("scannable_code", out var sc) && sc.ValueKind == JsonValueKind.Object
            && sc.TryGetProperty("image", out var img) && img.ValueKind == JsonValueKind.Object
            && img.TryGetProperty("download_uri", out var du) ? du.GetString() : null;
        DateTime? exp = r.TryGetProperty("expires_at", out var e) && e.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(e.GetString(), out var eo)
            ? TimeZoneInfo.ConvertTime(eo, BangkokOrLocal()).DateTime : null;
        return new GatewayCharge(r.GetProperty("id").GetString()!, r.GetProperty("status").GetString() ?? "", r.GetProperty("amount").GetInt64(), bookingId, qr, exp);
    }

    private static TimeZoneInfo BangkokOrLocal()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok"); } catch { return TimeZoneInfo.Local; }
    }

    /// <summary>
    /// บันทึกผลจาก Omise (เรียกจากหน้าเว็บที่รอผล และจาก webhook) — อ่านสถานะจาก Omise เองเสมอ, ใช้ได้ซ้ำ (บันทึกครั้งเดียวต่อ charge)
    /// คืน: "paid" (ยืนยันแล้ว), "pending", "failed", "mismatch" (ยอด/การจองไม่ตรง ต้องให้คนตรวจ)
    /// </summary>
    public async Task<string> ApplyAsync(Csi402dbContext db, string chargeId, int? expectBookingId, LineNotifier line, CancellationToken ct)
    {
        var c = await GetAsync(chargeId, ct);
        if (expectBookingId != null && c.BookingId != expectBookingId) return "mismatch";
        if (c.Status is "failed" or "expired" or "reversed") return "failed";
        if (c.Status != "successful" || c.BookingId is not { } bookingId) return "pending";

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // ล็อกแถวการจอง: หน้าเว็บกับ webhook มาพร้อมกันก็บันทึกครั้งเดียว
        await db.Bookings.Where(x => x.BookingId == bookingId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, x => x.Status), ct);
        if (await db.Payments.AnyAsync(x => x.TransRef == chargeId, ct)) { await tx.CommitAsync(ct); return "paid"; }
        var booking = await db.Bookings.Include(x => x.Payments).FirstOrDefaultAsync(x => x.BookingId == bookingId, ct);
        var amount = c.AmountSatang / 100m;
        if (booking == null || booking.Status != "PENDING" || Math.Abs(booking.NetTotal - amount) >= 0.01m
            || booking.Payments.Any(x => x.PaymentType == BookingWorkflow.PaymentType && x.Status == "PAID"))
        {
            await tx.RollbackAsync(ct);
            line.Post($"⚠️ ได้รับเงินผ่าน gateway {amount:N2} บาท (charge {chargeId}) แต่การจอง #{bookingId} ไม่อยู่ในสถานะรอชำระหรือยอดไม่ตรง — ตรวจสอบและคืนเงินใน Opn Dashboard");
            return "mismatch";
        }
        // สลิปที่แนบไว้ก่อนหน้า (ยังรอตรวจ) ไม่ต้องตรวจแล้ว
        foreach (var old in booking.Payments.Where(x => x.PaymentType == BookingWorkflow.PaymentType && x.Status == "PENDING"))
        { old.Status = "REJECTED"; old.PaidAt = AppClock.Now; old.VerifyNote = "ไม่ต้องตรวจ: ชำระผ่าน payment gateway แล้ว"; }
        var p = new Payment { Booking = booking, BookingId = bookingId, Amount = amount, PaymentType = BookingWorkflow.PaymentType, Method = "QR",
            Status = "PENDING", Reference = chargeId, TransRef = chargeId, VerifyNote = "ชำระผ่าน Opn Payments (PromptPay) ยืนยันอัตโนมัติ" };
        booking.Payments.Add(p);
        PaymentConfirm.Apply(db, p, null, "ชำระผ่าน QR อัตโนมัติ");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        line.Post($"✅ การจอง #{bookingId} ชำระผ่าน QR อัตโนมัติ {amount:N2} บาท — ยืนยันการจองแล้ว");
        return "paid";
    }
}

// ======================================================================
// LINE Messaging API — แจ้งเตือนพนักงาน
// ======================================================================
public sealed class LineNotifier
{
    private readonly Channel<string> _ch = Channel.CreateBounded<string>(new BoundedChannelOptions(300) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly IntegrationSettings _s;
    public LineNotifier(IntegrationSettings s) => _s = s;

    /// <summary>ใส่คิวแล้วกลับทันที (ไม่ได้ตั้งค่า LINE = ไม่ทำอะไร)</summary>
    public void Post(string text) { if (_s.Line) _ch.Writer.TryWrite(text); }
    public ChannelReader<string> Reader => _ch.Reader;
}

public sealed class LineWorker : BackgroundService
{
    private readonly LineNotifier _q;
    private readonly IntegrationSettings _s;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<LineWorker> _log;
    public LineWorker(LineNotifier q, IntegrationSettings s, IHttpClientFactory http, ILogger<LineWorker> log) { _q = q; _s = s; _http = http; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!_s.Line) return;
        _log.LogInformation("แจ้งเตือน LINE เปิดใช้งาน");
        try
        {
            await foreach (var text in _q.Reader.ReadAllAsync(stop))
            {
                try
                {
                    var body = JsonSerializer.Serialize(new { to = _s.LineTo, messages = new[] { new { type = "text", text = text.Length > 2000 ? text[..2000] : text } } });
                    using var req = new HttpRequestMessage(HttpMethod.Post, $"{_s.LineBaseUrl}/v2/bot/message/push")
                    { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _s.LineToken);
                    req.Headers.Add("X-Line-Retry-Key", Guid.NewGuid().ToString());
                    using var res = await _http.CreateClient("line").SendAsync(req, stop);
                    if (!res.IsSuccessStatusCode)
                        _log.LogWarning("ส่ง LINE ไม่สำเร็จ: {Status} {Body}", (int)res.StatusCode, await res.Content.ReadAsStringAsync(stop));
                }
                catch (Exception ex) when (!stop.IsCancellationRequested) { _log.LogWarning(ex, "ส่ง LINE ไม่สำเร็จ"); }
            }
        }
        catch (OperationCanceledException) { }
    }
}
