using System.Data;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using UserEntity = pameka_7033_l005.Models.User;
using pameka_7033_l005.Models;
using pameka_7033_l005.ViewModels;
using Microsoft.Extensions.Hosting;

namespace pameka_7033_l005.Helpers;

// ตัวช่วยงานจอง: ราคา/โปรโมชั่น, คำค้นที่ใช้ร่วม, สร้างการจอง, กฎสถานะ, ป้ายภาษาไทย

// ======================================================================
// PricingService
// ======================================================================
/// <summary>โปรโมชั่นหนึ่งรายการ (ตรงกับตาราง promotions) ที่ส่งเข้ามาคำนวณ</summary>
public record PromoRule(int PromoId, string Name, string Type, decimal Percent,
    int? MinAdvanceDays, DateOnly? Start, DateOnly? End, bool NeedStaffConfirm,
    string? Code = null, int? MinNights = null, int? MaxUses = null, int UsedCount = 0, decimal? MaxDiscount = null,
    int? MaxPerUser = null, int UserUsedCount = 0,           // ใช้ได้กี่ครั้งต่อลูกค้า / ลูกค้าคนนี้ใช้ไปแล้วกี่ครั้ง
    bool RecipientsOnly = false, bool IsRecipient = false,  // รหัสเฉพาะผู้ได้รับ
    IReadOnlyCollection<int>? UserYears = null,             // ปีที่ลูกค้าคนนี้ใช้โปรนี้ไปแล้ว (วันเกิด/ครบรอบ ปีละครั้ง)
    bool Claimable = false);                                // Part 49: คูปองต้องกดเก็บก่อน (เก็บแล้ว = IsRecipient)

public record PricingInput(
    IReadOnlyList<decimal> RoomPricesPerNight,   // ราคา/คืนของแต่ละห้องที่เลือก
    DateOnly CheckIn, DateOnly CheckOut,
    decimal ServicesTotal,                        // ยอดบริการเสริมรวม (ไม่ลดราคา)
    DateOnly Today,
    bool IsMember, DateOnly? BirthDate, DateOnly? AnniversaryDate,
    IReadOnlyList<PromoRule> Promos,
    string? EnteredCode = null);                  // รหัสโปรโมชั่นที่ลูกค้ากรอก (ถ้ามี)

public record PricingResult(int Nights, decimal RoomsTotal, decimal ServicesTotal,
    decimal Subtotal, decimal Discount, decimal Net, PromoRule? Applied,
    string? CodeMessage = null, bool CodeApplied = false);

/// <summary>
/// กติกา: ส่วนลดใช้กับ "ค่าห้อง" เท่านั้น (ไม่ลดค่าบริการเสริม)
/// และเลือกโปรที่ให้ส่วนลดสูงที่สุดเพียง 1 รายการต่อการจอง (ไม่ซ้อนกัน)
/// </summary>
/// <summary>อ่านโปรโมชั่นที่เปิดอยู่จากฐานข้อมูล พร้อมนับสิทธิ์ที่ใช้ไปแล้ว (การจองที่ไม่ถูกยกเลิก)</summary>
/// <summary>กันการเดารหัสโปรโมชั่น: กรอกรหัสที่ไม่มีอยู่จริงได้ไม่เกิน 8 ครั้ง / 10 นาที / ผู้ใช้</summary>
public static class PromoCodeGuard
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<DateTime>> Fails = new();
    private const int Max = 8;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public static bool Allow(string key)
    {
        if (!Fails.TryGetValue(key, out var list)) return true;
        lock (list) { list.RemoveAll(t => t < DateTime.UtcNow - Window); return list.Count < Max; }
    }

    public static void Fail(string key)
    {
        var list = Fails.GetOrAdd(key, _ => new List<DateTime>());
        lock (list) list.Add(DateTime.UtcNow);
        if (Fails.Count > 10_000) Fails.Clear();   // กันหน่วยความจำโต
    }
}

public static class PromoCatalog
{
    public static readonly string[] Types = { "MEMBER", "EARLY_BIRD", "SEASONAL", "LONG_STAY", "CODE", "BIRTHDAY", "ANNIVERSARY", "MOTHERS_DAY" };

    public const string RecipientsTableSql = @"CREATE TABLE IF NOT EXISTS `promo_recipients` (
    `recipient_id` bigint NOT NULL AUTO_INCREMENT,
    `promo_id` int NOT NULL,
    `user_id` int NOT NULL,
    `sent_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `sent_by` varchar(150) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    PRIMARY KEY (`recipient_id`),
    UNIQUE KEY `ux_promo_user` (`promo_id`, `user_id`),
    KEY `ix_recipient_user` (`user_id`),
    CONSTRAINT `promo_recipients_ibfk_1` FOREIGN KEY (`promo_id`) REFERENCES `promotions` (`promo_id`) ON DELETE CASCADE,
    CONSTRAINT `promo_recipients_ibfk_2` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;";

    /// <summary>กติกาโปรที่เปิดอยู่ + ข้อมูลเฉพาะของลูกค้าคนนี้ (ใช้ไปกี่ครั้ง / ได้รับรหัสไหม / ใช้ปีไหนไปแล้ว) · userId = 0 = ลูกค้าใหม่</summary>
    public static async Task<List<PromoRule>> ActiveRulesAsync(Csi402dbContext db, int userId = 0)
    {
        var rows = await db.Promotions.AsNoTracking().Where(p => p.IsActive == true)
            .Select(p => new
            {
                P = p,
                Used = p.Bookings.Count(b => b.Status != "CANCELLED"),
                Mine = p.Bookings.Where(b => b.Status != "CANCELLED" && b.UserId == userId).Select(b => b.CheckIn.Year).ToList(),
                Got = p.Recipients.Any(r => r.UserId == userId)
            }).ToListAsync();
        return rows.Select(x => new PromoRule(x.P.PromoId, x.P.PromoName, x.P.PromoType, x.P.DiscountPercent,
            x.P.MinAdvanceDays, x.P.StartDate, x.P.EndDate, x.P.NeedStaffConfirm,
            x.P.PromoCode, x.P.MinNights, x.P.MaxUses, x.Used, x.P.MaxDiscount,
            x.P.MaxPerUser, x.Mine.Count, x.P.RecipientsOnly, x.Got, x.Mine, x.P.Claimable)).ToList();
    }

    /// <summary>โปรที่แสดงต่อสาธารณะ: เปิดอยู่ ไม่ใช่รหัสลับ ไม่หมดช่วง และยังมีสิทธิ์เหลือ</summary>
    public static async Task<List<(Models.Promotion P, int Used)>> PublicAsync(Csi402dbContext db, DateOnly today)
    {
        var rows = await db.Promotions.AsNoTracking()
            .Where(p => p.IsActive == true && (p.PromoType != "CODE" || p.Claimable) && (p.EndDate == null || p.EndDate >= today))
            .Select(p => new { P = p, Used = p.Bookings.Count(b => b.Status != "CANCELLED") })
            .ToListAsync();
        return rows.Where(x => x.P.MaxUses == null || x.Used < x.P.MaxUses)
            .OrderByDescending(x => x.P.DiscountPercent).Select(x => (x.P, x.Used)).ToList();
    }
}

public static class PricingService
{
    public static PricingResult Calculate(PricingInput i)
    {
        var nights = Math.Max(0, i.CheckOut.DayNumber - i.CheckIn.DayNumber);
        var rooms = i.RoomPricesPerNight.Sum() * nights;
        var subtotal = rooms + i.ServicesTotal;

        PromoRule? best = null;
        decimal bestDiscount = 0;
        foreach (var p in i.Promos)
        {
            if (!IsEligible(p, i)) continue;
            var d = DiscountOf(p, rooms);
            if (d > bestDiscount) { best = p; bestDiscount = d; }
        }

        // รหัสโปรโมชั่น: บอกลูกค้าให้ชัดว่าใช้ได้ไหม และถ้าไม่ได้ เพราะอะไร
        string? codeMsg = null; var codeApplied = false;
        var code = NormalizeCode(i.EnteredCode);
        if (code != null)
        {
            var cp = i.Promos.FirstOrDefault(p => p.Type == "CODE" && p.Code == code);
            if (cp == null) codeMsg = "ไม่พบรหัสโปรโมชั่นนี้ หรือรหัสหมดอายุแล้ว";
            else if (best == cp) { codeApplied = true; codeMsg = $"ใช้รหัส {code} แล้ว"; }
            else if (WhyNot(cp, i) is { } why) codeMsg = $"รหัส {code} ใช้ไม่ได้: {why}";
            else codeMsg = $"รหัส {code} ใช้ได้ แต่ \"{best?.Name}\" ให้ส่วนลดมากกว่า ระบบจึงเลือกโปรนั้นให้";
        }
        return new PricingResult(nights, rooms, i.ServicesTotal, subtotal, bestDiscount, subtotal - bestDiscount, best, codeMsg, codeApplied);
    }

    /// <summary>ส่วนลดของโปรนี้ (ปัดเศษ 2 ตำแหน่ง ไม่เกินเพดานส่วนลดต่อการจอง)</summary>
    public static decimal DiscountOf(PromoRule p, decimal roomsTotal)
    {
        var d = Math.Round(roomsTotal * p.Percent / 100m, 2, MidpointRounding.AwayFromZero);
        return p.MaxDiscount is { } cap && cap > 0 && d > cap ? cap : d;
    }

    /// <summary>รหัสโปรโมชั่นรูปแบบมาตรฐาน: ตัด space, ตัวพิมพ์ใหญ่ · ค่าว่าง = null</summary>
    public static string? NormalizeCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

    /// <summary>เหตุผลที่ใช้โปรนี้ไม่ได้ (null = ใช้ได้)</summary>
    public static string? WhyNot(PromoRule p, PricingInput i)
    {
        var nights = i.CheckOut.DayNumber - i.CheckIn.DayNumber;
        if (p.Start is { } s && i.CheckIn < s) return $"ใช้กับการเข้าพักตั้งแต่ {s:dd/MM/yyyy}";
        if (p.End is { } e && i.CheckIn > e) return $"ใช้กับการเข้าพักถึง {e:dd/MM/yyyy}";
        if (p.MinNights is { } mn && nights < mn) return $"ต้องพักอย่างน้อย {mn} คืน";
        if (p.MaxUses is { } mu && p.UsedCount >= mu) return "สิทธิ์ถูกใช้ครบจำนวนแล้ว";
        if (p.Claimable && !p.IsRecipient) return "ต้องกด \"เก็บคูปอง\" ก่อน (หน้าโปรโมชั่น หรือในหน้าจองนี้)";
        if (p.RecipientsOnly && !p.IsRecipient) return "รหัสนี้ใช้ได้เฉพาะลูกค้าที่ได้รับรหัสจากโรงแรม";
        if (p.MaxPerUser is { } mpu && p.UserUsedCount >= mpu) return mpu == 1 ? "คุณใช้รหัสนี้ไปแล้ว (ใช้ได้ 1 ครั้งต่อท่าน)" : $"คุณใช้สิทธิ์นี้ครบ {mpu} ครั้งแล้ว";
        return IsEligible(p, i) ? null : "ไม่ตรงเงื่อนไขของโปรโมชั่น";
    }

    public static bool IsEligible(PromoRule p, PricingInput i)
    {
        if (p.Percent <= 0 || p.Percent > 100) return false;
        if (p.Start is { } s && i.CheckIn < s) return false;
        if (p.End is { } e && i.CheckIn > e) return false;
        var nights = i.CheckOut.DayNumber - i.CheckIn.DayNumber;
        if (p.MinNights is { } mn && nights < mn) return false;
        if (p.MaxUses is { } mu && p.UsedCount >= mu) return false;
        if (p.RecipientsOnly && !p.IsRecipient) return false;
        if (p.Claimable && !p.IsRecipient) return false;   // คูปองกดเก็บ: เก็บแล้วใช้ให้อัตโนมัติ
        if (p.MaxPerUser is { } mpu && p.UserUsedCount >= mpu) return false;
        // วันเกิด/วันครบรอบ: ปีละครั้งต่อลูกค้า (นับปีของวันเข้าพัก)
        if (p.Type is "BIRTHDAY" or "ANNIVERSARY" && p.UserYears?.Contains(i.CheckIn.Year) == true) return false;

        return p.Type switch
        {
            "MEMBER" => i.IsMember,
            "SEASONAL" => p.Start.HasValue && p.End.HasValue,      // แคมเปญตามช่วงวันเข้าพัก
            "LONG_STAY" => nights >= (p.MinNights ?? 3),
            "CODE" => p.Code != null && (NormalizeCode(i.EnteredCode) == p.Code || p.Claimable),   // คูปองที่เก็บแล้วไม่ต้องพิมพ์รหัส
            "EARLY_BIRD" => i.CheckIn.DayNumber - i.Today.DayNumber >= (p.MinAdvanceDays ?? 30),
            "MOTHERS_DAY" => p.Start.HasValue && p.End.HasValue,   // ต้องกำหนดช่วงวันที่เสมอ
            "BIRTHDAY" => i.BirthDate is { } b && StayContainsMonthDay(i.CheckIn, i.CheckOut, b),
            "ANNIVERSARY" => i.AnniversaryDate is { } a && StayContainsMonthDay(i.CheckIn, i.CheckOut, a),
            _ => false
        };
    }

    /// <summary>มีคืนใดคืนหนึ่งในช่วงพัก (วันเข้า .. ก่อนวันออก) ตรงกับวัน/เดือนของ anchor หรือไม่</summary>
    public static bool StayContainsMonthDay(DateOnly checkIn, DateOnly checkOut, DateOnly anchor)
    {
        for (var d = checkIn; d < checkOut; d = d.AddDays(1))
            if (MatchesMonthDay(d, anchor)) return true;
        return false;
    }

    public static bool MatchesMonthDay(DateOnly day, DateOnly anchor)
    {
        if (anchor.Month == 2 && anchor.Day == 29 && !DateTime.IsLeapYear(day.Year))
            return day.Month == 2 && day.Day == 28;   // เกิด 29 ก.พ. ปีไม่อธิกสุรทิน นับเป็น 28 ก.พ.
        return day.Month == anchor.Month && day.Day == anchor.Day;
    }
}

public static class BookingRules
{
    public const int MaxRooms = 5;
    public const int MaxNights = 30;
    public const int MaxAdvanceDays = 365;
    public const int MaxGuests = 30;
    public const int MaxServiceQty = 5;

    /// <summary>คืนข้อความผิดพลาด หรือ null ถ้าวันที่ใช้ได้</summary>
    public static string? ValidateDates(DateOnly? checkIn, DateOnly? checkOut, DateOnly today)
    {
        if (checkIn is null || checkOut is null) return "กรุณาเลือกวันเข้าพักและวันออก";
        if (checkIn < today) return "วันเข้าพักต้องไม่เป็นวันที่ผ่านมาแล้ว";
        if (checkOut <= checkIn) return "วันออกต้องหลังวันเข้าพักอย่างน้อย 1 คืน";
        if (checkOut.Value.DayNumber - checkIn.Value.DayNumber > MaxNights) return $"จองได้ครั้งละไม่เกิน {MaxNights} คืน";
        if (checkIn.Value.DayNumber - today.DayNumber > MaxAdvanceDays) return $"จองล่วงหน้าได้ไม่เกิน {MaxAdvanceDays} วัน";
        return null;
    }
}

public static partial class ThaiLabels
{
    /// <summary>เงื่อนไขโปรโมชั่นเป็นภาษาไทยอ่านง่าย (แสดงหน้าแรก)</summary>
    public static string PromoCondition(string? type, int? minDays, DateOnly? start, DateOnly? end) => type switch
    {
        "MEMBER" => "สมัครสมาชิกฟรี รับส่วนลดทุกการจอง",
        "EARLY_BIRD" => $"จองล่วงหน้าอย่างน้อย {(minDays ?? 30)} วัน",
        "MOTHERS_DAY" or "SEASONAL" => start.HasValue && end.HasValue
            ? $"เข้าพักวันที่ {start:dd/MM/yyyy} – {end:dd/MM/yyyy}"
            : "เข้าพักในช่วงเทศกาล",
        "BIRTHDAY" => "เข้าพักในวันเกิดของคุณ (ตั้งวันเกิดในโปรไฟล์)",
        "ANNIVERSARY" => "เข้าพักในวันครบรอบของคุณ (ตั้งวันครบรอบในโปรไฟล์)",
        "LONG_STAY" => "พักหลายคืน รับส่วนลดเพิ่ม",
        "CODE" => "กรอกรหัสโปรโมชั่นตอนจอง",
        _ => ""
    };

    /// <summary>เงื่อนไขครบทุกข้อของโปร (ประเภท + พักขั้นต่ำ + เพดานส่วนลด + จำนวนสิทธิ์ + ช่วงวันที่)</summary>
    public static string PromoCondition(pameka_7033_l005.Models.Promotion p, int usedCount = 0)
    {
        var parts = new List<string>();
        var main = PromoCondition(p.PromoType, p.MinAdvanceDays, p.StartDate, p.EndDate);
        if (p.PromoType == "LONG_STAY") main = $"พักตั้งแต่ {p.MinNights ?? 3} คืนขึ้นไป";
        if (main != "") parts.Add(main);
        if (p.PromoType != "LONG_STAY" && p.MinNights is { } mn && mn > 1) parts.Add($"พักอย่างน้อย {mn} คืน");
        if (p.PromoType is not ("MOTHERS_DAY" or "SEASONAL") && (p.StartDate.HasValue || p.EndDate.HasValue))
            parts.Add(p.StartDate.HasValue && p.EndDate.HasValue ? $"เข้าพัก {p.StartDate:dd/MM/yyyy} – {p.EndDate:dd/MM/yyyy}"
                : p.StartDate.HasValue ? $"เข้าพักตั้งแต่ {p.StartDate:dd/MM/yyyy}" : $"เข้าพักถึง {p.EndDate:dd/MM/yyyy}");
        if (p.MaxDiscount is { } cap) parts.Add($"ลดสูงสุด {cap:N0} บาท");
        if (p.MaxUses is { } mu) parts.Add(usedCount >= mu ? "สิทธิ์เต็มแล้ว" : $"เหลือ {mu - usedCount} สิทธิ์");
        if (p.PromoType is "BIRTHDAY" or "ANNIVERSARY") parts.Add("ปีละ 1 ครั้ง");
        if (p.MaxPerUser is { } mpu) parts.Add($"ท่านละ {mpu} ครั้ง");
        if (p.RecipientsOnly && !p.Claimable) parts.Add("เฉพาะผู้ได้รับรหัส");
        if (p.Claimable) parts.Add("กดเก็บคูปองก่อน แล้วระบบใช้ให้อัตโนมัติ");
        if (p.NeedStaffConfirm) parts.Add("แสดงบัตรยืนยันตอนเช็คอิน");
        return string.Join(" · ", parts);
    }

    public static string PromoType(string? t) => t switch
    {
        "MEMBER" => "สมาชิก", "EARLY_BIRD" => "จองล่วงหน้า", "MOTHERS_DAY" => "วันแม่", "SEASONAL" => "ช่วงเทศกาล",
        "BIRTHDAY" => "วันเกิด", "ANNIVERSARY" => "วันครบรอบ", "LONG_STAY" => "พักยาว", "CODE" => "รหัสโปรโมชั่น", _ => t ?? ""
    };

    /// <summary>เลือกฉากหน้าต่างโค้งตามชื่อธีมห้อง: sun = รุ่งเช้า, dusk = ยามค่ำ, dawn = ผสม</summary>
    /// <summary>"ม่วง" → "ธีม ม่วง" แต่ "ธีมม่วง" คงเดิม (ไม่ซ้ำคำว่าธีม)</summary>
    public static string ThemeLabel(string? name) => string.IsNullOrWhiteSpace(name) ? "" : name.StartsWith("ธีม") ? name : "ธีม " + name;

    public static string SceneFor(string? themeName)
    {
        var t = (themeName ?? "").ToLowerInvariant();
        if (t.Contains("เหลือง") || t.Contains("ส้ม") || t.Contains("แดด") || t.Contains("yellow") || t.Contains("sun")) return "sun";
        if (t.Contains("ม่วง") || t.Contains("ค่ำ") || t.Contains("purple") || t.Contains("night")) return "dusk";
        return "dawn";
    }

    public static string BookingStatus(string? s) => s switch
    {
        "PENDING" => "รอดำเนินการ",
        "CONFIRMED" => "จองสำเร็จ",
        "CHECKED_IN" => "เข้าพักอยู่",
        "CHECKED_OUT" => "เข้าพักเสร็จสิ้น",
        "CANCELLED" => "ยกเลิกแล้ว",
        "NO_SHOW" => "ไม่มาเข้าพัก",
        _ => s ?? ""
    };
}

// ======================================================================
// BookingQueries
// ======================================================================
/// <summary>คิวรีที่หลาย controller ใช้ร่วมกัน (เขียนที่เดียว กันนิยาม "ห้องว่าง" เพี้ยนระหว่างหน้า)</summary>
public static class BookingQueries
{
    /// <summary>รายละเอียดการจองครบทุกส่วน (ห้อง บริการ การเงิน งานพนักงาน รีวิว) — ownerUserId ไม่ใช่ null = ต้องเป็นการจองของคนนั้นเท่านั้น</summary>
    public static async Task<BookingDetailViewModel?> LoadDetailAsync(Csi402dbContext db, int id, int? ownerUserId)
    {
        var b = await db.Bookings.AsNoTracking().Where(x => x.BookingId == id && (ownerUserId == null || x.UserId == ownerUserId)).Select(x => new
        {
            x.BookingId, x.Status, x.CheckIn, x.CheckOut, x.BookedAt, x.Subtotal, x.DiscountTotal, x.NetTotal,
            Customer = x.User.FirstName + " " + x.User.LastName, x.User.Email, x.User.Phone,
            Promo = x.Promo != null ? x.Promo.PromoName : null,
            Confirmed = x.ConfirmedByNavigation != null ? x.ConfirmedByNavigation.FirstName + " " + x.ConfirmedByNavigation.LastName : null,
            Rooms = x.BookingRooms.Select(r => new { r.RoomId, r.Room.RoomNo, Type = r.Room.Type.TypeName, r.PricePerNight, r.Nights }).ToList(),
            Services = x.BookingServices.Where(s => !s.InStay).Select(s => new { s.Room.RoomNo, Name = s.Service.ServiceName, s.Quantity, s.UnitPrice }).ToList(),
            Pays = x.Payments.OrderBy(p => p.PaymentId).Select(p => new { p.PaymentId, p.PaymentType, p.Method, p.Status, p.Amount, p.PaidAt, By = p.HandledByNavigation != null ? p.HandledByNavigation.FirstName : null, HasSlip = p.SlipPath != null, p.Reference }).ToList(),
            x.Guests,
            Cleaning = x.HousekeepingTasks.Select(t => new { t.Room.RoomNo, t.Status, Who = t.AssignedToNavigation != null ? t.AssignedToNavigation.FirstName : null, t.DoneAt }).ToList(),
            Delivery = x.BookingServices.SelectMany(s => s.DeliveryTasks).Select(t => new { t.Room.RoomNo, t.Status, Who = t.AssignedToNavigation != null ? t.AssignedToNavigation.FirstName : null, t.DoneAt }).ToList(),
            Review = x.Review != null ? new { x.Review.Rating, x.Review.Comment, x.Review.CreatedAt } : null,
            x.ArrivalTime, x.GuestRequest
        }).FirstOrDefaultAsync();
        if (b == null) return null;

        var flags = PaymentFlags.From(b.Pays.Select(p => (p.PaymentType, p.Status)));
        var vm = new BookingDetailViewModel
        {
            BookingId = b.BookingId, Status = b.Status, Customer = b.Customer, Email = b.Email, Phone = b.Phone,
            CheckIn = b.CheckIn, CheckOut = b.CheckOut, BookedAt = b.BookedAt, PromoName = b.Promo,
            Subtotal = b.Subtotal, Discount = b.DiscountTotal, Net = b.NetTotal, ConfirmedBy = b.Confirmed,
            Rooms = b.Rooms.Select(r => (r.RoomNo, r.Type, r.PricePerNight, (int)r.Nights)).ToList(),
            Services = b.Services.Select(s => (s.RoomNo, s.Name, s.Quantity, s.UnitPrice)).ToList(),
            LastRejectedAt = b.Pays.Where(p => p.Status == "REJECTED").Max(p => p.PaidAt),
            Payments = b.Pays.Select(p => (p.PaymentId, p.PaymentType, p.Method, p.Status, p.Amount, p.PaidAt, p.By, p.HasSlip, p.Reference)).ToList(),
            Guests = b.Guests,
            Tasks = b.Delivery.Select(t => ("ส่งของ", t.RoomNo, t.Status, t.Who, t.DoneAt))
                .Concat(b.Cleaning.Select(t => ("ทำความสะอาด", t.RoomNo, t.Status, t.Who, t.DoneAt))).ToList(),
            Review = b.Review == null ? null : (b.Review.Rating, b.Review.Comment, b.Review.CreatedAt),
            Flags = flags,
            RoomIds = b.Rooms.Select(r => r.RoomId).ToList(),
            ArrivalTime = b.ArrivalTime, GuestRequest = b.GuestRequest
        };
        vm.StayLines = await StayBill.LinesAsync(db, b.BookingId);
        return vm;
    }

    // ต้องเป็น List (ไม่ใช่อาร์เรย์): บน .NET 10 อาร์เรย์.Contains ใน query จะถูกตีความเป็น Span แล้ว EF Core 9 แปลเป็น SQL ไม่ได้
    private static readonly List<string> ActiveStatuses = new() { "PENDING", "CONFIRMED", "CHECKED_IN" };

    /// <summary>คืนที่ห้องนี้ไม่ว่าง (มีการจองที่ยังมีผล) ตั้งแต่วันนี้ถึงล่วงหน้า 400 วัน — ใช้ทำเครื่องหมายในปฏิทินเลือกวัน
    /// ข้อมูลเป็นแค่วันที่ ไม่มีข้อมูลผู้จอง (หน้าปฏิทินห้องว่างแสดงข้อมูลระดับเดียวกันต่อสาธารณะอยู่แล้ว)</summary>
    public static async Task<List<string>> BookedNightsAsync(Csi402dbContext db, int roomId)
    {
        var today = DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today);
        var end = today.AddDays(400);
        var stays = await db.BookingRooms.AsNoTracking()
            .Where(br => br.RoomId == roomId && ActiveStatuses.Contains(br.Booking.Status) && br.Booking.CheckIn < end && br.Booking.CheckOut > today)
            .Select(br => new { br.Booking.CheckIn, br.Booking.CheckOut }).ToListAsync();
        var nights = new SortedSet<DateOnly>();
        foreach (var s in stays)
            for (var d = s.CheckIn < today ? today : s.CheckIn; d < s.CheckOut && d < end; d = d.AddDays(1)) nights.Add(d);
        return nights.Select(d => d.ToString("yyyy-MM-dd")).ToList();
    }

    /// <summary>ปฏิทินห้องว่างรายห้องของเดือนหนึ่ง (ดูได้ตั้งแต่เดือนปัจจุบันถึงล่วงหน้า 12 เดือน)</summary>
    public static async Task<AvailabilityPageViewModel> AvailabilityAsync(Csi402dbContext db, int? year, int? month)
    {
        var today = DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today);
        var first = new DateOnly(today.Year, today.Month, 1);
        var last = first.AddMonths(12);
        var m = first;
        if (year is >= 2000 and <= 2100 && month is >= 1 and <= 12) m = new DateOnly(year.Value, month.Value, 1);
        if (m < first) m = first;
        if (m > last) m = last;
        var end = m.AddMonths(1);
        var days = end.DayNumber - m.DayNumber;

        var rooms = await db.Rooms.AsNoTracking().OrderBy(r => r.RoomNo)
            .Select(r => new { r.RoomId, r.RoomNo, r.Type.TypeName, r.Type.BasePrice, r.Status }).ToListAsync();
        var stays = await db.BookingRooms.AsNoTracking()
            .Where(br => ActiveStatuses.Contains(br.Booking.Status) && br.Booking.CheckIn < end && br.Booking.CheckOut > m)
            .Select(br => new { br.RoomId, br.Booking.CheckIn, br.Booking.CheckOut }).ToListAsync();

        return new AvailabilityPageViewModel
        {
            Month = m, Today = today, Days = days, CanPrev = m > first, CanNext = m < last,
            Rows = rooms.Select(r =>
            {
                var busy = new bool[days];
                foreach (var s in stays.Where(s => s.RoomId == r.RoomId))
                    for (var d = Math.Max(0, s.CheckIn.DayNumber - m.DayNumber); d < Math.Min(days, s.CheckOut.DayNumber - m.DayNumber); d++) busy[d] = true;
                return new AvailabilityRowViewModel { RoomId = r.RoomId, RoomNo = r.RoomNo, TypeName = r.TypeName, Price = r.BasePrice, Maintenance = r.Status == "MAINTENANCE", Busy = busy };
            }).ToList()
        };
    }

    /// <summary>ตัวเลขงานที่รอในเมนูหลังบ้าน (key = action ของเมนูพนักงาน หรือชื่อ controller ของเมนูผู้จัดการ)</summary>
    public static async Task<Dictionary<string, int>> MenuCountsAsync(Csi402dbContext db, string? role)
    {
        var today = DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today);
        var c = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            switch (role?.ToLowerInvariant())
            {
                case RoleNames.Accounting:
                    c["Index"] = await db.Payments.CountAsync(p => p.PaymentType == BookingWorkflow.PaymentType && p.Status == "PENDING");
                    c["Refunds"] = await db.Payments.CountAsync(p => p.PaymentType == BookingWorkflow.RefundType && p.Status == "PENDING");
                    break;
                case RoleNames.FrontDesk:   // ต้องเช็คอินวันนี้ (ยืนยันแล้ว ถึงวันเข้าพัก) + ต้องเช็คเอาท์ (ถึง/เลยวันออก)
                    c["Index"] = await db.Bookings.CountAsync(b => (b.Status == "CONFIRMED" && b.CheckIn <= today && b.CheckOut > today) || (b.Status == "CHECKED_IN" && b.CheckOut <= today));
                    c["Activities"] = await db.ActivityBookings.CountAsync(b => b.ActDate == today && b.Status == "CONFIRMED");
                    c["Requests"] = await db.StayRequests.CountAsync(r => r.Status == "PENDING") + await db.LostItems.CountAsync(i => i.Status == "CLAIMED");
                    break;
                case RoleNames.Delivery:
                    c["Index"] = await db.DeliveryTasks.CountAsync(t => t.Status != "DONE")
                               + await db.ItemRequests.CountAsync(r => r.Status == "PENDING" || r.Status == "DELIVERING");
                    break;
                case RoleNames.Housekeeping:
                    c["Index"] = await db.HousekeepingTasks.CountAsync(t => t.Status != "DONE");
                    c["Issues"] = await db.RoomIssues.CountAsync(i => i.Status == "OPEN");
                    c["LostFound"] = await db.LostItems.CountAsync(i => i.Status == "STORED");
                    break;
                case RoleNames.Admin:
                    c["AdminBookings"] = await db.Bookings.CountAsync(b => b.Status == "PENDING");
                    c["AdminMessages"] = await db.ContactMessages.CountAsync(m => m.Status == "NEW");
                    break;
            }
        }
        catch (Exception) { c.Clear(); }   // ฐานข้อมูลมีปัญหา: แสดงเมนูต่อได้ แค่ไม่มีตัวเลข
        return c;
    }

    /// <summary>ห้องที่ถูกจองทับช่วงวันที่ (นับเฉพาะการจองที่ยังมีผล)</summary>
    public static IQueryable<int> BusyRoomIds(Csi402dbContext db, DateOnly checkIn, DateOnly checkOut) =>
        db.BookingRooms
            .Where(br => ActiveStatuses.Contains(br.Booking.Status)
                         && br.Booking.CheckIn < checkOut && br.Booking.CheckOut > checkIn)
            .Select(br => br.RoomId);

    /// <summary>ห้องที่เปิดให้จอง (ไม่ปิดซ่อมบำรุง)</summary>
    public static IQueryable<Room> Bookable(Csi402dbContext db) =>
        db.Rooms.Where(r => r.Status != "MAINTENANCE");

    /// <summary>ห้องที่เปิดให้จองและว่างตลอดช่วงวันที่</summary>
    public static IQueryable<Room> Available(Csi402dbContext db, DateOnly checkIn, DateOnly checkOut)
    {
        var busy = BusyRoomIds(db, checkIn, checkOut);
        return Bookable(db).Where(r => !busy.Contains(r.RoomId));
    }

    public static readonly Expression<Func<Room, RoomCardViewModel>> ToCard = r => new RoomCardViewModel
    {
        RoomId = r.RoomId,
        RoomNo = r.RoomNo,
        TypeId = r.TypeId,
        TypeName = r.Type.TypeName,
        Description = r.Type.Description,
        Amenities = r.Type.Amenities,
        ImageUrl = r.Type.ImageUrl,
        BasePrice = r.Type.BasePrice,
        WeekendPrice = r.Type.WeekendPrice,
        MaxGuests = r.Type.MaxGuests,
        ThemeName = r.Theme != null ? r.Theme.ThemeName : null,
        ThemeDescription = r.Theme != null ? r.Theme.Description : null
    };

    public static IQueryable<ServiceItemViewModel> ActiveServices(Csi402dbContext db) =>
        db.ExtraServices.AsNoTracking()
            .Where(s => s.IsActive == true)
            .OrderBy(s => s.ServiceName)
            .Select(s => new ServiceItemViewModel { ServiceId = s.ServiceId, Name = s.ServiceName, Price = s.Price, Category = s.Category, Description = s.Description, ImageUrl = s.ImageUrl });

    /// <summary>หาผู้ใช้ที่อีเมลหรือเบอร์ซ้ำ (excludeUserId = ข้ามผู้ใช้คนนี้ ใช้ตอนแก้ไขข้อมูลตัวเอง)</summary>
    public static Task<UserEntity?> FindDuplicateUserAsync(Csi402dbContext db, string email, string phone, int excludeUserId = 0) =>
        db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId != excludeUserId && (u.Email == email || u.Phone == phone));
}

// ======================================================================
// BookingBuilder
// ======================================================================
/// <summary>
/// ตรรกะการจองห้องที่ใช้ร่วมกันทั้ง "ลูกค้าจองเอง" และ "ฝ่ายบริการจองให้ลูกค้า" (เขียนที่เดียว)
/// ไม่เชื่อราคาจากฟอร์ม: อ่านราคาห้อง/บริการ/โปรจากฐานข้อมูลเสมอ และตรวจห้องว่างซ้ำในธุรกรรม Serializable ตอนยืนยัน
/// </summary>
public sealed class BookingBuilder
{
    private readonly Csi402dbContext _db;
    public BookingBuilder(Csi402dbContext db) => _db = db;

    private static DateOnly Today => DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today);

    /// <summary>เติมรายการห้องว่างตามวันที่ และรายการบริการเสริม</summary>
    public async Task FillListsAsync(BookingFormViewModel vm)
    {
        vm.Services = await BookingQueries.ActiveServices(_db).ToListAsync();

        vm.Error ??= BookingRules.ValidateDates(vm.CheckIn, vm.CheckOut, Today);
        if (vm.Error != null) { vm.AvailableRooms = new(); return; }

        vm.AvailableRooms = await BookingQueries.Available(_db, vm.CheckIn!.Value, vm.CheckOut!.Value)
            .AsNoTracking().OrderBy(r => r.RoomNo).Select(BookingQueries.ToCard).ToListAsync();
        RatePlan.ApplyToCards(vm.AvailableRooms, vm.CheckIn!.Value, vm.CheckOut!.Value,
            await RatePlan.SeasonsAsync(_db, vm.CheckIn!.Value, vm.CheckOut!.Value));
    }

    /// <summary>คำนวณราคาเพื่อแสดงก่อนยืนยัน (ไม่บันทึก)</summary>
    public async Task<bool> QuoteAsync(BookingFormViewModel vm, int userId) => (await BuildAsync(vm, userId, persist: false, staffId: null)).Ok;

    /// <summary>
    /// ยืนยันการจอง คืน Booking ที่บันทึกแล้ว หรือ null (พร้อมตั้ง vm.Error และเติมรายการใหม่ให้)
    /// staffId != null = พนักงานจองให้ที่เคาน์เตอร์: ยืนยันทันที + บันทึกการรับเงินสด
    /// </summary>
    public async Task<Booking?> ConfirmAsync(BookingFormViewModel vm, int userId, int? staffId = null)
    {
        var dateError = BookingRules.ValidateDates(vm.CheckIn, vm.CheckOut, Today);
        if (dateError != null) { vm.Error = dateError; await FillListsAsync(vm); return null; }

        // ล็อกช่วงตรวจสอบ+บันทึกในธุรกรรมเดียว กันจองห้องทับกันพร้อมกัน
        // ถ้าชนกับอีกคำขอ (deadlock / ฐานข้อมูลไม่ว่าง) ฐานข้อมูลจะยกเลิกฝั่งหนึ่ง → ลองใหม่ ซึ่งรอบใหม่จะเห็นว่าห้องถูกจองไปแล้ว
        for (var attempt = 1; ; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var (ok, booking) = await BuildAsync(vm, userId, persist: true, staffId);
                if (!ok)
                {
                    await tx.RollbackAsync();
                    vm.Error ??= "จองไม่สำเร็จ";
                    await FillListsAsync(vm);
                    return null;
                }
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return booking;
            }
            catch (Exception ex) when (IsDbConflict(ex))
            {
                try { await tx.RollbackAsync(); } catch { /* ฐานข้อมูลยกเลิกธุรกรรมให้แล้ว */ }
                _db.ChangeTracker.Clear();
                if (attempt < 4) { await Task.Delay(Random.Shared.Next(30, 120) * attempt); continue; }
                vm.Error = "ระบบมีผู้ใช้งานพร้อมกันจำนวนมาก บันทึกการจองไม่สำเร็จ กรุณาลองใหม่อีกครั้ง";
                await FillListsAsync(vm);
                return null;
            }
        }
    }

    /// <summary>ข้อผิดพลาดจากฐานข้อมูล (ไล่ดูทุกชั้น เพราะ MySQL ห่อ deadlock ไว้ใน InvalidOperationException)</summary>
    private static bool IsDbConflict(Exception? ex)
    {
        for (; ex != null; ex = ex.InnerException)
            if (ex is DbUpdateException or System.Data.Common.DbException) return true;
        return false;
    }

    private async Task<(bool Ok, Booking? Booking)> BuildAsync(BookingFormViewModel vm, int userId, bool persist, int? staffId)
    {
        var ci = vm.CheckIn!.Value;
        var co = vm.CheckOut!.Value;

        var roomIds = vm.RoomIds.Distinct().ToList();
        if (roomIds.Count == 0) { vm.Error = persist ? "กรุณาเลือกห้องอย่างน้อย 1 ห้อง" : null; return (false, null); }
        if (roomIds.Count > BookingRules.MaxRooms) { vm.Error = $"จองได้ครั้งละไม่เกิน {BookingRules.MaxRooms} ห้อง"; return (false, null); }

        var rooms = await BookingQueries.Available(_db, ci, co).Include(r => r.Type)
            .Where(r => roomIds.Contains(r.RoomId))
            .ToListAsync();
        if (rooms.Count != roomIds.Count)
        {
            vm.Error = "มีห้องที่เลือกไม่ว่างในช่วงวันที่นี้แล้ว กรุณาเลือกใหม่";
            vm.RoomIds = rooms.Select(r => r.RoomId).ToList();
            return (false, null);
        }

        // จำนวนผู้เข้าพักต้องไม่เกินความจุรวมของห้องที่เลือก
        var guests = vm.Guests ?? 1;
        var capacity = rooms.Sum(r => (int)r.Type.MaxGuests);
        if (guests < 1 || guests > BookingRules.MaxGuests) { vm.Error = $"จำนวนผู้เข้าพักต้องอยู่ระหว่าง 1–{BookingRules.MaxGuests} คน"; return (false, null); }
        if (guests > capacity) { vm.Error = $"ห้องที่เลือกพักได้รวม {capacity} คน แต่มีผู้เข้าพัก {guests} คน กรุณาเลือกห้องเพิ่ม"; return (false, null); }
        vm.Guests = guests;

        var qtyById = vm.ServiceQty.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => Math.Min(kv.Value, BookingRules.MaxServiceQty));
        var serviceIds = qtyById.Keys.ToList();
        var services = await _db.ExtraServices.AsNoTracking()
            .Where(s => serviceIds.Contains(s.ServiceId) && s.IsActive == true).ToListAsync();
        var servicesTotal = services.Sum(s => s.Price * qtyById[s.ServiceId]) * rooms.Count;

        // userId = 0 → ลูกค้าใหม่ที่พนักงานกำลังจะสร้างบัญชีให้ (คิดราคาแบบสมาชิกใหม่ ยังไม่มีวันเกิด/วันครบรอบ)
        var user = userId == 0 ? new UserEntity { IsMember = true }
            : await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null) { vm.Error = "ไม่พบบัญชีผู้ใช้"; return (false, null); }

        var promos = await PromoCatalog.ActiveRulesAsync(_db, userId);
        var code = PricingService.NormalizeCode(vm.PromoCode);
        if (code != null && !PromoCodeGuard.Allow(userId == 0 ? "staff:" + vm.CustomerEmail : "u:" + userId))
        {
            vm.Error = "กรอกรหัสโปรโมชั่นผิดหลายครั้งเกินไป กรุณารอ 10 นาทีแล้วลองใหม่";
            return (false, null);
        }

        // ราคาตามวัน: เฉลี่ยต่อคืนของแต่ละห้อง (คืนศุกร์/เสาร์ + ช่วงราคาพิเศษ)
        var seasons = await RatePlan.SeasonsAsync(_db, ci, co);
        var nightly = rooms.ToDictionary(r => r.RoomId, r => RatePlan.AveragePerNight(r.Type.BasePrice, r.Type.WeekendPrice, ci, co, seasons));

        var result = PricingService.Calculate(new PricingInput(
            rooms.Select(r => nightly[r.RoomId]).ToList(), ci, co, servicesTotal, Today,
            user.IsMember, user.BirthDate, user.AnniversaryDate, promos, code));
        if (code != null && !result.CodeApplied && result.CodeMessage?.StartsWith("ไม่พบ") == true)
            PromoCodeGuard.Fail(userId == 0 ? "staff:" + vm.CustomerEmail : "u:" + userId);
        vm.PromoCode = code;

        vm.Summary = new PriceSummaryViewModel
        {
            Nights = result.Nights, RoomCount = rooms.Count,
            RoomsTotal = result.RoomsTotal, ServicesTotal = result.ServicesTotal,
            Subtotal = result.Subtotal, Discount = result.Discount, Net = result.Net,
            PromoName = result.Applied?.Name, PromoPercent = result.Applied?.Percent ?? 0,
            PromoCapped = result.Applied is { } ap && result.Discount < Math.Round(result.RoomsTotal * ap.Percent / 100m, 2, MidpointRounding.AwayFromZero),
            PromoNeedsId = result.Applied?.NeedStaffConfirm == true,
            CodeMessage = result.CodeMessage, CodeApplied = result.CodeApplied
        };

        if (!persist) return (true, null);

        // ราคาที่ลูกค้าเห็นก่อนกดยืนยันต้องตรงกับราคาที่จะบันทึก (เช่น สิทธิ์โปรเต็มระหว่างนั้น) — ไม่ตรง = ให้ตรวจสอบใหม่
        if (vm.QuotedNet is { } quoted && quoted != result.Net)
        {
            vm.Error = $"ยอดสุทธิเปลี่ยนจาก {quoted:N2} เป็น {result.Net:N2} บาท (โปรโมชั่นหรือราคาเปลี่ยนระหว่างนั้น) กรุณาตรวจสอบแล้วกดยืนยันอีกครั้ง";
            return (false, null);
        }

        var booking = new Booking
        {
            UserId = userId,
            PromoId = result.Applied?.PromoId,
            CheckIn = ci,
            CheckOut = co,
            BookedAt = pameka_7033_l005.Helpers.AppClock.Now,
            Status = staffId.HasValue ? "CONFIRMED" : "PENDING",
            ConfirmedBy = staffId,
            Subtotal = result.Subtotal,
            DiscountTotal = result.Discount,
            NetTotal = result.Net,
            Guests = (sbyte)guests
        };
        foreach (var r in rooms)
        {
            booking.BookingRooms.Add(new BookingRoom { RoomId = r.RoomId, PricePerNight = nightly[r.RoomId], Nights = (short)result.Nights });
            foreach (var s in services)
                booking.BookingServices.Add(new BookingService { RoomId = r.RoomId, ServiceId = s.ServiceId, Quantity = qtyById[s.ServiceId], UnitPrice = s.Price });
        }
        if (staffId.HasValue)
            booking.Payments.Add(new Payment
            {
                Amount = result.Net, PaymentType = BookingWorkflow.PaymentType, Method = "CASH",
                Status = "PAID", PaidAt = pameka_7033_l005.Helpers.AppClock.Now, HandledBy = staffId
            });
        _db.Bookings.Add(booking);
        return (true, booking);
    }
}

// ======================================================================
// BookingWorkflow
// ======================================================================
/// <summary>สถานะการเงินของการจองหนึ่งรายการ (คำนวณจากแถวในตาราง payments)</summary>
public readonly record struct PaymentFlags(bool Paid, bool PendingPayment, bool PendingRefund, bool Refunded)
{
    public static PaymentFlags From(IEnumerable<(string Type, string Status)> payments)
    {
        bool paid = false, pendingPayment = false, pendingRefund = false, refunded = false;
        foreach (var (type, status) in payments)
        {
            if (type == BookingWorkflow.PaymentType)
            {
                if (status == "PAID") paid = true;
                else if (status == "PENDING") pendingPayment = true;
            }
            else if (type == BookingWorkflow.RefundType)
            {
                if (status == "PENDING") pendingRefund = true;
                else if (status == "REFUNDED") refunded = true;
            }
        }
        return new PaymentFlags(paid, pendingPayment, pendingRefund, refunded);
    }
}

/// <summary>
/// กฎการเปลี่ยนสถานะของการจอง/ห้อง/งาน รวมไว้ที่เดียว (ไม่แตะฐานข้อมูล จึงทดสอบได้ตรง ๆ)
/// ทุก controller ต้องเรียกกฎเหล่านี้ก่อนเปลี่ยนสถานะเสมอ
/// </summary>
public static class BookingWorkflow
{
    public const string PaymentType = "PAYMENT";
    public const string RefundType = "REFUND";
    public const string DeliveryDoing = "DELIVERING";
    public const string CleaningDoing = "CLEANING";

    // ---- การชำระเงิน / ยกเลิก / คืนเงิน
    public static bool CanPay(string bookingStatus, PaymentFlags f) =>
        bookingStatus == "PENDING" && !f.Paid && !f.PendingPayment;

    /// <summary>ลูกค้ายกเลิกเองได้เมื่อยังไม่มีการแจ้งชำระเงิน (ถ้าแจ้งแล้วเงินอาจโอนมาแล้ว ต้องผ่านฝ่ายบัญชี)</summary>
    public static bool CanCancelDirect(string bookingStatus, PaymentFlags f) =>
        bookingStatus == "PENDING" && !f.Paid && !f.PendingPayment;

    /// <summary>สร้างคำขอคืนเงินเต็มจำนวนได้ (ผู้จัดการ): รายการที่ชำระแล้วและยังไม่เข้าพัก รวมถึงกรณีไม่มาเข้าพักที่โรงแรมยินยอมคืนให้</summary>
    public static bool CanRequestRefund(string bookingStatus, PaymentFlags f) =>
        bookingStatus is "CONFIRMED" or "NO_SHOW" && f.Paid && !f.PendingRefund;

    /// <summary>ลูกค้าขอคืนเงินเองได้จนก่อนวันสิ้นสุดการเข้าพัก — การจองที่เลยวันออกไปแล้ว (ไม่มาเข้าพัก) ต้องติดต่อโรงแรม</summary>
    public static bool CanCustomerRequestRefund(string bookingStatus, PaymentFlags f, DateOnly checkOut, DateOnly today) =>
        bookingStatus == "CONFIRMED" && CanRequestRefund(bookingStatus, f) && today < checkOut;

    public static bool CanConfirmPayment(string bookingStatus, string paymentStatus) =>
        bookingStatus == "PENDING" && paymentStatus == "PENDING";

    public static bool CanApproveRefund(string bookingStatus, string refundStatus) =>
        bookingStatus is "CONFIRMED" or "NO_SHOW" && refundStatus == "PENDING";

    // ---- ไม่มาเข้าพัก (No-show)
    /// <summary>บันทึก "ไม่มาเข้าพัก" ได้ตั้งแต่วันถัดจากวันเข้าพัก (วันเข้าพักลูกค้ายังมาถึงดึกได้) และต้องไม่มีคำขอคืนเงินค้าง — ห้องจะว่างให้จองต่อทันที</summary>
    public static bool CanMarkNoShow(string bookingStatus, DateOnly checkIn, DateOnly today, PaymentFlags f) =>
        bookingStatus == "CONFIRMED" && today > checkIn && !f.PendingRefund;

    /// <summary>บันทึกผิด/ลูกค้ามาทีหลัง: กลับเป็น "จองสำเร็จ" ได้ถ้ายังไม่เลยวันออก (ต้องตรวจว่าห้องยังว่างอีกชั้น)</summary>
    public static bool CanUndoNoShow(string bookingStatus, DateOnly checkOut, DateOnly today) =>
        bookingStatus == "NO_SHOW" && today < checkOut;

    // ---- เช็คอิน / เช็คเอาท์ / ส่งของ
    /// <summary>เช็คอินได้ตั้งแต่วันเข้าพักจนก่อนวันออก และต้องไม่มีคำขอคืนเงินค้างอยู่</summary>
    public static bool CanCheckIn(string bookingStatus, DateOnly checkIn, DateOnly checkOut, DateOnly today, PaymentFlags f) =>
        bookingStatus == "CONFIRMED" && today >= checkIn && today < checkOut && !f.PendingRefund;

    /// <summary>รีวิวได้เมื่อเช็คเอาท์แล้ว และยังไม่เคยรีวิวการจองนี้ (1 การจอง = 1 รีวิว)</summary>
    public static bool CanReview(string bookingStatus, bool alreadyReviewed) => bookingStatus == "CHECKED_OUT" && !alreadyReviewed;

    public static bool CanCheckOut(string bookingStatus) => bookingStatus == "CHECKED_IN";

    public static bool CanSendDelivery(string bookingStatus) => bookingStatus == "CHECKED_IN";

    // ---- สถานะห้อง (มือ)
    /// <summary>ตั้งสถานะห้องเองได้เฉพาะ AVAILABLE / MAINTENANCE; ห้องที่มีผู้พักต้องเช็คเอาท์ก่อน; ตั้งว่างไม่ได้ถ้ายังมีงานทำความสะอาดค้าง</summary>
    public static bool CanSetRoomStatus(string from, string to, bool hasOpenCleaning) =>
        (to == "AVAILABLE" || to == "MAINTENANCE") && from != "OCCUPIED" && from != to
        && !(to == "AVAILABLE" && hasOpenCleaning);

    public static bool CanSendCleaning(string roomStatus, bool hasOpenCleaning) =>
        roomStatus != "OCCUPIED" && !hasOpenCleaning;

    // ---- งานของฝ่ายส่งของ / ทำความสะอาด
    public static bool CanTakeTask(string taskStatus) => taskStatus == "PENDING";

    public static bool CanFinishTask(string taskStatus, string doingStatus, int? assignedTo, int? userId) =>
        taskStatus == doingStatus && userId.HasValue && assignedTo == userId;
}

// ======================================================================
// ThaiLabels.Status
// ======================================================================
public static partial class ThaiLabels
{
    public static string PaymentStatus(string? type, string? status) => (type, status) switch
    {
        ("PAYMENT", "PENDING") => "รอตรวจสอบการชำระเงิน",
        ("PAYMENT", "PAID") => "ชำระแล้ว",
        ("PAYMENT", "REFUNDED") => "คืนเงินแล้ว",
        ("PAYMENT", "REJECTED") => "ถูกปฏิเสธ",
        ("REFUND", "PENDING") => "รอคืนเงิน",
        ("REFUND", "REFUNDED") => "คืนเงินแล้ว",
        _ => status ?? ""
    };

    public static string Stars(int rating) => new string('★', Math.Clamp(rating, 0, 5)) + new string('☆', 5 - Math.Clamp(rating, 0, 5));

    public static string PaymentMethod(string? m) => m switch { "QR" => "QR Code", "CASH" => "เงินสด", "CARD" => "บัตร", _ => m ?? "" };

    public static string RoomStatus(string? s) => s switch
    {
        "AVAILABLE" => "ว่าง",
        "BOOKED" => "จองแล้ว",
        "OCCUPIED" => "มีผู้เข้าพัก",
        "CLEANING" => "กำลังทำความสะอาด",
        "MAINTENANCE" => "ปิดซ่อมบำรุง",
        _ => s ?? ""
    };

    public static string TaskStatus(string? s) => s switch
    {
        "PENDING" => "รอรับงาน",
        "DELIVERING" => "กำลังนำส่ง",
        "CANCELLED" => "ยกเลิกแล้ว",
        "CLEANING" => "กำลังทำความสะอาด",
        "DONE" => "เสร็จแล้ว",
        _ => s ?? ""
    };
}

// ======================================================================
// BookingExpiry — ยกเลิกการจองค้างชำระอัตโนมัติ
// ======================================================================
/// <summary>
/// การจองที่ลูกค้ายังไม่แจ้งชำระเงินจะกันห้องไว้ได้ไม่เกินกำหนด เพื่อไม่ให้ห้องถูกกันไว้ฟรี
/// กำหนดชำระ = เวลาจอง + Booking:PaymentHoldHours (ค่าเริ่มต้น 24 ชม.) แต่ไม่เกินสิ้นวันเช็คอิน
/// รายการที่แจ้งชำระแล้ว (รอฝ่ายบัญชี) จะไม่ถูกยกเลิก
/// </summary>
public static class BookingExpiry
{
    public static int HoldHours { get; set; } = 24;

    /// <summary>ถ้าสลิปถูกปฏิเสธ ให้เวลาแนบใหม่อีก HoldHours นับจากเวลาที่ปฏิเสธ (ไม่เกินสิ้นวันเช็คอินเหมือนเดิม)</summary>
    public static DateTime Deadline(DateTime bookedAt, DateOnly checkIn, DateTime? lastRejectedAt = null)
    {
        var from = lastRejectedAt > bookedAt ? lastRejectedAt.Value : bookedAt;
        var byHold = from.AddHours(HoldHours);
        var byCheckIn = checkIn.ToDateTime(new TimeOnly(23, 59, 59));
        return byHold < byCheckIn ? byHold : byCheckIn;
    }

    /// <summary>
    /// ยกเลิกรายการที่เกินกำหนดชำระ และบันทึก "ไม่มาเข้าพัก" ให้การจองที่ชำระแล้วแต่เลยวันออกโดยไม่ได้เช็คอิน
    /// คืนจำนวนรายการที่เปลี่ยน (ส่ง userId = เฉพาะของลูกค้าคนนั้น)
    /// </summary>
    public static async Task<int> RunAsync(Csi402dbContext db, DateTime now, int? userId = null)
    {
        var noShows = await MarkNoShowsAsync(db, DateOnly.FromDateTime(now), userId);
        var bookedBefore = now.AddHours(-HoldHours);
        var today = DateOnly.FromDateTime(now);
        var q = db.Bookings.Where(b => b.Status == "PENDING"
            && !b.Payments.Any(p => p.PaymentType == BookingWorkflow.PaymentType && (p.Status == "PENDING" || p.Status == "PAID"))
            && ((b.BookedAt < bookedBefore && !b.Payments.Any(p => p.Status == "REJECTED" && p.PaidAt >= bookedBefore)) || b.CheckIn < today));
        if (userId.HasValue) q = q.Where(b => b.UserId == userId.Value);

        var expired = await q.ToListAsync();
        foreach (var b in expired)
        {
            b.Status = "CANCELLED";
            Notify.Add(db, b.UserId, $"การจอง #{b.BookingId} ถูกยกเลิกอัตโนมัติ",
                "ไม่ได้รับหลักฐานการชำระเงินภายในเวลาที่กำหนด ห้องถูกคืนให้ผู้อื่นแล้ว จองใหม่ได้ตลอดเวลา", Notify.BookingLink(b.BookingId));
        }

        // การจองที่ลูกค้าแจ้งชำระแล้วแต่ฝ่ายบัญชียังไม่ตรวจ จนเลยวันออก: ห้องไม่เหลืออะไรให้กัน → ปิดรายการ
        // สลิปที่รอตรวจเปลี่ยนเป็น "ถูกปฏิเสธ" เก็บไว้เป็นหลักฐาน (ลูกค้าอาจโอนจริง ต้องติดต่อโรงแรม)
        var stale = db.Bookings.Include(b => b.Payments).Where(b => b.Status == "PENDING" && b.CheckOut <= today
            && b.Payments.Any(p => p.PaymentType == BookingWorkflow.PaymentType && p.Status == "PENDING"));
        if (userId.HasValue) stale = stale.Where(b => b.UserId == userId.Value);
        var staleList = await stale.ToListAsync();
        foreach (var b in staleList)
        {
            foreach (var p in b.Payments.Where(p => p.PaymentType == BookingWorkflow.PaymentType && p.Status == "PENDING"))
            { p.Status = "REJECTED"; p.PaidAt = now; }
            b.Status = "CANCELLED";
            Notify.Add(db, b.UserId, $"การจอง #{b.BookingId} ถูกปิดรายการ",
                "การจองเลยวันเข้าพักโดยที่ยังไม่ได้รับการยืนยันการชำระเงิน หากคุณโอนเงินแล้ว กรุณาติดต่อโรงแรมพร้อมหลักฐาน", Notify.BookingLink(b.BookingId));
        }
        if (expired.Count > 0 || staleList.Count > 0) await db.SaveChangesAsync();
        await StayBill.SweepAsync(db, now);
        return expired.Count + staleList.Count + noShows;
    }

    /// <summary>การจองที่ยืนยันแล้วแต่เลยวันออกไปโดยไม่ได้เช็คอิน (และไม่มีคำขอคืนเงินค้าง) → NO_SHOW</summary>
    public static async Task<int> MarkNoShowsAsync(Csi402dbContext db, DateOnly today, int? userId = null)
    {
        var q = db.Bookings.Where(b => b.Status == "CONFIRMED" && b.CheckOut <= today
            && !b.Payments.Any(p => p.PaymentType == BookingWorkflow.RefundType && p.Status == "PENDING"));
        if (userId.HasValue) q = q.Where(b => b.UserId == userId.Value);
        var list = await q.ToListAsync();
        foreach (var b in list)
        {
            b.Status = "NO_SHOW";
            Notify.Add(db, b.UserId, $"การจอง #{b.BookingId}: ไม่ได้เข้าพักตามกำหนด",
                "ระบบบันทึกว่าไม่มาเข้าพัก หากมีเหตุจำเป็นหรือข้อสงสัย กรุณาติดต่อโรงแรม", Notify.BookingLink(b.BookingId));
        }
        if (list.Count > 0) await db.SaveChangesAsync();
        return list.Count;
    }
}

/// <summary>งานเบื้องหลัง: ตรวจทุก Booking:ExpiryCheckMinutes นาที (ค่าเริ่มต้น 10, ตั้ง 0 = ปิด)</summary>
public sealed class BookingExpiryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BookingExpiryService> _log;
    private readonly TimeSpan _every;
    private readonly bool _autoFestivals, _autoCampaigns;
    private DateOnly _festivalsCheckedOn, _campaignsCheckedOn;

    public BookingExpiryService(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<BookingExpiryService> log)
    {
        _scopes = scopes; _log = log;
        _autoFestivals = cfg.GetValue("Pricing:AutoFestivals", true);
        _autoCampaigns = cfg.GetValue("Promotions:AutoCampaigns", true);
        BookingExpiry.HoldHours = Math.Clamp(cfg.GetValue("Booking:PaymentHoldHours", 24), 1, 24 * 14);
        _every = TimeSpan.FromMinutes(Math.Max(0, cfg.GetValue("Booking:ExpiryCheckMinutes", 10)));
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (_every == TimeSpan.Zero) return;
        using var timer = new PeriodicTimer(_every);
        try
        {
            do
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<Csi402dbContext>();
                    var n = await BookingExpiry.RunAsync(db, pameka_7033_l005.Helpers.AppClock.Now);
                    if (n > 0) _log.LogInformation("ปรับสถานะการจอง (เกินกำหนดชำระ / ไม่มาเข้าพัก) {Count} รายการ", n);
                    // Part 47: ราคาตามเทศกาลอัตโนมัติ — ตรวจวันละครั้ง
                    var today = DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today);
                    if (_autoFestivals && _festivalsCheckedOn != today)
                    {
                        var added = await FestivalPricing.EnsureAsync(db, today);
                        _festivalsCheckedOn = today;
                        if (added > 0) _log.LogInformation("สร้างช่วงราคาเทศกาลอัตโนมัติ {Count} ช่วง", added);
                    }
                    // Part 49: แคมเปญโปรโมชั่นเทศกาลไทย (คูปองกดเก็บ / ลดอัตโนมัติ) — ตรวจวันละครั้ง
                    if (_autoCampaigns && _campaignsCheckedOn != today)
                    {
                        var addedP = await PromoCampaigns.EnsureAsync(db, today);
                        _campaignsCheckedOn = today;
                        if (addedP > 0) _log.LogInformation("สร้างแคมเปญโปรโมชั่นเทศกาล {Count} รายการ", addedP);
                    }
                    // Part 51: รายชื่อรอห้องว่าง — แจ้งเตือนเมื่อมีห้องว่าง (ทุกรอบ)
                    var w = await Waitlist.CheckAsync(db, today);
                    if (w > 0) _log.LogInformation("แจ้งเตือนรายชื่อรอห้องว่าง {Count} รายการ", w);
                }
                catch (Exception ex) when (!stop.IsCancellationRequested)
                {
                    _log.LogWarning(ex, "ตรวจการจองค้างชำระไม่สำเร็จ จะลองใหม่รอบถัดไป");
                }
            } while (await timer.WaitForNextTickAsync(stop));
        }
        catch (OperationCanceledException) { }
    }
}

/// <summary>
/// Part 43: ราคาห้องตามวัน
/// · ราคาวันหยุดสุดสัปดาห์ (คืนวันศุกร์ / เสาร์) ตั้งแยกได้ต่อประเภทห้อง (room_types.weekend_price ว่าง = ใช้ราคาปกติ)
/// · ช่วงราคาพิเศษ (rate_seasons) ปรับ +/− % ทุกประเภทห้อง เช่น ปีใหม่ +30% · ช่วงโลว์ซีซั่น −15% (ซ้อนกัน → ใช้ % สูงสุด)
/// · ราคาต่อคืนปัดเป็นบาทเต็ม · ราคาที่บันทึกในการจอง = ค่าเฉลี่ยต่อคืน (ทศนิยม 2 ตำแหน่ง) × จำนวนคืน เพื่อให้ยอดในใบเสร็จตรงกันทุกที่
/// </summary>
public static class RatePlan
{
    public const string SeasonsTableSql = @"CREATE TABLE IF NOT EXISTS `rate_seasons` (
    `season_id` int NOT NULL AUTO_INCREMENT,
    `season_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `start_date` date NOT NULL,
    `end_date` date NOT NULL,
    `adjust_percent` decimal(5,2) NOT NULL,
    `is_active` tinyint(1) NOT NULL DEFAULT '1',
    `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `auto_key` varchar(40) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    PRIMARY KEY (`season_id`),
    UNIQUE KEY `ux_season_auto` (`auto_key`),
    KEY `ix_season_dates` (`is_active`, `start_date`, `end_date`),
    CONSTRAINT `chk_season_dates` CHECK (`end_date` >= `start_date`),
    CONSTRAINT `chk_season_percent` CHECK (`adjust_percent` BETWEEN -90 AND 300)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;";

    public const decimal MinPercent = -90, MaxPercent = 300;

    public record Season(int Id, string Name, DateOnly Start, DateOnly End, decimal Percent);

    /// <summary>ช่วงราคาพิเศษที่เปิดอยู่และทับกับคืนในช่วง [from, to)</summary>
    public static async Task<List<Season>> SeasonsAsync(Csi402dbContext db, DateOnly from, DateOnly to) =>
        await db.RateSeasons.AsNoTracking()
            .Where(s => s.IsActive && s.StartDate < to && s.EndDate >= from)
            .Select(s => new Season(s.SeasonId, s.SeasonName, s.StartDate, s.EndDate, s.AdjustPercent))
            .ToListAsync();

    public static bool IsWeekendNight(DateOnly night) => night.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;

    /// <summary>ช่วงราคาพิเศษที่ใช้กับคืนนี้ (ซ้อนกัน → % สูงสุด)</summary>
    public static Season? SeasonOf(DateOnly night, IReadOnlyList<Season> seasons) =>
        seasons.Where(s => s.Start <= night && night <= s.End).OrderByDescending(s => s.Percent).ThenBy(s => s.Id).FirstOrDefault();

    public static decimal NightPrice(decimal basePrice, decimal? weekendPrice, DateOnly night, IReadOnlyList<Season> seasons)
    {
        var p = weekendPrice is > 0 && IsWeekendNight(night) ? weekendPrice.Value : basePrice;
        if (SeasonOf(night, seasons) is { } s)
            p = Math.Round(p * (100m + s.Percent) / 100m, 0, MidpointRounding.AwayFromZero);
        return Math.Max(0, p);
    }

    public static List<(DateOnly Night, decimal Price, string? Season)> Breakdown(decimal basePrice, decimal? weekendPrice,
        DateOnly checkIn, DateOnly checkOut, IReadOnlyList<Season> seasons)
    {
        var list = new List<(DateOnly, decimal, string?)>();
        for (var d = checkIn; d < checkOut; d = d.AddDays(1))
            list.Add((d, NightPrice(basePrice, weekendPrice, d, seasons), SeasonOf(d, seasons)?.Name));
        return list;
    }

    /// <summary>ราคาเฉลี่ยต่อคืนของการเข้าพักนี้ (ทศนิยม 2 ตำแหน่ง) — ใช้คิดเงินและบันทึกลง booking_rooms.price_per_night</summary>
    public static decimal AveragePerNight(decimal basePrice, decimal? weekendPrice, DateOnly checkIn, DateOnly checkOut, IReadOnlyList<Season> seasons)
    {
        var nights = checkOut.DayNumber - checkIn.DayNumber;
        if (nights <= 0) return basePrice;
        var sum = Breakdown(basePrice, weekendPrice, checkIn, checkOut, seasons).Sum(n => n.Price);
        return Math.Round(sum / nights, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>ใส่ราคาตามวันที่เลือกให้การ์ดห้อง (BasePrice = เฉลี่ยต่อคืนของช่วงนั้น · RegularPrice = ราคาปกติ)</summary>
    public static void ApplyToCards(IEnumerable<RoomCardViewModel> cards, DateOnly checkIn, DateOnly checkOut, IReadOnlyList<Season> seasons)
    {
        foreach (var c in cards)
        {
            c.RegularPrice = c.BasePrice;
            var nights = Breakdown(c.BasePrice, c.WeekendPrice, checkIn, checkOut, seasons);
            if (nights.Count == 0) continue;
            c.BasePrice = AveragePerNight(c.RegularPrice, c.WeekendPrice, checkIn, checkOut, seasons);
            c.PriceVaries = nights.Select(n => n.Price).Distinct().Count() > 1 || nights[0].Price != c.RegularPrice;
            c.SeasonName = nights.Select(n => n.Season).FirstOrDefault(s => s != null);
        }
    }
}

/// <summary>
/// Part 47: ปรับราคาห้องตามเทศกาลอัตโนมัติ
/// · ระบบสร้าง "ช่วงราคาพิเศษ" ของเทศกาลไทยให้ล่วงหน้า (ปีนี้ + ปีหน้า) ทุกวัน — เพิ่มอย่างเดียว ไม่แก้/ลบของเดิม
/// · แต่ละเทศกาลมีรหัส auto_key (เช่น songkran-2027) จึงไม่สร้างซ้ำ ผู้จัดการแก้ % / วันที่ / ปิด ได้ในหน้า "ช่วงราคาพิเศษ"
///   (ลบช่วงอัตโนมัติ = ปิดใช้แทน เพื่อไม่ให้ระบบสร้างกลับมาอีก)
/// · ปิดทั้งระบบได้ด้วย Pricing:AutoFestivals = false
/// · เทศกาลตามจันทรคติ (ตรุษจีน / ลอยกระทง) ใช้ตารางวันที่ที่ตรวจสอบแล้ว ปีที่ยังไม่มีในตารางจะข้ามไป (ตั้งเองได้)
/// </summary>
public static class FestivalPricing
{
    public record Festival(string Key, string Name, DateOnly Start, DateOnly End, decimal Percent);

    // วันตรุษจีน (ค.ศ.) และวันลอยกระทง (ขึ้น 15 ค่ำ เดือน 12)
    private static readonly Dictionary<int, DateOnly> ChineseNewYear = new()
    {
        [2026] = new(2026, 2, 17), [2027] = new(2027, 2, 6), [2028] = new(2028, 1, 26),
        [2029] = new(2029, 2, 13), [2030] = new(2030, 2, 3),
    };
    internal static readonly Dictionary<int, DateOnly> LoyKrathong = new()
    {
        [2025] = new(2025, 11, 5), [2026] = new(2026, 11, 24),
    };

    private static string Be(int year) => (year + 543).ToString();

    /// <summary>เทศกาลของปี ค.ศ. นี้ (คืนแรก–คืนสุดท้ายที่ปรับราคา)</summary>
    public static List<Festival> For(int y)
    {
        var list = new List<Festival>
        {
            new($"valentine-{y}", $"วาเลนไทน์ {Be(y)}", new(y, 2, 13), new(y, 2, 14), 10),
            new($"songkran-{y}", $"สงกรานต์ {Be(y)}", new(y, 4, 11), new(y, 4, 15), 30),
            new($"christmas-{y}", $"คริสต์มาส {Be(y)}", new(y, 12, 23), new(y, 12, 25), 15),
            new($"newyear-{y}", $"ปีใหม่ {Be(y + 1)}", new(y, 12, 29), new(y + 1, 1, 1), 30),
        };
        if (ChineseNewYear.TryGetValue(y, out var cny))
            list.Add(new($"cny-{y}", $"ตรุษจีน {Be(y)}", cny.AddDays(-1), cny.AddDays(1), 15));
        if (LoyKrathong.TryGetValue(y, out var lk))
            list.Add(new($"loykrathong-{y}", $"ลอยกระทง {Be(y)}", lk.AddDays(-1), lk, 15));
        return list.OrderBy(f => f.Start).ToList();
    }

    public static bool IsAuto(RateSeason s) => !string.IsNullOrEmpty(s.AutoKey);

    /// <summary>สร้างช่วงราคาเทศกาลที่ยังไม่มี (ปีนี้และปีหน้า ข้ามช่วงที่ผ่านไปแล้ว) — คืนจำนวนที่เพิ่ม</summary>
    public static async Task<int> EnsureAsync(Csi402dbContext db, DateOnly today)
    {
        var wanted = For(today.Year).Concat(For(today.Year + 1)).Where(f => f.End >= today).ToList();
        var keys = wanted.Select(f => f.Key).ToList();
        var have = await db.RateSeasons.Where(s => s.AutoKey != null && keys.Contains(s.AutoKey)).Select(s => s.AutoKey!).ToListAsync();
        var add = wanted.Where(f => !have.Contains(f.Key)).ToList();
        if (add.Count == 0) return 0;
        var now = AppClock.Now;
        foreach (var f in add)
            db.RateSeasons.Add(new RateSeason { SeasonName = f.Name, StartDate = f.Start, EndDate = f.End, AdjustPercent = f.Percent, IsActive = true, CreatedAt = now, AutoKey = f.Key });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return 0; }   // อีกโปรเซสสร้างไปพร้อมกัน (unique key) — รอบหน้าค่อยตรวจใหม่
        return add.Count;
    }
}

/// <summary>
/// Part 49: แคมเปญโปรโมชั่นเทศกาลไทย + คูปองกดเก็บ
/// · ระบบสร้างแคมเปญเทศกาลให้ล่วงหน้าทุกวัน (เพิ่มอย่างเดียว ไม่แก้/ลบของเดิม · ผู้จัดการแก้/ปิดได้ในหน้าโปรโมชั่น)
/// · โปร "ใช้ได้ทันที" (เช่น หน้าฝน) ระบบเลือกให้อัตโนมัติตอนจอง · คูปอง "กดเก็บ" ต้องกดเก็บในช่วงเวลาเก็บ แล้วใช้ให้อัตโนมัติ
/// · ปิดการสร้างอัตโนมัติ: Promotions:AutoCampaigns = false
/// </summary>
public static class PromoCampaigns
{
    public record Campaign(string Key, string Name, string Type, decimal Percent, decimal? Cap, string? Code, bool Claimable,
        DateOnly? ClaimFrom, DateOnly? ClaimUntil, DateOnly Start, DateOnly End, short? MinNights, int? MaxUses, string Description);

    private static string Be(int y) => (y + 543).ToString();

    /// <summary>แคมเปญของปี ค.ศ. y (ช่วงวันที่ = วันเข้าพักที่ใช้ได้)</summary>
    public static List<Campaign> For(int y)
    {
        var yy = (y % 100).ToString("00");
        var febEnd = new DateOnly(y, 2, DateTime.DaysInMonth(y, 2));
        return new List<Campaign>
        {
            new($"p-newyear-{y}", $"เที่ยวต้นปี {Be(y)}", "CODE", 15, 1500, $"NEWYEAR{yy}", true,
                new(y - 1, 12, 1), new(y, 1, 20), new(y, 1, 2), febEnd, 2, null,
                "เก็บคูปองฉลองปีใหม่ไว้ก่อน แล้วพักช่วงต้นปีรับส่วนลด 15% (พัก 2 คืนขึ้นไป)"),
            new($"p-valentine-{y}", $"วาเลนไทน์ คู่รัก {Be(y)}", "CODE", 14, 1400, $"LOVE{yy}", true,
                new(y, 2, 1), new(y, 2, 14), new(y, 2, 1), new(y, 3, 15), null, 214,
                "คูปองคู่รักจำนวนจำกัด 214 ใบ เก็บได้ 1–14 ก.พ. ใช้เข้าพักถึงกลางเดือนมีนาคม"),
            new($"p-songkran-{y}", $"สงกรานต์ จองก่อนได้ก่อน {Be(y)}", "CODE", 12, 2000, $"SONGKRAN{yy}", true,
                new(y, 3, 1), new(y, 4, 9), new(y, 4, 10), new(y, 4, 20), null, 300,
                "เก็บคูปองตั้งแต่ 1 มี.ค. แล้วจองพักช่วงสงกรานต์ ลด 12% (ลดสูงสุด 2,000 บาท)"),
            new($"p-green-{y}", $"เที่ยวไทยหน้าฝน {Be(y)}", "SEASONAL", 15, null, null, false,
                null, null, new(y, 6, 1), new(y, 9, 30), null, null,
                "Green Season ลด 15% ทุกการเข้าพักเดือน มิ.ย.–ก.ย. ไม่ต้องเก็บคูปอง ระบบลดให้อัตโนมัติ"),
            new($"p-1111-{y}", $"11.11 ดับเบิลเดย์ {Be(y)}", "CODE", 11, 1111, $"DOUBLE11Y{yy}", true,
                new(y, 11, 11), new(y, 11, 11), new(y, 11, 12), new(y + 1, 1, 31), null, 111,
                "เก็บได้เฉพาะวันที่ 11 พ.ย. เท่านั้น 111 ใบ ลด 11% เข้าพักถึง 31 ม.ค."),
            new($"p-1212-{y}", $"12.12 ส่งท้ายปี {Be(y)}", "CODE", 12, 1212, $"DOUBLE12Y{yy}", true,
                new(y, 12, 12), new(y, 12, 12), new(y, 12, 13), new(y + 1, 3, 31), null, 121,
                "เก็บได้เฉพาะวันที่ 12 ธ.ค. เท่านั้น 121 ใบ ลด 12% เข้าพักถึง 31 มี.ค."),
            new($"p-fathers-{y}", $"วันพ่อแห่งชาติ {Be(y)}", "CODE", 12, 1200, $"DAD{yy}", true,
                new(y, 11, 20), new(y, 12, 5), new(y, 11, 25), new(y, 12, 20), null, 205,
                "พาคุณพ่อพักผ่อน เก็บคูปองถึง 5 ธ.ค. ลด 12% เข้าพัก 25 พ.ย. – 20 ธ.ค."),
        }.Concat(FestivalPricing.LoyKrathong.TryGetValue(y, out var lk)
            ? new[] { new Campaign($"p-loykrathong-{y}", $"ลอยกระทง {Be(y)}", "CODE", 12, 1200, $"KRATHONG{yy}", true,
                lk.AddDays(-14), lk, lk.AddDays(-4), lk.AddDays(21), null, 150,
                "คืนเพ็ญเดือนสิบสอง เก็บคูปองได้ถึงวันลอยกระทง ลด 12% เข้าพักได้ถึง 3 สัปดาห์หลังเทศกาล") }
            : Array.Empty<Campaign>()).ToList();
    }

    /// <summary>สร้างแคมเปญที่ยังไม่มี (ปีนี้ + ปีหน้า ข้ามแคมเปญที่หมดเวลาแล้ว) — คืนจำนวนที่เพิ่ม</summary>
    public static async Task<int> EnsureAsync(Csi402dbContext db, DateOnly today)
    {
        var wanted = For(today.Year).Concat(For(today.Year + 1))
            .Where(c => (c.Claimable ? c.ClaimUntil!.Value : c.End) >= today).ToList();
        var keys = wanted.Select(c => c.Key).ToList();
        var codes = wanted.Where(c => c.Code != null).Select(c => c.Code!).ToList();
        var have = await db.Promotions.Where(p => (p.AutoKey != null && keys.Contains(p.AutoKey)) || (p.PromoCode != null && codes.Contains(p.PromoCode)))
            .Select(p => new { p.AutoKey, p.PromoCode }).ToListAsync();
        var add = wanted.Where(c => !have.Any(h => h.AutoKey == c.Key || (c.Code != null && h.PromoCode == c.Code))).ToList();
        if (add.Count == 0) return 0;
        foreach (var c in add)
            db.Promotions.Add(new Models.Promotion
            {
                PromoName = c.Name, PromoType = c.Type, DiscountPercent = c.Percent, MaxDiscount = c.Cap, PromoCode = c.Code,
                Claimable = c.Claimable, ClaimFrom = c.ClaimFrom, ClaimUntil = c.ClaimUntil, StartDate = c.Start, EndDate = c.End,
                MinNights = c.MinNights, MaxUses = c.MaxUses, MaxPerUser = c.Claimable ? (short)1 : null, Description = c.Description,
                IsActive = true, Featured = true, AutoKey = c.Key, NeedStaffConfirm = false
            });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return 0; }
        return add.Count;
    }

    /// <summary>สถานะการเก็บคูปอง: none = ไม่ต้องเก็บ (ใช้ได้ทันที), open, soon, closed, soldout</summary>
    public static string ClaimState(Models.Promotion p, int claims, DateOnly today)
    {
        if (!p.Claimable) return "none";
        if (p.EndDate is { } e && e < today) return "closed";
        if (p.ClaimFrom is { } f && today < f) return "soon";
        if (p.ClaimUntil is { } u && today > u) return "closed";
        if (p.MaxUses is { } mu && claims >= mu) return "soldout";
        return "open";
    }

    /// <summary>ธีมสีของการ์ด/แบนเนอร์ตามเทศกาล</summary>
    public static string Theme(Models.Promotion p)
    {
        var k = p.AutoKey ?? "";
        if (k.StartsWith("p-newyear")) return "newyear";
        if (k.StartsWith("p-valentine")) return "love";
        if (k.StartsWith("p-songkran")) return "water";
        if (k.StartsWith("p-green")) return "green";
        if (k.StartsWith("p-1111") || k.StartsWith("p-1212")) return "mega";
        if (k.StartsWith("p-loykrathong")) return "water";
        if (k.StartsWith("p-fathers")) return "gold";
        return p.PromoType switch { "MOTHERS_DAY" => "love", "EARLY_BIRD" => "water", "MEMBER" => "gold", "BIRTHDAY" or "ANNIVERSARY" => "love", _ => "gold" };
    }

    /// <summary>การ์ดโปรทั้งหมดที่ลูกค้าเห็น (รวมคูปองกดเก็บ) พร้อมสถานะของผู้ใช้คนนี้ · ซ่อนแคมเปญที่ยังไกลเกิน 60 วัน</summary>
    public static async Task<List<PromoPublicViewModel>> CardsAsync(Csi402dbContext db, DateOnly today, int userId)
    {
        var rows = await db.Promotions.AsNoTracking()
            .Where(p => p.IsActive == true && (p.PromoType != "CODE" || p.Claimable) && (p.EndDate == null || p.EndDate >= today))
            .Select(p => new
            {
                P = p,
                Used = p.Bookings.Count(b => b.Status != "CANCELLED"),
                Claims = p.Recipients.Count(),
                Mine = p.Recipients.Any(r => r.UserId == userId),
                MyUses = p.Bookings.Count(b => b.Status != "CANCELLED" && b.UserId == userId)
            }).ToListAsync();
        var list = new List<PromoPublicViewModel>();
        foreach (var x in rows)
        {
            var p = x.P;
            var state = ClaimState(p, x.Claims, today);
            var shownFrom = p.Claimable ? p.ClaimFrom : p.StartDate;
            if (p.AutoKey != null && shownFrom is { } sf && sf.DayNumber - today.DayNumber > 60) continue;   // แคมเปญอัตโนมัติที่ยังไกลเกิน ยังไม่โฆษณา
            if (!p.Claimable && p.MaxUses is { } mu && x.Used >= mu) continue;                  // สิทธิ์เต็ม
            if (p.Claimable && state is "closed" or "soldout" && !x.Mine) continue;            // หมดเวลาเก็บ และเราไม่ได้เก็บไว้
            list.Add(new PromoPublicViewModel
            {
                Id = p.PromoId, Name = p.PromoName, Type = p.PromoType, Percent = p.DiscountPercent, Description = p.Description,
                Condition = ThaiLabels.PromoCondition(p, x.Used), Start = p.StartDate, End = p.EndDate,
                MinNights = p.PromoType == "LONG_STAY" ? p.MinNights ?? 3 : p.MinNights,
                MinAdvanceDays = p.PromoType == "EARLY_BIRD" ? p.MinAdvanceDays ?? 30 : null,
                Remaining = p.MaxUses is { } m ? Math.Max(0, m - (p.Claimable ? x.Claims : x.Used)) : null,
                Upcoming = p.Claimable ? state == "soon" : p.StartDate is { } s && s > today,
                Claimable = p.Claimable, ClaimFrom = p.ClaimFrom, ClaimUntil = p.ClaimUntil, ClaimState = state,
                Claimed = x.Mine, UsedUp = x.Mine && p.MaxPerUser is { } mpu && x.MyUses >= mpu,
                Featured = p.Featured, Theme = Theme(p), MaxDiscount = p.MaxDiscount
            });
        }
        return list.OrderBy(c => c.Upcoming).ThenByDescending(c => c.Claimable && c.ClaimState == "open" && !c.Claimed)
            .ThenByDescending(c => c.Featured).ThenByDescending(c => c.Percent).ToList();
    }

    /// <summary>กดเก็บคูปอง — คืน (สำเร็จ, ข้อความ)</summary>
    public static async Task<(bool Ok, string Message)> ClaimAsync(Csi402dbContext db, int promoId, int userId, DateOnly today)
    {
        var p = await db.Promotions.FirstOrDefaultAsync(x => x.PromoId == promoId && x.IsActive == true && x.Claimable);
        if (p == null) return (false, "ไม่พบคูปองนี้ หรือคูปองปิดไปแล้ว");
        if (await db.PromoRecipients.AnyAsync(r => r.PromoId == promoId && r.UserId == userId))
            return (true, $"คุณเก็บคูปอง \"{p.PromoName}\" ไว้แล้ว ระบบจะใช้ให้อัตโนมัติเมื่อการจองตรงเงื่อนไข");
        var claims = await db.PromoRecipients.CountAsync(r => r.PromoId == promoId);
        switch (ClaimState(p, claims, today))
        {
            case "soon": return (false, $"คูปองนี้เริ่มเก็บได้วันที่ {p.ClaimFrom:dd/MM/yyyy}");
            case "closed": return (false, "หมดเวลาเก็บคูปองนี้แล้ว");
            case "soldout": return (false, "คูปองนี้ถูกเก็บครบจำนวนแล้ว");
        }
        db.PromoRecipients.Add(new PromoRecipient { PromoId = promoId, UserId = userId, SentAt = pameka_7033_l005.Helpers.AppClock.Now, SentBy = "เก็บคูปองเอง" });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return (true, "เก็บคูปองไว้แล้ว"); }   // กดซ้ำพร้อมกัน (unique key)
        return (true, $"เก็บคูปอง \"{p.PromoName}\" แล้ว ลด {p.DiscountPercent:0.##}% · ระบบจะใช้ให้อัตโนมัติตอนจองที่ตรงเงื่อนไข");
    }
}

// ======================================================================
// DemoData (Part 50) — ข้อมูลลูกค้าตัวอย่างสำหรับนำเสนอ
// ======================================================================
/// <summary>
/// สร้างลูกค้าตัวอย่าง 30 คน พร้อมประวัติการจอง/การชำระเงิน/รีวิว/ข้อความติดต่อ เพื่อใช้นำเสนอ (รายงาน กราฟ และหน้าต่าง ๆ มีข้อมูลให้ดู)
/// · ทุกบัญชีใช้อีเมล @demo.aurellestay.local (เป็นชื่อสมมติ) → ลบออกได้ทั้งหมดในคลิกเดียว ไม่แตะข้อมูลจริง
/// · ไม่จองห้องทับกับการจองที่มีอยู่ · ไม่เปลี่ยนสถานะห้อง · สุ่มแบบคงที่ (ได้ข้อมูลชุดเดิมทุกครั้ง)
/// </summary>
public static class DemoData
{
    public const string Domain = "@demo.aurellestay.local";
    public const string Password = "Demo@2026";

    private static readonly string[] First = { "สมชาย", "สมหญิง", "วิภาวดี", "ธนากร", "ปิยะพงษ์", "กมลชนก", "ณัฐวุฒิ", "สุภาวดี", "อรรถพล", "พิมพ์ชนก",
        "ชยพล", "ศิริพร", "ภูริทัต", "จิราพร", "ธีรวัฒน์", "นันทนา", "วรเมธ", "ปวีณา", "กิตติพัฒน์", "มณีรัตน์",
        "อนุชา", "ชลธิชา", "ธนวัฒน์", "รัชนี", "พีรพล", "เบญจวรรณ", "ศุภชัย", "ดวงใจ", "ณัฐธิดา", "วีรยุทธ" };
    private static readonly string[] Last = { "ใจดี", "รักสงบ", "ศรีสุข", "มั่นคง", "วงศ์ไทย", "แสงทอง", "บุญมา", "ทองคำ", "พึ่งบุญ", "สายสุวรรณ",
        "เพชรรัตน์", "จันทร์เพ็ญ", "กาญจนา", "ศักดิ์ดี", "รุ่งเรือง", "สมบูรณ์", "ประเสริฐ", "วัฒนา", "ทรัพย์มาก", "อินทร์แก้ว" };
    private static readonly string[] Comments = {
        "ห้องสะอาด เตียงนอนสบายมาก พนักงานบริการดีเยี่ยม", "ทำเลดี เดินทางสะดวก อาหารเช้าอร่อย จะกลับมาพักอีก",
        "ห้องกว้าง ตกแต่งสวย เหมาะกับการพักผ่อนกับครอบครัว", "เช็คอินรวดเร็ว จองผ่านเว็บง่ายมาก ชำระเงินผ่าน QR สะดวก",
        "คุ้มค่ากับราคา ได้ส่วนลดสมาชิกด้วย", "บรรยากาศเงียบสงบ นอนหลับสบาย", "พนักงานยิ้มแย้ม ช่วยเหลือดีทุกอย่าง",
        "ห้องน้ำสะอาด อุปกรณ์ครบ", "โดยรวมดี แต่ห้องอยู่ใกล้ลิฟต์เสียงดังนิดหน่อย", "ฉลองวันเกิดที่นี่ ทางโรงแรมจัดเค้กให้ ประทับใจมาก" };
    private static readonly string[] Messages = {
        "สอบถามว่ามีที่จอดรถสำหรับผู้เข้าพักไหมคะ", "ต้องการจองห้องสำหรับงานสัมมนา 10 ห้อง ขอใบเสนอราคาครับ",
        "ขอเปลี่ยนวันเข้าพักจากวันศุกร์เป็นวันเสาร์ได้ไหมคะ", "ลืมเสื้อแจ็คเก็ตไว้ที่ห้อง ช่วยเก็บไว้ให้หน่อยครับ", "มีบริการรับส่งสนามบินไหมคะ" };

    public static Task<int> CountAsync(Csi402dbContext db) => db.Users.CountAsync(u => u.Email.EndsWith(Domain));

    /// <summary>สร้างข้อมูลตัวอย่าง (ถ้ามีอยู่แล้วจะไม่สร้างซ้ำ) — คืนข้อความสรุป</summary>
    public static async Task<string> SeedAsync(Csi402dbContext db, DateTime now)
    {
        if (await CountAsync(db) > 0) return "มีข้อมูลลูกค้าตัวอย่างอยู่แล้ว (ลบก่อนถ้าต้องการสร้างใหม่)";
        var role = await db.Roles.FirstOrDefaultAsync(r => r.RoleName == RoleNames.Customer);
        var rooms = await db.Rooms.AsNoTracking().Include(r => r.Type).Where(r => r.Status != "MAINTENANCE").OrderBy(r => r.RoomNo).ToListAsync();
        if (role == null || rooms.Count == 0) return "ยังไม่มีห้องพักหรือบทบาทลูกค้าในระบบ";
        var memberPromo = await db.Promotions.AsNoTracking().FirstOrDefaultAsync(p => p.PromoType == "MEMBER" && p.IsActive == true);
        var today = DateOnly.FromDateTime(now);
        var rnd = new Random(2026);

        // ห้องที่ไม่ว่างอยู่แล้ว (การจองจริงที่ยังไม่ยกเลิก) → ห้ามจองทับ
        var from = today.AddDays(-200); var to = today.AddDays(90);
        var taken = (await db.BookingRooms.AsNoTracking()
            .Where(br => br.Booking.Status != "CANCELLED" && br.Booking.Status != "NO_SHOW" && br.Booking.CheckOut > from && br.Booking.CheckIn < to)
            .Select(br => new { br.RoomId, br.Booking.CheckIn, br.Booking.CheckOut }).ToListAsync())
            .Select(x => (x.RoomId, x.CheckIn, x.CheckOut)).ToList();
        bool Free(int roomId, DateOnly ci, DateOnly co) => !taken.Any(t => t.RoomId == roomId && t.CheckIn < co && ci < t.CheckOut);

        var hash = PasswordHelper.Hash(Password);
        var phones = (await db.Users.Select(u => u.Phone).ToListAsync()).ToHashSet();
        var users = new List<Models.User>();
        for (var i = 0; i < 30; i++)
        {
            var phone = $"0899{i:000}{rnd.Next(100, 999)}";
            while (phones.Contains(phone)) phone = $"0899{i:000}{rnd.Next(100, 999)}";
            phones.Add(phone);
            var u = new Models.User
            {
                FirstName = First[i], LastName = Last[rnd.Next(Last.Length)], Phone = phone,
                Email = $"guest{i + 1:00}{Domain}", PasswordHash = hash, SecurityStamp = Guid.NewGuid().ToString("N"),
                RoleId = role.RoleId, IsMember = true, CreatedAt = now.AddDays(-rnd.Next(20, 240)).AddMinutes(-rnd.Next(0, 1440)),
                BirthDate = rnd.Next(3) == 0 ? new DateOnly(1975 + rnd.Next(30), 1 + rnd.Next(12), 1 + rnd.Next(28)) : null
            };
            users.Add(u); db.Users.Add(u);
        }
        await db.SaveChangesAsync();

        int bookings = 0, reviews = 0;
        foreach (var u in users)
        {
            var trips = 1 + rnd.Next(3);
            for (var t = 0; t < trips; t++)
            {
                // ช่วงเวลา: อดีต 70% (เข้าพักแล้ว) · อนาคต 30% (จองล่วงหน้า)
                var past = rnd.NextDouble() < 0.7;
                var offset = past ? -rnd.Next(5, 180) : rnd.Next(2, 75);
                var nights = 1 + rnd.Next(rnd.Next(4) == 0 ? 5 : 3);
                var ci = today.AddDays(offset); var co = ci.AddDays(nights);
                if (past && co > today) continue;
                var need = rnd.Next(6) == 0 ? 2 : 1;
                var chosen = rooms.OrderBy(_ => rnd.Next()).Where(r => Free(r.RoomId, ci, co)).Take(need).ToList();
                if (chosen.Count == 0) continue;
                var guests = (short)Math.Min(chosen.Sum(r => (int)r.Type.MaxGuests), 1 + rnd.Next(3));

                string status; bool paid;
                if (past)
                {
                    var roll = rnd.Next(20);
                    status = roll == 0 ? "NO_SHOW" : roll < 3 ? "CANCELLED" : "CHECKED_OUT"; paid = status != "CANCELLED" || rnd.Next(2) == 0;
                }
                else
                {
                    var roll = rnd.Next(10);
                    status = roll < 7 ? "CONFIRMED" : roll < 9 ? "PENDING" : "CANCELLED"; paid = status == "CONFIRMED";
                }
                var bookedAt = status == "PENDING" ? now.AddHours(-rnd.Next(1, 10)) : now.AddDays(Math.Min(offset, 0) - rnd.Next(3, 45)).AddMinutes(-rnd.Next(0, 1440));
                var roomsTotal = chosen.Sum(r => r.Type.BasePrice * nights);
                var pct = memberPromo?.DiscountPercent ?? 0;
                var discount = Math.Round(roomsTotal * pct / 100m, 2, MidpointRounding.AwayFromZero);
                var b = new Booking
                {
                    UserId = u.UserId, CheckIn = ci, CheckOut = co, BookedAt = bookedAt, Status = status,
                    Subtotal = roomsTotal, DiscountTotal = discount, NetTotal = roomsTotal - discount,
                    PromoId = discount > 0 ? memberPromo!.PromoId : null, Guests = (sbyte)guests,
                    ArrivalTime = status == "CONFIRMED" && rnd.Next(2) == 0 ? $"{14 + rnd.Next(7)}:{(rnd.Next(2) == 0 ? "00" : "30")}" : null,
                    GuestRequest = status == "CONFIRMED" && rnd.Next(3) == 0 ? new[] { "ขอหมอนเพิ่ม 2 ใบ", "ขอห้องชั้นสูง วิวเมือง", "มาถึงดึก รบกวนเก็บห้องไว้ให้ด้วย", "ฉลองครบรอบแต่งงาน" }[rnd.Next(4)] : null
                };
                foreach (var r in chosen)
                {
                    b.BookingRooms.Add(new BookingRoom { RoomId = r.RoomId, PricePerNight = r.Type.BasePrice, Nights = (short)nights });
                    taken.Add((r.RoomId, ci, co));
                }
                if (paid)
                {
                    var paidAt = bookedAt.AddHours(rnd.Next(1, 20));
                    b.Payments.Add(new Payment { Amount = b.NetTotal, PaymentType = BookingWorkflow.PaymentType, Method = rnd.Next(5) == 0 ? "CASH" : "QR",
                        Status = status == "CANCELLED" ? "REFUNDED" : "PAID", PaidAt = paidAt, Reference = $"DEMO{rnd.Next(10000000, 99999999)}" });
                }
                else if (status == "PENDING" && rnd.Next(2) == 0)
                {
                    b.Payments.Add(new Payment { Amount = b.NetTotal, PaymentType = BookingWorkflow.PaymentType, Method = "QR", Status = "PENDING",
                        PaidAt = bookedAt.AddMinutes(rnd.Next(10, 50)), Reference = $"DEMO{rnd.Next(10000000, 99999999)}" });
                }
                db.Bookings.Add(b); bookings++;
                if (status == "CHECKED_OUT" && rnd.Next(10) < 6)
                {
                    db.Reviews.Add(new Review { Booking = b, UserId = u.UserId, Rating = (sbyte)(rnd.Next(10) < 7 ? 5 : rnd.Next(10) < 7 ? 4 : 3),
                        Comment = Comments[rnd.Next(Comments.Length)], CreatedAt = co.ToDateTime(new TimeOnly(10 + rnd.Next(10), rnd.Next(60))) });
                    reviews++;
                }
            }
        }
        for (var i = 0; i < Messages.Length; i++)
        {
            var u = users[i * 5];
            db.ContactMessages.Add(new ContactMessage { UserId = u.UserId, Name = u.FirstName + " " + u.LastName, Email = u.Email, Phone = u.Phone,
                Topic = i == 1 ? "BOOKING" : "GENERAL", Message = Messages[i], Status = i < 2 ? "NEW" : "DONE",
                StaffNote = i < 2 ? null : "ตอบกลับลูกค้าทางโทรศัพท์แล้ว", HandledAt = i < 2 ? null : now.AddDays(-i), CreatedAt = now.AddDays(-i - 1).AddHours(-rnd.Next(12)) });
        }
        await db.SaveChangesAsync();

        // Part 52: จองกิจกรรมตัวอย่าง — ผู้เข้าพักที่ยืนยัน/เข้าพักอยู่ในอีก 14 วันข้างหน้า (ไม่เกินที่นั่ง)
        var acts = await db.Activities.AsNoTracking().Where(a => a.IsActive).ToListAsync();
        var acts_n = 0;
        if (acts.Count > 0)
        {
            var uids = users.Select(u => u.UserId).ToList();
            var aToday = DateOnly.FromDateTime(now);
            var stays = await db.Bookings.AsNoTracking().Where(b => uids.Contains(b.UserId) && (b.Status == "CONFIRMED" || b.Status == "CHECKED_IN")
                && b.CheckOut >= aToday && b.CheckIn <= aToday.AddDays(14)).ToListAsync();
            var aTaken = await HotelActivities.TakenAsync(db, aToday, aToday.AddDays(15));
            foreach (var b in stays)
            {
                var d = b.CheckIn < aToday.AddDays(1) ? aToday.AddDays(1) : b.CheckIn;
                if (d > b.CheckOut) continue;
                var a = acts[rnd.Next(acts.Count)];
                if (!HotelActivities.OpenOn(a, d)) continue;
                var t = HotelActivities.ParseTimes(a.Times).FirstOrDefault();
                if (t == null) continue;
                var ppl = 1 + rnd.Next(2);
                var key = (a.ActivityId, d, t);
                if (aTaken.GetValueOrDefault(key) + ppl > a.Capacity) continue;
                aTaken[key] = aTaken.GetValueOrDefault(key) + ppl;
                db.ActivityBookings.Add(new ActivityBooking { ActivityId = a.ActivityId, UserId = b.UserId, BookingId = b.BookingId, ActDate = d, StartTime = t,
                    People = (byte)ppl, Total = a.Price * ppl, Status = "CONFIRMED", CreatedAt = now.AddHours(-rnd.Next(1, 48)) });
                acts_n++;
            }
            await db.SaveChangesAsync();
        }
        return $"สร้างลูกค้าตัวอย่าง {users.Count} คน · การจอง {bookings} รายการ · รีวิว {reviews} รายการ · ข้อความ {Messages.Length} รายการ" + (acts_n > 0 ? $" · จองกิจกรรม {acts_n} รายการ" : "") + $" (รหัสผ่านทุกบัญชี {Password})";
    }

    /// <summary>ลบเฉพาะข้อมูลตัวอย่าง (บัญชี @demo.aurellestay.local และทุกอย่างที่ผูกกับบัญชีนั้น)</summary>
    public static async Task<string> RemoveAsync(Csi402dbContext db)
    {
        var ids = await db.Users.Where(u => u.Email.EndsWith(Domain)).Select(u => u.UserId).ToListAsync();
        if (ids.Count == 0) return "ไม่มีข้อมูลตัวอย่างให้ลบ";
        var bids = await db.Bookings.Where(b => ids.Contains(b.UserId)).Select(b => b.BookingId).ToListAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Reviews.Where(r => bids.Contains(r.BookingId) || ids.Contains(r.UserId)).ExecuteDeleteAsync();
        await db.DeliveryTasks.Where(t => bids.Contains(t.Bs.BookingId)).ExecuteDeleteAsync();
        await db.BookingServices.Where(s => bids.Contains(s.BookingId)).ExecuteDeleteAsync();
        await db.HousekeepingTasks.Where(t => t.BookingId != null && bids.Contains(t.BookingId.Value)).ExecuteDeleteAsync();
        await db.Payments.Where(p => bids.Contains(p.BookingId)).ExecuteDeleteAsync();
        await db.BookingRooms.Where(br => bids.Contains(br.BookingId)).ExecuteDeleteAsync();
        await db.Bookings.Where(b => bids.Contains(b.BookingId)).ExecuteDeleteAsync();
        await db.Notifications.Where(n => ids.Contains(n.UserId)).ExecuteDeleteAsync();
        await db.PromoRecipients.Where(r => ids.Contains(r.UserId)).ExecuteDeleteAsync();
        await db.LoginHistories.Where(l => ids.Contains(l.UserId)).ExecuteDeleteAsync();
        await db.ContactMessages.Where(m => m.Email.EndsWith(Domain)).ExecuteDeleteAsync();
        await db.Users.Where(u => ids.Contains(u.UserId)).ExecuteDeleteAsync();
        await tx.CommitAsync();
        return $"ลบลูกค้าตัวอย่าง {ids.Count} คน และการจอง {bids.Count} รายการแล้ว";
    }
}

// ======================================================================
// Part 51: รายชื่อรอห้องว่าง (Waitlist)
// ======================================================================
public static class Waitlist
{
    public const string TableSql = @"CREATE TABLE IF NOT EXISTS `room_waitlist` (
    `wait_id` int NOT NULL AUTO_INCREMENT,
    `user_id` int NOT NULL,
    `type_id` int NULL,
    `check_in` date NOT NULL,
    `check_out` date NOT NULL,
    `guests` tinyint unsigned NOT NULL DEFAULT 1,
    `status` varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'WAITING',
    `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `notified_at` datetime NULL,
    PRIMARY KEY (`wait_id`),
    KEY `ix_wait_status` (`status`, `check_in`),
    KEY `ix_wait_user` (`user_id`),
    KEY `ix_wait_type` (`type_id`),
    CONSTRAINT `room_waitlist_ibfk_1` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE CASCADE,
    CONSTRAINT `room_waitlist_ibfk_2` FOREIGN KEY (`type_id`) REFERENCES `room_types` (`type_id`) ON DELETE CASCADE,
    CONSTRAINT `chk_wait_dates` CHECK (`check_out` > `check_in`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

    public const int MaxActivePerUser = 5;

    /// <summary>ลงชื่อรอ: ตรวจวันที่ จำนวนรายการ และไม่ซ้ำช่วงเดิม · ถ้าห้องว่างอยู่แล้วจะไม่ให้ลง (ให้จองได้เลย)</summary>
    public static async Task<(bool Ok, string Message)> JoinAsync(Csi402dbContext db, int userId, int? typeId, DateOnly? checkIn, DateOnly? checkOut, int guests, DateOnly today)
    {
        var err = BookingRules.ValidateDates(checkIn, checkOut, today);
        if (err != null) return (false, err);
        if ((checkOut!.Value.DayNumber - checkIn!.Value.DayNumber) > 30) return (false, "ช่วงวันที่รอได้ไม่เกิน 30 คืน");
        guests = Math.Clamp(guests, 1, 20);
        if (typeId != null && !await db.RoomTypes.AnyAsync(t => t.TypeId == typeId)) return (false, "ไม่พบประเภทห้องนี้");
        if (await AvailableCountAsync(db, typeId, checkIn.Value, checkOut.Value, guests) > 0)
            return (false, "ตอนนี้มีห้องว่างในช่วงนี้อยู่แล้ว จองได้ทันที");
        var active = await db.Waitlist.CountAsync(w => w.UserId == userId && w.Status == "WAITING");
        if (active >= MaxActivePerUser) return (false, $"ลงชื่อรอได้พร้อมกันไม่เกิน {MaxActivePerUser} รายการ");
        if (await db.Waitlist.AnyAsync(w => w.UserId == userId && w.Status == "WAITING" && w.TypeId == typeId && w.CheckIn == checkIn && w.CheckOut == checkOut))
            return (false, "คุณลงชื่อรอช่วงนี้ไว้แล้ว");
        db.Waitlist.Add(new WaitlistEntry { UserId = userId, TypeId = typeId, CheckIn = checkIn.Value, CheckOut = checkOut.Value, Guests = (byte)guests, Status = "WAITING", CreatedAt = AppClock.Now });
        await db.SaveChangesAsync();
        return (true, "ลงชื่อรอแล้ว ระบบจะแจ้งเตือนทันทีที่มีห้องว่างในช่วงนี้ (ดูได้ที่หน้าสมาชิก)");
    }

    public static Task<int> AvailableCountAsync(Csi402dbContext db, int? typeId, DateOnly checkIn, DateOnly checkOut, int guests) =>
        BookingQueries.Available(db, checkIn, checkOut).Where(r => (typeId == null || r.TypeId == typeId) && r.Type.MaxGuests >= guests).CountAsync();

    /// <summary>ตรวจรายชื่อรอทั้งหมด: มีห้องว่างครบช่วงแล้ว → แจ้งเตือน (ในเว็บ + อีเมล) · เลยวันเข้าพัก → ปิดรายการ</summary>
    public static async Task<int> CheckAsync(Csi402dbContext db, DateOnly today, int? userId = null)
    {
        var mine = db.Waitlist.Where(w => userId == null || w.UserId == userId);
        var expired = await mine.Where(w => w.Status == "WAITING" && w.CheckIn < today).ToListAsync();
        foreach (var w in expired) w.Status = "CANCELLED";
        var list = await mine.Include(w => w.Type).Where(w => w.Status == "WAITING" && w.CheckIn >= today).OrderBy(w => w.CreatedAt).Take(500).ToListAsync();
        var n = 0;
        foreach (var w in list)
        {
            if (await AvailableCountAsync(db, w.TypeId, w.CheckIn, w.CheckOut, w.Guests) == 0) continue;
            w.Status = "NOTIFIED"; w.NotifiedAt = AppClock.Now; n++;
            var what = w.Type?.TypeName ?? "ห้องพัก";
            var link = $"/Booking/Create?checkIn={w.CheckIn:yyyy-MM-dd}&checkOut={w.CheckOut:yyyy-MM-dd}&guests={w.Guests}";
            Notify.Add(db, w.UserId, $"มีห้องว่างแล้ว: {what}",
                $"ช่วง {w.CheckIn:dd/MM/yyyy} – {w.CheckOut:dd/MM/yyyy} ที่คุณลงชื่อรอไว้ มีห้องว่างแล้ว รีบจองก่อนห้องเต็มอีกครั้ง", link);
        }
        if (expired.Count > 0 || n > 0) await db.SaveChangesAsync();
        return n;
    }
}

// ======================================================================
// Part 52: กิจกรรมของโรงแรม
// ======================================================================
public static class HotelActivities
{
    public const string ActivitiesTableSql = @"CREATE TABLE IF NOT EXISTS `activities` (
    `activity_id` int NOT NULL AUTO_INCREMENT,
    `name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `category` varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'CULTURE',
    `description` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `price` decimal(10,2) NOT NULL DEFAULT 0,
    `duration_min` smallint NOT NULL DEFAULT 60,
    `capacity` smallint NOT NULL DEFAULT 10,
    `times` varchar(60) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '09:00',
    `days` varchar(7) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '0123456',
    `location` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `is_active` tinyint(1) NOT NULL DEFAULT 1,
    `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`activity_id`),
    UNIQUE KEY `ux_activity_name` (`name`),
    CONSTRAINT `chk_activity_price` CHECK (`price` >= 0),
    CONSTRAINT `chk_activity_capacity` CHECK (`capacity` BETWEEN 1 AND 200),
    CONSTRAINT `chk_activity_duration` CHECK (`duration_min` BETWEEN 15 AND 720)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

    public const string BookingsTableSql = @"CREATE TABLE IF NOT EXISTS `activity_bookings` (
    `ab_id` int NOT NULL AUTO_INCREMENT,
    `activity_id` int NOT NULL,
    `user_id` int NOT NULL,
    `booking_id` int NULL,
    `act_date` date NOT NULL,
    `start_time` char(5) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `people` tinyint unsigned NOT NULL DEFAULT 1,
    `total` decimal(10,2) NOT NULL DEFAULT 0,
    `status` varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'CONFIRMED',
    `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `cancelled_at` datetime NULL,
    PRIMARY KEY (`ab_id`),
    KEY `ix_ab_slot` (`activity_id`, `act_date`, `start_time`),
    KEY `ix_ab_user` (`user_id`),
    KEY `ix_ab_booking` (`booking_id`),
    CONSTRAINT `activity_bookings_ibfk_1` FOREIGN KEY (`activity_id`) REFERENCES `activities` (`activity_id`),
    CONSTRAINT `activity_bookings_ibfk_2` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE CASCADE,
    CONSTRAINT `activity_bookings_ibfk_3` FOREIGN KEY (`booking_id`) REFERENCES `bookings` (`booking_id`) ON DELETE CASCADE,
    CONSTRAINT `chk_ab_people` CHECK (`people` BETWEEN 1 AND 20)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

    public static readonly (string Key, string Label, string Icon)[] Categories =
    {
        ("CULTURE", "วัฒนธรรมไทย", "M4 21h16M6 21V10l6-6 6 6v11M10 21v-6h4v6"),
        ("FOOD", "อาหาร", "M7 3v8M5 3v5a2 2 0 0 0 4 0V3M7 11v10M17 3c-2 0-3 2-3 5s1 4 3 4v9"),
        ("WELLNESS", "สุขภาพ / โยคะ", "M12 6a2 2 0 1 0 0-.01M5 21l4-7 3 2 3-2 4 7M7 10h10"),
        ("TOUR", "ทัวร์ / นอกสถานที่", "M3 18h18M5 18l2-8h10l2 8M9 10V6h6v4"),
        ("KIDS", "ครอบครัว / เด็ก", "M9 7a2 2 0 1 0 0-.01M15 9a1.6 1.6 0 1 0 0-.01M6 21v-6l3-4 3 4v6M13 21v-5l2-3 2 3v5"),
    };
    public static bool IsCategory(string? k) => Categories.Any(c => c.Key == k);
    public static string CategoryLabel(string? k) => Categories.FirstOrDefault(c => c.Key == k).Label ?? "อื่น ๆ";
    public static string CategoryIcon(string? k) => Categories.FirstOrDefault(c => c.Key == k).Icon ?? Categories[0].Icon;

    public const int MaxPeople = 10;
    public const int CancelBeforeHours = 2;
    public const int BookAheadDays = 60;

    public static readonly string[] DayNames = { "อา", "จ", "อ", "พ", "พฤ", "ศ", "ส" };

    /// <summary>รอบเวลาที่ถูกต้อง (HH:mm) เรียงจากเช้าไปเย็น ตัดค่าที่ผิดรูปแบบ/ซ้ำทิ้ง</summary>
    public static List<string> ParseTimes(string? times) =>
        (times ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => TimeOnly.TryParseExact(t.Trim(), "H:mm", out var x) ? x.ToString("HH:mm") : null)
            .Where(t => t != null).Select(t => t!).Distinct().OrderBy(t => t).ToList();

    public static string NormalizeDays(string? days) =>
        new string((days ?? "").Where(c => c is >= '0' and <= '6').Distinct().OrderBy(c => c).ToArray());

    public static bool OpenOn(Activity a, DateOnly d) => a.Days.Contains((char)('0' + (int)d.DayOfWeek));

    public static string DaysLabel(string days) => days.Length == 7 ? "ทุกวัน" : string.Join(" ", days.Select(c => DayNames[c - '0']));

    /// <summary>ที่นั่งที่ถูกจองแล้วของแต่ละรอบ (กิจกรรม, วันที่, เวลา) → จำนวนคน</summary>
    public static async Task<Dictionary<(int, DateOnly, string), int>> TakenAsync(Csi402dbContext db, DateOnly from, DateOnly to, int? activityId = null) =>
        (await db.ActivityBookings.Where(b => b.ActDate >= from && b.ActDate <= to && (b.Status == "CONFIRMED" || b.Status == "ATTENDED")
                && (activityId == null || b.ActivityId == activityId))
            .GroupBy(b => new { b.ActivityId, b.ActDate, b.StartTime }).Select(g => new { g.Key.ActivityId, g.Key.ActDate, g.Key.StartTime, N = g.Sum(x => (int)x.People) })
            .ToListAsync()).ToDictionary(x => (x.ActivityId, x.ActDate, x.StartTime), x => x.N);

    /// <summary>การจองห้องที่ทำให้ลูกค้ามีสิทธิ์ร่วมกิจกรรมในวันนั้น (ยืนยันแล้ว/เข้าพักอยู่ · วันเข้าพัก ≤ วันนั้น ≤ วันออก)</summary>
    public static Task<Booking?> StayOnAsync(Csi402dbContext db, int userId, DateOnly d) =>
        db.Bookings.Where(b => b.UserId == userId && (b.Status == "CONFIRMED" || b.Status == "CHECKED_IN") && b.CheckIn <= d && b.CheckOut >= d)
            .OrderBy(b => b.CheckIn).FirstOrDefaultAsync();

    /// <summary>จองกิจกรรม: ตรวจรอบ วัน เวลา สิทธิ์การเข้าพัก และที่นั่ง (ล็อกแถวกิจกรรมกันจองเกินพร้อมกัน)</summary>
    public static async Task<(bool Ok, string Message, int? Id)> BookAsync(Csi402dbContext db, int userId, int activityId, DateOnly? date, string? time, int people, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        if (date is not { } d) return (false, "กรุณาเลือกวันที่", null);
        if (d < today || d > today.AddDays(BookAheadDays)) return (false, $"เลือกวันที่ได้ตั้งแต่วันนี้ถึง {BookAheadDays} วันข้างหน้า", null);
        if (people < 1 || people > MaxPeople) return (false, $"จำนวนผู้ร่วมกิจกรรม 1–{MaxPeople} คน", null);
        var a = await db.Activities.AsNoTracking().FirstOrDefaultAsync(x => x.ActivityId == activityId && x.IsActive);
        if (a == null) return (false, "ไม่พบกิจกรรมนี้ หรือปิดรับจองแล้ว", null);
        if (!OpenOn(a, d)) return (false, $"กิจกรรมนี้เปิดเฉพาะวัน {DaysLabel(a.Days)}", null);
        if (time == null || !ParseTimes(a.Times).Contains(time)) return (false, "กรุณาเลือกรอบเวลาที่มีในตาราง", null);
        var start = d.ToDateTime(TimeOnly.ParseExact(time, "HH:mm"));
        if (start <= now.AddMinutes(30)) return (false, "รอบนี้เริ่มแล้วหรือใกล้เริ่ม (ต้องจองล่วงหน้าอย่างน้อย 30 นาที)", null);
        var stay = await StayOnAsync(db, userId, d);
        if (stay == null) return (false, "กิจกรรมสำหรับผู้เข้าพัก: ต้องมีการจองห้องที่ยืนยันแล้วซึ่งครอบคลุมวันที่เลือก", null);
        if (await db.ActivityBookings.AnyAsync(b => b.UserId == userId && b.ActivityId == activityId && b.ActDate == d && b.StartTime == time && b.Status == "CONFIRMED"))
            return (false, "คุณจองรอบนี้ไว้แล้ว (เพิ่มจำนวนคนได้โดยยกเลิกแล้วจองใหม่)", null);

        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT activity_id FROM activities WHERE activity_id = {0} FOR UPDATE", activityId);
        var taken = await db.ActivityBookings.Where(b => b.ActivityId == activityId && b.ActDate == d && b.StartTime == time && (b.Status == "CONFIRMED" || b.Status == "ATTENDED"))
            .SumAsync(b => (int?)b.People) ?? 0;
        var left = a.Capacity - taken;
        if (people > left) return (false, left <= 0 ? "รอบนี้เต็มแล้ว ลองรอบอื่น" : $"รอบนี้เหลือที่นั่ง {left} ที่", null);
        var ab = new ActivityBooking
        {
            ActivityId = activityId, UserId = userId, BookingId = stay.BookingId, ActDate = d, StartTime = time,
            People = (byte)people, Total = a.Price * people, Status = "CONFIRMED", CreatedAt = now
        };
        db.ActivityBookings.Add(ab);
        await db.SaveChangesAsync();
        Notify.Add(db, userId, $"จองกิจกรรมแล้ว: {a.Name}",
            $"{d:dd/MM/yyyy} เวลา {time} น. · {people} คน" + (a.Price > 0 ? $" · ค่ากิจกรรม {ab.Total:N0} บาท (รวมในบิลระหว่างพัก ชำระที่เคาน์เตอร์ก่อนเช็คเอาท์)" : " · ฟรีสำหรับผู้เข้าพัก")
            + (a.Location is { Length: > 0 } loc ? $" · จุดนัดพบ {loc}" : ""), "/Activities");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return (true, $"จอง \"{a.Name}\" วันที่ {d:dd/MM/yyyy} เวลา {time} น. แล้ว", ab.AbId);
    }

    public static bool CanCancel(ActivityBooking b, DateTime now) =>
        b.Status == "CONFIRMED" && b.PaidAt == null && b.ActDate.ToDateTime(TimeOnly.ParseExact(b.StartTime, "HH:mm")) > now.AddHours(CancelBeforeHours);

    public static string StatusLabel(string? s) => s switch
    {
        "CONFIRMED" => "จองแล้ว", "CANCELLED" => "ยกเลิก", "ATTENDED" => "เข้าร่วมแล้ว", "NO_SHOW" => "ไม่มาร่วม", _ => s ?? ""
    };

    /// <summary>กิจกรรมตัวอย่าง (ใส่ครั้งแรกเมื่อยังไม่มีกิจกรรมเลย แก้/ปิดได้ที่หน้าผู้จัดการ)</summary>
    public static async Task<int> SeedAsync(Csi402dbContext db)
    {
        if (await db.Activities.AnyAsync()) return 0;
        var now = AppClock.Now;
        var list = new[]
        {
            new Activity { Name = "คลาสทำอาหารไทย", Category = "FOOD", Price = 890, DurationMin = 150, Capacity = 10, Times = "10:00,15:00", Days = "0123456", Location = "ครัวสาธิต ชั้น 1",
                Description = "เรียนทำต้มยำกุ้ง แกงเขียวหวาน และมะม่วงข้าวเหนียวกับเชฟของโรงแรม พร้อมทานอาหารที่ทำเองและสูตรกลับบ้าน" },
            new Activity { Name = "โยคะยามเช้าริมสระ", Category = "WELLNESS", Price = 0, DurationMin = 60, Capacity = 15, Times = "07:00", Days = "0123456", Location = "ลานริมสระว่ายน้ำ",
                Description = "โยคะเบา ๆ เหมาะทุกระดับ มีเสื่อและน้ำดื่มให้ ฟรีสำหรับผู้เข้าพัก" },
            new Activity { Name = "ทัวร์ตลาดน้ำครึ่งวัน", Category = "TOUR", Price = 1200, DurationMin = 300, Capacity = 12, Times = "07:30", Days = "0156", Location = "ล็อบบี้ (รถรับ-ส่ง)",
                Description = "นั่งเรือชมตลาดน้ำ ชิมขนมไทย พร้อมไกด์และรถรับ-ส่งจากโรงแรม (เปิด ศุกร์–จันทร์)" },
            new Activity { Name = "เวิร์กช็อปพวงมาลัยและดอกไม้ไทย", Category = "CULTURE", Price = 450, DurationMin = 90, Capacity = 8, Times = "14:00", Days = "23456", Location = "ห้องกิจกรรม ชั้น 2",
                Description = "ร้อยพวงมาลัยและจับดอกไม้แบบไทยกับครูช่าง ได้ผลงานกลับบ้าน" },
            new Activity { Name = "ปั่นจักรยานชมเมืองเก่า", Category = "TOUR", Price = 650, DurationMin = 180, Capacity = 10, Times = "16:00", Days = "0246", Location = "ล็อบบี้",
                Description = "ปั่นชมย่านเมืองเก่า วัด และร้านอาหารท้องถิ่น พร้อมจักรยาน หมวกกันน็อก และไกด์" },
            new Activity { Name = "ศิลปะเด็กวันหยุด", Category = "KIDS", Price = 250, DurationMin = 90, Capacity = 12, Times = "10:00,14:00", Days = "06", Location = "Kids Corner ชั้น 2",
                Description = "ระบายสีร่มกระดาษและงานประดิษฐ์ไทยสำหรับเด็ก 4–12 ปี มีพี่เลี้ยงดูแล (เสาร์–อาทิตย์)" },
        };
        foreach (var a in list) { a.IsActive = true; a.CreatedAt = now; }
        foreach (var a in list) a.ImageUrl ??= SampleImages.ActivityImage(a.Name);   // Part 62: รูปตั้งต้น
        db.Activities.AddRange(list);
        await db.SaveChangesAsync();
        return list.Length;
    }
}

// ======================================================================
// Part 53: ฝ่ายส่งของ — ของใช้ที่ลูกค้าขอเพิ่ม · สถานะละเอียด · งานด่วน/จับเวลา · สถิติ
// ======================================================================
public static class RoomRequests
{
    public const string TableSql = @"CREATE TABLE IF NOT EXISTS `item_requests` (
    `request_id` int NOT NULL AUTO_INCREMENT,
    `booking_id` int NOT NULL,
    `user_id` int NOT NULL,
    `room_id` int NOT NULL,
    `item` varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `qty` tinyint unsigned NOT NULL DEFAULT 1,
    `note` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `urgent` tinyint(1) NOT NULL DEFAULT 0,
    `status` varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'PENDING',
    `assigned_to` int NULL,
    `missed` tinyint unsigned NOT NULL DEFAULT 0,
    `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `picked_at` datetime NULL,
    `done_at` datetime NULL,
    PRIMARY KEY (`request_id`),
    KEY `ix_ir_status` (`status`, `created_at`),
    KEY `ix_ir_booking` (`booking_id`),
    CONSTRAINT `item_requests_ibfk_1` FOREIGN KEY (`booking_id`) REFERENCES `bookings` (`booking_id`) ON DELETE CASCADE,
    CONSTRAINT `item_requests_ibfk_2` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE CASCADE,
    CONSTRAINT `item_requests_ibfk_3` FOREIGN KEY (`room_id`) REFERENCES `rooms` (`room_id`),
    CONSTRAINT `item_requests_ibfk_4` FOREIGN KEY (`assigned_to`) REFERENCES `users` (`user_id`) ON DELETE SET NULL,
    CONSTRAINT `chk_ir_qty` CHECK (`qty` BETWEEN 1 AND 5)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

    /// <summary>ของที่ขอได้ (ไม่มีค่าใช้จ่าย) — รหัส → ชื่อที่แสดง</summary>
    public static readonly (string Code, string Name)[] Items =
    {
        ("TOWEL", "ผ้าเช็ดตัว"), ("WATER", "น้ำดื่ม"), ("PILLOW", "หมอน"), ("BLANKET", "ผ้าห่ม"),
        ("TOILETRY", "ชุดของใช้ในห้องน้ำ"), ("SLIPPER", "รองเท้าแตะ"), ("COFFEE", "ชา/กาแฟ"), ("OTHER", "อื่น ๆ (ระบุในหมายเหตุ)")
    };
    public const int MaxQty = 5;
    public const int MaxOpenPerBooking = 5;
    /// <summary>รอเกินกี่นาทีถือว่าช้า (แสดงสีแดงในคิวงาน)</summary>
    public const int LateMinutes = 30;

    public static string ItemName(string? code) => Items.FirstOrDefault(i => i.Code == code).Name ?? code ?? "";
    public static bool IsItem(string? code) => Items.Any(i => i.Code == code);

    public static string StatusLabel(string s, byte missed) => s switch
    {
        "PENDING" when missed > 0 => "รอนำส่งใหม่",
        "PENDING" => "รอรับงาน",
        "DELIVERING" => "กำลังนำส่ง",
        "DONE" => "ส่งแล้ว",
        "CANCELLED" => "ยกเลิกแล้ว",
        _ => s
    };

    /// <summary>นาทีที่ผ่านไปนับจากเข้าคิว (ไม่มีเวลาเข้าคิว = null)</summary>
    public static int? Minutes(DateTime? from, DateTime now) => from is { } f ? Math.Max(0, (int)(now - f).TotalMinutes) : null;
}

// ======================================================================
// Part 54: ฝ่ายแม่บ้าน — ลูกค้าขอทำความสะอาด · ห้ามรบกวน · ด่วน/จับเวลา · แจ้งซ่อม · สถิติ
// ======================================================================
public static class HousekeepingOps
{
    public const string IssuesTableSql = @"CREATE TABLE IF NOT EXISTS `room_issues` (
    `issue_id` int NOT NULL AUTO_INCREMENT,
    `room_id` int NOT NULL,
    `reported_by` int NULL,
    `description` varchar(300) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `serious` tinyint(1) NOT NULL DEFAULT 0,
    `status` varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'OPEN',
    `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `fixed_at` datetime NULL,
    PRIMARY KEY (`issue_id`),
    KEY `ix_issue_status` (`status`),
    CONSTRAINT `room_issues_ibfk_1` FOREIGN KEY (`room_id`) REFERENCES `rooms` (`room_id`) ON DELETE CASCADE,
    CONSTRAINT `room_issues_ibfk_2` FOREIGN KEY (`reported_by`) REFERENCES `users` (`user_id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

    /// <summary>ช่วงเวลาที่ลูกค้าเลือกให้เข้าทำความสะอาด</summary>
    public static readonly string[] Slots = { "ตอนนี้เลย", "09:00–12:00", "12:00–15:00", "15:00–18:00" };
    /// <summary>รอเกินกี่นาทีถือว่าช้า</summary>
    public const int LateMinutes = 60;

    public static string KindLabel(string? kind, int? bookingId) => kind switch
    {
        "REQUEST" => "ลูกค้าขอทำความสะอาดระหว่างพัก",
        "CHECKOUT" => $"เช็คเอาท์ของการจอง #{bookingId}",
        _ when bookingId.HasValue => $"เช็คเอาท์ของการจอง #{bookingId}",
        _ => "ทำความสะอาดทั่วไป (แผนกต้อนรับสั่ง)"
    };

    public static string StatusLabel(string s, byte missed) => s switch
    {
        "PENDING" when missed > 0 => "รอเข้าใหม่",
        "PENDING" => "รอรับงาน",
        "CLEANING" => "กำลังทำ",
        "DONE" => "เสร็จแล้ว",
        _ => s
    };
}

// ======================================================================
// Part 60: บิลระหว่างพัก — ค่ากิจกรรม + บริการเสริมที่สั่งเพิ่มระหว่างพัก ชำระที่เคาน์เตอร์ก่อนเช็คเอาท์
// (ยอดจองห้อง + บริการที่จองไว้ล่วงหน้าชำระไปแล้วตอนจอง จึงไม่อยู่ในบิลนี้ · ของใช้ที่ขอเพิ่มฟรี)
// ======================================================================
public static class StayBill
{
    public static readonly (string Table, string Column, string Ddl)[] Columns =
    {
        ("booking_services", "in_stay", "tinyint(1) NOT NULL DEFAULT 0"),
        ("booking_services", "paid_at", "datetime NULL"),
        ("booking_services", "pay_method", "varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL"),
        ("booking_services", "paid_by", "int NULL"),
        ("activity_bookings", "paid_at", "datetime NULL"),
        ("activity_bookings", "pay_method", "varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL"),
        ("activity_bookings", "paid_by", "int NULL"),
        // Part 62: รูปกิจกรรม
        ("activities", "image_url", "varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL"),
    };

    /// <summary>วิธีรับชำระที่เคาน์เตอร์</summary>
    public static readonly (string Code, string Name)[] Methods = { ("CASH", "เงินสด"), ("QR", "โอน / สแกน QR"), ("CARD", "บัตรเครดิต/เดบิต") };
    public static bool IsMethod(string? m) => Methods.Any(x => x.Code == m);
    public static string MethodName(string? m) => Methods.FirstOrDefault(x => x.Code == m).Name ?? m ?? "";
    /// <summary>สั่งบริการเพิ่มระหว่างพักได้ครั้งละไม่เกิน / ค้างชำระได้ไม่เกิน (กันกดซ้ำ/ยอดบานปลาย)</summary>
    public const int MaxQty = 5;
    public const int MaxUnpaidLines = 15;

    /// <summary>คำนำหน้า / หน่วยของรายการในบิล (S = บริการ, A = กิจกรรม, E = ค่าห้องต่อคืน)</summary>
    public static string Prefix(string kind) => kind switch { "A" => "กิจกรรม: ", "S" => "บริการ: ", _ => "" };
    public static string Unit(string kind) => kind switch { "A" => " คน", "E" => " คืน", _ => "" };

    public record Line(string Kind, int Id, string Title, string? RoomNo, DateOnly? Date, int Qty, decimal Amount, DateTime At, DateTime? PaidAt, string? Method, bool CanCancel);

    /// <summary>ค่ากิจกรรมที่ถูกนับ: ยืนยันแล้ว / เข้าร่วมแล้ว (ยกเลิก = ไม่คิด, ไม่มาตามนัด = ไม่คิด)</summary>
    public static async Task<List<Line>> LinesAsync(Csi402dbContext db, int bookingId)
    {
        var svc = await db.BookingServices.AsNoTracking().Where(s => s.BookingId == bookingId && s.InStay)
            .Select(s => new { s.BsId, s.Service.ServiceName, s.Room.RoomNo, s.Quantity, s.UnitPrice, s.PaidAt, s.PayMethod,
                Sent = s.DeliveryTasks.Any(t => t.Status != "PENDING" || t.AssignedTo != null),
                At = s.DeliveryTasks.Select(t => t.CreatedAt).FirstOrDefault() }).ToListAsync();
        var act = await db.ActivityBookings.AsNoTracking()
            .Where(a => a.BookingId == bookingId && (a.Status == "CONFIRMED" || a.Status == "ATTENDED") && a.Total > 0)
            .Select(a => new { a.AbId, a.Activity.Name, a.ActDate, a.StartTime, a.People, a.Total, a.CreatedAt, a.PaidAt, a.PayMethod }).ToListAsync();
        // Part 65: ค่าห้องคืนที่ต่อเพิ่ม
        var ext = await StayChanges.ExtLinesAsync(db, new[] { bookingId });
        var co = await db.Bookings.AsNoTracking().Where(b => b.BookingId == bookingId).Select(b => b.CheckOut).FirstOrDefaultAsync();
        var today = DateOnly.FromDateTime(AppClock.Today);
        return svc.Select(s => new Line("S", s.BsId, s.ServiceName, s.RoomNo, null, s.Quantity, s.UnitPrice * s.Quantity, s.At ?? DateTime.MinValue,
                    s.PaidAt, s.PayMethod, s.PaidAt == null && !s.Sent))
            .Concat(act.Select(a => new Line("A", a.AbId, $"{a.Name} ({a.StartTime} น.)", null, a.ActDate, a.People, a.Total, a.CreatedAt, a.PaidAt, a.PayMethod, false)))
            .Concat(ext.Select(e => new Line("E", e.ExtId, $"ค่าห้องต่อคืน {DateOnly.FromDateTime(e.FromDate):dd/MM} – {DateOnly.FromDateTime(e.FromDate).AddDays(e.Nights):dd/MM}",
                    null, DateOnly.FromDateTime(e.FromDate), e.Nights, e.Amount, e.CreatedAt, e.PaidAt, e.PayMethod,
                    e.PaidAt == null && today < DateOnly.FromDateTime(e.FromDate) && co == DateOnly.FromDateTime(e.FromDate).AddDays(e.Nights))))
            .OrderBy(l => l.PaidAt != null).ThenBy(l => l.At).ToList();
    }

    public static async Task<decimal> UnpaidAsync(Csi402dbContext db, int bookingId) =>
        (await LinesAsync(db, bookingId)).Where(l => l.PaidAt == null).Sum(l => l.Amount);

    /// <summary>ยอดค้างของหลายการจอง (หน้าเช็คอิน/เช็คเอาท์)</summary>
    public static async Task<Dictionary<int, decimal>> UnpaidByBookingAsync(Csi402dbContext db, List<int> ids)
    {
        var s = await db.BookingServices.AsNoTracking().Where(x => ids.Contains(x.BookingId) && x.InStay && x.PaidAt == null)
            .Select(x => new { x.BookingId, Amt = x.UnitPrice * x.Quantity }).ToListAsync();
        var a = await db.ActivityBookings.AsNoTracking().Where(x => x.BookingId != null && ids.Contains(x.BookingId.Value)
                && (x.Status == "CONFIRMED" || x.Status == "ATTENDED") && x.Total > 0 && x.PaidAt == null)
            .Select(x => new { BookingId = x.BookingId!.Value, Amt = x.Total }).ToListAsync();
        var e = (await StayChanges.ExtLinesAsync(db, ids)).Where(x => x.PaidAt == null).Select(x => new { x.BookingId, Amt = x.Amount });
        return s.Concat(a).Concat(e).GroupBy(x => x.BookingId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amt));
    }

    /// <summary>สั่งบริการเสริมเพิ่มระหว่างพัก → บันทึกในบิล + ส่งงานให้ฝ่ายส่งของทันที</summary>
    public static async Task<(bool Ok, string Msg)> AddServiceAsync(Csi402dbContext db, int bookingId, int roomId, int serviceId, int qty, bool urgent)
    {
        var b = await db.Bookings.AsNoTracking().Where(x => x.BookingId == bookingId && x.Status == "CHECKED_IN")
            .Select(x => new { Rooms = x.BookingRooms.Select(r => r.RoomId).ToList() }).FirstOrDefaultAsync();
        if (b == null) return (false, "สั่งบริการเพิ่มได้เฉพาะระหว่างเข้าพัก (หลังเช็คอิน)");
        if (!b.Rooms.Contains(roomId)) return (false, "เลือกห้องของการจองนี้");
        if (qty < 1 || qty > MaxQty) return (false, $"จำนวน 1–{MaxQty}");
        var svc = await db.ExtraServices.AsNoTracking().FirstOrDefaultAsync(s => s.ServiceId == serviceId && s.IsActive != false);
        if (svc == null) return (false, "ไม่พบบริการนี้ หรือปิดให้บริการแล้ว");
        var open = await db.BookingServices.CountAsync(s => s.BookingId == bookingId && s.InStay && s.PaidAt == null);
        if (open >= MaxUnpaidLines) return (false, $"มีรายการรอชำระ {open} รายการแล้ว กรุณาชำระที่เคาน์เตอร์ก่อนสั่งเพิ่ม");

        var bs = new BookingService { BookingId = bookingId, RoomId = roomId, ServiceId = serviceId, Quantity = qty, UnitPrice = svc.Price, InStay = true };
        db.BookingServices.Add(bs);
        await db.SaveChangesAsync();
        db.DeliveryTasks.Add(new DeliveryTask { BsId = bs.BsId, RoomId = roomId, Status = "PENDING", Urgent = urgent, CreatedAt = AppClock.Now });
        await db.SaveChangesAsync();
        return (true, $"สั่ง{svc.ServiceName} × {qty} แล้ว ({svc.Price * qty:N2} บาท ชำระที่เคาน์เตอร์ตอนเช็คเอาท์) พนักงานจะนำไปส่งที่ห้อง");
    }

    /// <summary>ยกเลิกบริการที่สั่งระหว่างพัก ได้เฉพาะที่ยังไม่มีพนักงานรับงานและยังไม่ชำระ</summary>
    public static async Task<bool> CancelServiceAsync(Csi402dbContext db, int bsId, int bookingId)
    {
        var bs = await db.BookingServices.Include(s => s.DeliveryTasks)
            .FirstOrDefaultAsync(s => s.BsId == bsId && s.BookingId == bookingId && s.InStay && s.PaidAt == null);
        if (bs == null || bs.DeliveryTasks.Any(t => t.Status != "PENDING" || t.AssignedTo != null)) return false;
        db.DeliveryTasks.RemoveRange(bs.DeliveryTasks);
        db.BookingServices.Remove(bs);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>รับชำระทุกรายการที่ค้าง (ครั้งเดียว) → คืนยอดที่รับ</summary>
    public static async Task<decimal> SettleAsync(Csi402dbContext db, int bookingId, string method, int staffId)
    {
        var now = AppClock.Now;
        var svc = await db.BookingServices.Where(s => s.BookingId == bookingId && s.InStay && s.PaidAt == null).ToListAsync();
        var act = await db.ActivityBookings.Where(a => a.BookingId == bookingId && (a.Status == "CONFIRMED" || a.Status == "ATTENDED") && a.Total > 0 && a.PaidAt == null).ToListAsync();
        foreach (var s in svc) { s.PaidAt = now; s.PayMethod = method; s.PaidBy = staffId; }
        foreach (var a in act) { a.PaidAt = now; a.PayMethod = method; a.PaidBy = staffId; }
        await db.SaveChangesAsync();
        var extDue = (await StayChanges.ExtLinesAsync(db, new[] { bookingId })).Where(x => x.PaidAt == null).Sum(x => x.Amount);
        await db.Database.ExecuteSqlRawAsync("UPDATE stay_extensions SET paid_at = {0}, pay_method = {1}, paid_by = {2} WHERE booking_id = {3} AND paid_at IS NULL",
            now, method, staffId, bookingId);
        return svc.Sum(s => s.UnitPrice * s.Quantity) + act.Sum(a => a.Total) + extDue;
    }

    /// <summary>
    /// เก็บกวาดให้สอดคล้องกับสถานะการจอง (รันได้ซ้ำ):
    /// การจองถูกยกเลิก / ไม่มาเข้าพัก → ยกเลิกกิจกรรมที่ยังไม่จ่าย (คืนที่นั่ง ไม่คิดเงิน)
    /// เช็คเอาท์แล้ว → ยกเลิกกิจกรรมรอบที่ยังไม่เริ่ม (ออกก่อนกำหนด)
    /// </summary>
    public static async Task<int> SweepAsync(Csi402dbContext db, DateTime now)
    {
        var n = await db.Database.ExecuteSqlRawAsync(@"UPDATE activity_bookings ab JOIN bookings b ON b.booking_id = ab.booking_id
            SET ab.status = 'CANCELLED', ab.cancelled_at = {0}
            WHERE ab.status = 'CONFIRMED' AND ab.paid_at IS NULL AND b.status IN ('CANCELLED', 'NO_SHOW')", now);
        n += await db.Database.ExecuteSqlRawAsync(@"UPDATE activity_bookings ab JOIN bookings b ON b.booking_id = ab.booking_id
            SET ab.status = 'CANCELLED', ab.cancelled_at = {0}
            WHERE ab.status = 'CONFIRMED' AND ab.paid_at IS NULL AND b.status = 'CHECKED_OUT'
              AND TIMESTAMP(ab.act_date, ab.start_time) > {0}", now);
        return n;
    }

    /// <summary>
    /// ก่อนเช็คเอาท์: ยกเลิกสิ่งที่ยังไม่เกิดขึ้น เพื่อไม่ให้คิดเงินสิ่งที่ลูกค้าไม่ได้รับ
    /// กิจกรรมรอบที่ยังไม่เริ่ม · บริการที่สั่งระหว่างพักที่ยังไม่มีพนักงานรับงาน · ของใช้ที่ขอแล้วยังไม่มีคนรับ
    /// </summary>
    public static async Task CloseOpenAsync(Csi402dbContext db, int bookingId, DateTime now)
    {
        var acts = await db.ActivityBookings.Where(a => a.BookingId == bookingId && a.Status == "CONFIRMED" && a.PaidAt == null).ToListAsync();
        foreach (var a in acts.Where(a => a.ActDate.ToDateTime(TimeOnly.ParseExact(a.StartTime, "HH:mm")) > now))
        { a.Status = "CANCELLED"; a.CancelledAt = now; }
        var svc = await db.BookingServices.Include(s => s.DeliveryTasks)
            .Where(s => s.BookingId == bookingId && s.InStay && s.PaidAt == null).ToListAsync();
        foreach (var s in svc.Where(s => !s.DeliveryTasks.Any(t => t.Status != "PENDING" || t.AssignedTo != null)))
        { db.DeliveryTasks.RemoveRange(s.DeliveryTasks); db.BookingServices.Remove(s); }
        await db.ItemRequests.Where(r => r.BookingId == bookingId && r.Status == "PENDING" && r.AssignedTo == null)
            .ExecuteUpdateAsync(x => x.SetProperty(r => r.Status, "CANCELLED"));
        await db.SaveChangesAsync();
    }

    /// <summary>รายรับระหว่างพักที่รับชำระแล้วในช่วงเวลา (รวมในรายงานรายรับ)</summary>
    public static async Task<List<(DateTime PaidAt, decimal Amount)>> PaidInRangeAsync(Csi402dbContext db, DateTime start, DateTime end)
    {
        var s = await db.BookingServices.AsNoTracking().Where(x => x.InStay && x.PaidAt >= start && x.PaidAt < end)
            .Select(x => new { x.PaidAt, Amt = x.UnitPrice * x.Quantity }).ToListAsync();
        var a = await db.ActivityBookings.AsNoTracking().Where(x => x.PaidAt >= start && x.PaidAt < end && x.Total > 0)
            .Select(x => new { x.PaidAt, Amt = x.Total }).ToListAsync();
        var e = await db.Database.SqlQueryRaw<StayChanges.ExtLine>(
            "SELECT ext_id AS ExtId, booking_id AS BookingId, from_date AS FromDate, nights AS Nights, amount AS Amount, created_at AS CreatedAt, paid_at AS PaidAt, pay_method AS PayMethod FROM stay_extensions WHERE paid_at >= {0} AND paid_at < {1}",
            start, end).ToListAsync();
        return s.Select(x => (x.PaidAt!.Value, x.Amt)).Concat(a.Select(x => (x.PaidAt!.Value, x.Amt))).Concat(e.Select(x => (x.PaidAt!.Value, x.Amount))).ToList();
    }
}

/// <summary>
/// Part 65: งานหน้าเคาน์เตอร์ระหว่างพักที่โรงแรมจริงต้องมี
/// · ต่อคืน (ขยายวันเช็คเอาท์) — ตรวจห้องว่างจริง คิดราคาตามเรท/เทศกาลของคืนนั้น ค่าคืนที่เพิ่มเข้าบิลระหว่างพัก (ชำระก่อนเช็คเอาท์)
/// · ย้ายห้อง — ห้องปลายทางต้องว่างตลอดช่วงที่เหลือและพร้อมเข้าพักตอนนี้ ราคาเดิม ห้องเก่าส่งทำความสะอาด งานที่ค้างย้ายตามไป
/// </summary>
public static class StayChanges
{
    public const int MaxExtendNights = 14;

    public const string TableSql = @"CREATE TABLE IF NOT EXISTS `stay_extensions` (
    `ext_id` int NOT NULL AUTO_INCREMENT,
    `booking_id` int NOT NULL,
    `from_date` date NOT NULL,
    `nights` smallint NOT NULL,
    `amount` decimal(10,2) NOT NULL,
    `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `created_by` int NULL,
    `paid_at` datetime NULL,
    `pay_method` varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `paid_by` int NULL,
    PRIMARY KEY (`ext_id`),
    KEY `ix_ext_booking` (`booking_id`),
    KEY `ix_ext_paid` (`paid_at`),
    CONSTRAINT `stay_extensions_ibfk_1` FOREIGN KEY (`booking_id`) REFERENCES `bookings` (`booking_id`) ON DELETE CASCADE,
    CONSTRAINT `stay_extensions_ibfk_2` FOREIGN KEY (`created_by`) REFERENCES `users` (`user_id`) ON DELETE SET NULL,
    CONSTRAINT `stay_extensions_ibfk_3` FOREIGN KEY (`paid_by`) REFERENCES `users` (`user_id`) ON DELETE SET NULL,
    CONSTRAINT `chk_ext_nights` CHECK (`nights` BETWEEN 1 AND 14),
    CONSTRAINT `chk_ext_amount` CHECK (`amount` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

    private static readonly List<string> Active = new() { "PENDING", "CONFIRMED", "CHECKED_IN" };   // List (ไม่ใช่ array): EF แปล Contains เป็น IN ได้

    public record ExtendQuote(bool Ok, string Message, DateOnly NewCheckOut, decimal Amount, List<(string RoomNo, decimal Amount)> Rooms);

    /// <summary>ตรวจว่าต่อคืนได้ไหม + ราคา (ไม่บันทึก)</summary>
    public static async Task<ExtendQuote> QuoteAsync(Csi402dbContext db, int bookingId, int nights)
    {
        var b = await db.Bookings.AsNoTracking().Where(x => x.BookingId == bookingId)
            .Select(x => new { x.Status, x.CheckOut, Rooms = x.BookingRooms.Select(r => new { r.RoomId, r.Room.RoomNo, r.Room.Status, r.Room.Type.BasePrice, r.Room.Type.WeekendPrice }).ToList() })
            .FirstOrDefaultAsync();
        var none = new List<(string, decimal)>();
        if (b == null) return new(false, "ไม่พบการจอง", default, 0, none);
        if (b.Status != "CHECKED_IN") return new(false, "ต่อคืนได้เฉพาะการจองที่เช็คอินแล้ว", b.CheckOut, 0, none);
        if (nights < 1 || nights > MaxExtendNights) return new(false, $"ต่อได้ครั้งละ 1–{MaxExtendNights} คืน", b.CheckOut, 0, none);
        var from = b.CheckOut; var to = from.AddDays(nights);
        var ids = b.Rooms.Select(r => r.RoomId).ToList();
        var clash = await db.BookingRooms.AsNoTracking()
            .Where(br => ids.Contains(br.RoomId) && br.BookingId != bookingId && Active.Contains(br.Booking.Status)
                         && br.Booking.CheckIn < to && br.Booking.CheckOut > from)
            .Select(br => new { br.Room.RoomNo, br.Booking.CheckIn }).ToListAsync();
        if (clash.Count > 0)
            return new(false, $"ต่อคืนไม่ได้: ห้อง {string.Join(", ", clash.Select(c => c.RoomNo).Distinct())} มีผู้จองต่อตั้งแต่ {clash.Min(c => c.CheckIn):dd/MM/yyyy} " +
                "(ย้ายไปห้องอื่นที่ว่างก่อน แล้วค่อยต่อคืน)", from, 0, none);
        var seasons = await RatePlan.SeasonsAsync(db, from, to);
        var per = b.Rooms.OrderBy(r => r.RoomNo).Select(r => (r.RoomNo,
            Enumerable.Range(0, nights).Sum(i => RatePlan.NightPrice(r.BasePrice, r.WeekendPrice, from.AddDays(i), seasons)))).ToList();
        return new(true, "", to, per.Sum(p => p.Item2), per);
    }

    /// <summary>ต่อคืนจริง: ทำในทรานแซกชัน (กันจองชนกันระหว่างตรวจกับบันทึก)</summary>
    public static async Task<(bool Ok, string Msg)> ExtendAsync(Csi402dbContext db, int bookingId, int nights, int staffId)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await db.Database.ExecuteSqlRawAsync("SELECT booking_id FROM bookings WHERE booking_id = {0} FOR UPDATE", bookingId);
        var q = await QuoteAsync(db, bookingId, nights);
        if (!q.Ok) return (false, q.Message);
        var b = await db.Bookings.Include(x => x.BookingRooms).FirstAsync(x => x.BookingId == bookingId);
        var from = b.CheckOut;
        b.CheckOut = q.NewCheckOut;
        foreach (var br in b.BookingRooms) br.Nights = (short)(br.Nights + nights);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO stay_extensions (booking_id, from_date, nights, amount, created_at, created_by) VALUES ({0}, {1}, {2}, {3}, {4}, {5})",
            bookingId, from.ToDateTime(TimeOnly.MinValue), nights, q.Amount, AppClock.Now, staffId);
        Notify.Add(db, b.UserId, $"ต่อการเข้าพักแล้ว · การจอง #{bookingId}",
            $"เช็คเอาท์ใหม่ {q.NewCheckOut:dd/MM/yyyy} (+{nights} คืน) ค่าห้องเพิ่ม {q.Amount:N2} บาท ชำระที่เคาน์เตอร์ก่อนเช็คเอาท์", Notify.BookingLink(bookingId));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return (true, $"ต่อการจอง #{bookingId} อีก {nights} คืน เช็คเอาท์ใหม่ {q.NewCheckOut:dd/MM/yyyy} · ค่าห้องเพิ่ม {q.Amount:N2} บาท (เข้าบิลระหว่างพัก)");
    }

    /// <summary>ยกเลิกการต่อคืน (ยังไม่ชำระ และยังไม่ถึงคืนแรกที่ต่อ) — คืนวันเช็คเอาท์เดิม ห้องกลับมาว่างให้คนอื่นจอง</summary>
    public static async Task<(bool Ok, string Msg)> CancelExtensionAsync(Csi402dbContext db, int bookingId, int extId)
    {
        var ext = await db.Database.SqlQueryRaw<ExtRow>(
            "SELECT ext_id AS ExtId, from_date AS FromDate, nights AS Nights, amount AS Amount, paid_at AS PaidAt FROM stay_extensions WHERE ext_id = {0} AND booking_id = {1}",
            extId, bookingId).FirstOrDefaultAsync();
        var b = await db.Bookings.Include(x => x.BookingRooms).FirstOrDefaultAsync(x => x.BookingId == bookingId);
        if (ext == null || b == null || b.Status != "CHECKED_IN") return (false, "ไม่พบรายการต่อคืน");
        var fromD = DateOnly.FromDateTime(ext.FromDate);
        if (ext.PaidAt != null) return (false, "ชำระแล้ว ยกเลิกไม่ได้");
        if (DateOnly.FromDateTime(AppClock.Today) >= fromD) return (false, "เริ่มพักคืนที่ต่อแล้ว ยกเลิกไม่ได้");
        if (b.CheckOut != fromD.AddDays(ext.Nights)) return (false, "ยกเลิกได้เฉพาะการต่อคืนครั้งล่าสุด");
        b.CheckOut = fromD;
        foreach (var br in b.BookingRooms) br.Nights = (short)Math.Max(1, br.Nights - ext.Nights);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM stay_extensions WHERE ext_id = {0}", extId);
        return (true, $"ยกเลิกการต่อคืนแล้ว เช็คเอาท์ {fromD:dd/MM/yyyy} ตามเดิม");
    }

    public class ExtRow { public int ExtId { get; set; } public DateTime FromDate { get; set; } public short Nights { get; set; } public decimal Amount { get; set; } public DateTime? PaidAt { get; set; } }
    public class ExtLine { public int ExtId { get; set; } public int BookingId { get; set; } public DateTime FromDate { get; set; } public short Nights { get; set; } public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; } public DateTime? PaidAt { get; set; } public string? PayMethod { get; set; } }

    public static Task<List<ExtLine>> ExtLinesAsync(Csi402dbContext db, IEnumerable<int> bookingIds)
    {
        var ids = bookingIds.Distinct().ToList();
        if (ids.Count == 0) return Task.FromResult(new List<ExtLine>());
        return db.Database.SqlQueryRaw<ExtLine>(
            $"SELECT ext_id AS ExtId, booking_id AS BookingId, from_date AS FromDate, nights AS Nights, amount AS Amount, created_at AS CreatedAt, paid_at AS PaidAt, pay_method AS PayMethod FROM stay_extensions WHERE booking_id IN ({string.Join(",", ids.Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)))})")
            .ToListAsync();
    }

    /// <summary>ห้องที่ย้ายไปได้: ไม่ใช่ห้องของการจองนี้ · พร้อมเข้าพักตอนนี้ (ว่าง) · ไม่มีใครจองทับตั้งแต่วันนี้ถึงวันเช็คเอาท์</summary>
    public static async Task<List<(int RoomId, string RoomNo, string Type, int MaxGuests)>> MoveTargetsAsync(Csi402dbContext db, int bookingId)
    {
        var b = await db.Bookings.AsNoTracking().Where(x => x.BookingId == bookingId).Select(x => new { x.CheckOut, Rooms = x.BookingRooms.Select(r => r.RoomId).ToList() }).FirstOrDefaultAsync();
        if (b == null) return new();
        var today = DateOnly.FromDateTime(AppClock.Today);
        var busy = db.BookingRooms.Where(br => br.BookingId != bookingId && Active.Contains(br.Booking.Status) && br.Booking.CheckIn < b.CheckOut && br.Booking.CheckOut > today).Select(br => br.RoomId);
        var list = await db.Rooms.AsNoTracking().Where(r => r.Status == "AVAILABLE" && !b.Rooms.Contains(r.RoomId) && !busy.Contains(r.RoomId))
            .OrderBy(r => r.RoomNo).Select(r => new { r.RoomId, r.RoomNo, r.Type.TypeName, r.Type.MaxGuests }).ToListAsync();
        return list.Select(r => (r.RoomId, r.RoomNo, r.TypeName, (int)r.MaxGuests)).ToList();
    }

    public static async Task<(bool Ok, string Msg)> MoveAsync(Csi402dbContext db, int bookingId, int fromRoomId, int toRoomId, string? reason)
    {
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reason?.Length > 150) return (false, "เหตุผลไม่เกิน 150 ตัวอักษร");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await db.Database.ExecuteSqlRawAsync("SELECT room_id FROM rooms WHERE room_id IN ({0}, {1}) FOR UPDATE", fromRoomId, toRoomId);
        var b = await db.Bookings.Include(x => x.BookingRooms).ThenInclude(r => r.Room).FirstOrDefaultAsync(x => x.BookingId == bookingId);
        if (b == null || b.Status != "CHECKED_IN") return (false, "ย้ายห้องได้เฉพาะการจองที่เช็คอินแล้ว");
        var br = b.BookingRooms.FirstOrDefault(r => r.RoomId == fromRoomId);
        if (br == null) return (false, "ห้องเดิมไม่ใช่ห้องของการจองนี้");
        if (!(await MoveTargetsAsync(db, bookingId)).Any(t => t.RoomId == toRoomId))
            return (false, "ห้องปลายทางไม่ว่างตลอดช่วงที่เหลือ หรือยังไม่พร้อมเข้าพัก");
        var oldRoom = br.Room;
        var newRoom = await db.Rooms.FirstAsync(r => r.RoomId == toRoomId);
        br.RoomId = toRoomId;                  // ราคาต่อคืนคงเดิม (PricePerNight ไม่เปลี่ยน)
        newRoom.Status = "OCCUPIED";
        oldRoom.Status = "CLEANING";
        db.HousekeepingTasks.Add(new HousekeepingTask { RoomId = fromRoomId, BookingId = bookingId, Status = "PENDING", Kind = "CHECKOUT", CreatedAt = AppClock.Now,
            Note = $"ย้ายผู้เข้าพักไปห้อง {newRoom.RoomNo}" });
        await db.SaveChangesAsync();
        // งานที่ยังไม่เสร็จของห้องเดิม (ส่งของ / ขอของใช้ / ขอทำความสะอาด / บริการระหว่างพัก) ย้ายตามไปห้องใหม่
        await db.DeliveryTasks.Where(t => t.RoomId == fromRoomId && t.Status != "DONE" && t.Bs.BookingId == bookingId)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.RoomId, toRoomId));
        await db.ItemRequests.Where(r => r.BookingId == bookingId && r.RoomId == fromRoomId && (r.Status == "PENDING" || r.Status == "DELIVERING"))
            .ExecuteUpdateAsync(x => x.SetProperty(r => r.RoomId, toRoomId));
        await db.HousekeepingTasks.Where(t => t.BookingId == bookingId && t.RoomId == fromRoomId && t.Kind == "REQUEST" && t.Status != "DONE")
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.RoomId, toRoomId));
        await db.BookingServices.Where(s => s.BookingId == bookingId && s.RoomId == fromRoomId && s.PaidAt == null && s.InStay)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.RoomId, toRoomId));
        Notify.Add(db, b.UserId, $"ย้ายห้องแล้ว · การจอง #{bookingId}",
            $"ย้ายจากห้อง {oldRoom.RoomNo} ไปห้อง {newRoom.RoomNo} ราคาเดิม" + (reason != null ? $" ({reason})" : ""), Notify.BookingLink(bookingId));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return (true, $"ย้ายการจอง #{bookingId} จากห้อง {oldRoom.RoomNo} ไปห้อง {newRoom.RoomNo} แล้ว (ราคาเดิม) ห้อง {oldRoom.RoomNo} ส่งทำความสะอาดแล้ว");
    }
}


/// <summary>Part 66: คำขอต่อคืนจากผู้เข้าพัก + ของลืมในห้อง</summary>
public static class GuestServices
{
    public const string StayRequestsTableSql = @"CREATE TABLE IF NOT EXISTS `stay_requests` (
    `request_id` int NOT NULL AUTO_INCREMENT,
    `booking_id` int NOT NULL,
    `user_id` int NOT NULL,
    `nights` smallint NOT NULL,
    `quoted_amount` decimal(10,2) NOT NULL DEFAULT 0,
    `note` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `status` varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'PENDING',
    `reply` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `handled_by` int NULL,
    `handled_at` datetime NULL,
    PRIMARY KEY (`request_id`),
    KEY `ix_sr_status` (`status`, `created_at`),
    KEY `ix_sr_booking` (`booking_id`),
    CONSTRAINT `stay_requests_ibfk_1` FOREIGN KEY (`booking_id`) REFERENCES `bookings` (`booking_id`) ON DELETE CASCADE,
    CONSTRAINT `stay_requests_ibfk_2` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE CASCADE,
    CONSTRAINT `stay_requests_ibfk_3` FOREIGN KEY (`handled_by`) REFERENCES `users` (`user_id`) ON DELETE SET NULL,
    CONSTRAINT `chk_sr_nights` CHECK (`nights` BETWEEN 1 AND 14),
    CONSTRAINT `chk_sr_status` CHECK (`status` IN ('PENDING','APPROVED','REJECTED','CANCELLED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

    public const string LostItemsTableSql = @"CREATE TABLE IF NOT EXISTS `lost_items` (
    `item_id` int NOT NULL AUTO_INCREMENT,
    `room_id` int NOT NULL,
    `booking_id` int NULL,
    `description` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `status` varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'STORED',
    `claim_note` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `found_by` int NULL,
    `found_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `claimed_at` datetime NULL,
    `closed_by` int NULL,
    `closed_at` datetime NULL,
    PRIMARY KEY (`item_id`),
    KEY `ix_li_status` (`status`, `found_at`),
    KEY `ix_li_booking` (`booking_id`),
    KEY `ix_li_room` (`room_id`),
    CONSTRAINT `lost_items_ibfk_1` FOREIGN KEY (`room_id`) REFERENCES `rooms` (`room_id`),
    CONSTRAINT `lost_items_ibfk_2` FOREIGN KEY (`booking_id`) REFERENCES `bookings` (`booking_id`) ON DELETE SET NULL,
    CONSTRAINT `lost_items_ibfk_3` FOREIGN KEY (`found_by`) REFERENCES `users` (`user_id`) ON DELETE SET NULL,
    CONSTRAINT `lost_items_ibfk_4` FOREIGN KEY (`closed_by`) REFERENCES `users` (`user_id`) ON DELETE SET NULL,
    CONSTRAINT `chk_li_status` CHECK (`status` IN ('STORED','CLAIMED','RETURNED','DISPOSED'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

    /// <summary>เก็บของลืมไว้กี่วันก่อนจำหน่าย (ทิ้ง/บริจาค) ได้</summary>
    public const int KeepDays = 30;

    public static string RequestStatus(string s) => s switch
    { "PENDING" => "รอเคาน์เตอร์ตอบ", "APPROVED" => "อนุมัติแล้ว", "REJECTED" => "ไม่อนุมัติ", "CANCELLED" => "ยกเลิกแล้ว", _ => s };

    public static string LostStatus(string s) => s switch
    { "STORED" => "เก็บไว้ที่โรงแรม", "CLAIMED" => "ลูกค้าแจ้งรับคืน", "RETURNED" => "ส่งคืนแล้ว", "DISPOSED" => "จำหน่ายแล้ว", _ => s };

    /// <summary>การจองที่น่าจะเป็นเจ้าของของ: ผู้เข้าพักที่อยู่ / เพิ่งออกจากห้องนี้ (ภายใน 3 วัน)</summary>
    public static async Task<(int BookingId, int UserId)?> OwnerOfAsync(Csi402dbContext db, int roomId, DateOnly today)
    {
        var b = await db.BookingRooms.AsNoTracking()
            .Where(br => br.RoomId == roomId && (br.Booking.Status == "CHECKED_IN" || (br.Booking.Status == "CHECKED_OUT" && br.Booking.CheckOut >= today.AddDays(-3))))
            .OrderByDescending(br => br.Booking.Status == "CHECKED_IN" && br.Booking.CheckIn <= today)
            .ThenByDescending(br => br.Booking.Status == "CHECKED_IN").ThenByDescending(br => br.Booking.CheckOut)
            .Select(br => new { br.BookingId, br.Booking.UserId }).FirstOrDefaultAsync();
        return b == null ? null : (b.BookingId, b.UserId);
    }
}
