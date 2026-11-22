using System.Linq.Expressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using pameka_7033_l005.Helpers;
using pameka_7033_l005.Models;
using pameka_7033_l005.ViewModels;

namespace pameka_7033_l005.Controllers;

// พนักงาน: ฝ่ายบัญชี, ฝ่ายบริการ (เช็คอิน/เช็คเอาท์/จองหน้าฟรอนต์), ส่งของ, ทำความสะอาด

// ======================================================================
// AccountingController
// ======================================================================
/// <summary>ฝ่ายบัญชี: ตรวจสอบการชำระเงินของลูกค้า, อนุมัติคืนเงิน, ดูประวัติ (เฉพาะบทบาท accounting)</summary>
[Authorize(Roles = RoleNames.Accounting)]
public class AccountingController : AppController
{
    private readonly Csi402dbContext _db;
    public AccountingController(Csi402dbContext db) => _db = db;

    [HttpGet]
    public Task<IActionResult> Index() => List("verify", "ตรวจสอบการชำระเงิน",
        "ลูกค้าแจ้งชำระเงินแล้ว ตรวจสอบยอดเข้าบัญชีแล้วกด \"ยืนยัน\" การจองจะเปลี่ยนเป็น \"จองสำเร็จ\"",
        p => p.PaymentType == BookingWorkflow.PaymentType && p.Status == "PENDING");

    [HttpGet]
    public Task<IActionResult> Refunds() => List("refund", "คำขอคืนเงิน",
        "ลูกค้าขอยกเลิกการจองที่ชำระแล้ว อนุมัติแล้วโอนคืนเต็มจำนวน การจองจะถูกยกเลิก",
        p => p.PaymentType == BookingWorkflow.RefundType && p.Status == "PENDING");

    [HttpGet]
    public Task<IActionResult> History() => List("history", "ประวัติการเงิน",
        "รายการที่ดำเนินการแล้ว (200 รายการล่าสุด)", p => p.Status != "PENDING");

    // ---------------------------------------------------------------- ดำเนินการ
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(int id)
    {
        var p = await _db.Payments.Include(x => x.Booking)
            .FirstOrDefaultAsync(x => x.PaymentId == id && x.PaymentType == BookingWorkflow.PaymentType);
        if (p == null || !BookingWorkflow.CanConfirmPayment(p.Booking.Status, p.Status))
            return Stale(nameof(Index));
        if (p.Booking.CheckOut <= DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today))
        {
            Flash($"ยืนยันไม่ได้: การจอง #{p.BookingId} เลยวันออกแล้ว ({p.Booking.CheckOut:dd/MM/yyyy}) — ตรวจสอบกับลูกค้า แล้วปฏิเสธหลักฐานนี้หรือคืนเงินนอกระบบ", true);
            return RedirectToAction(nameof(Index));
        }

        p.Status = "PAID";
        p.PaidAt = pameka_7033_l005.Helpers.AppClock.Now;
        p.HandledBy = CurrentUserId;
        p.Booking.Status = "CONFIRMED";
        p.Booking.ConfirmedBy = CurrentUserId;
        Notify.Add(_db, p.Booking.UserId, $"ยืนยันการชำระเงินแล้ว · การจอง #{p.BookingId}",
            $"ได้รับยอด {p.Amount:N2} บาท การจองของคุณได้รับการยืนยันแล้ว เข้าพัก {p.Booking.CheckIn:dd/MM/yyyy}", Notify.BookingLink(p.BookingId));
        await _db.SaveChangesAsync();
        Flash($"ยืนยันการชำระเงินของการจอง #{p.BookingId} แล้ว");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>ปฏิเสธการแจ้งชำระเงิน (ยอดไม่เข้า/ไม่ตรง): ลบรายการแจ้ง ลูกค้าแจ้งใหม่ได้ การจองยังรอดำเนินการ</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id)
    {
        var p = await _db.Payments.Include(x => x.Booking)
            .FirstOrDefaultAsync(x => x.PaymentId == id && x.PaymentType == BookingWorkflow.PaymentType);
        if (p == null || !BookingWorkflow.CanConfirmPayment(p.Booking.Status, p.Status))
            return Stale(nameof(Index));

        // เก็บไว้เป็นประวัติพร้อมสลิป (ไม่ลบ) ลูกค้าได้เวลาแนบใหม่อีกรอบนับจากตอนนี้
        p.Status = "REJECTED";
        p.HandledBy = CurrentUserId;
        p.PaidAt = pameka_7033_l005.Helpers.AppClock.Now;
        Notify.Add(_db, p.Booking.UserId, $"หลักฐานการชำระเงินไม่ผ่าน · การจอง #{p.BookingId}",
            $"กรุณาตรวจสอบยอดและแนบหลักฐานใหม่ภายใน {BookingExpiry.HoldHours} ชั่วโมง มิฉะนั้นการจองจะถูกยกเลิกอัตโนมัติ", $"/Booking/Pay/{p.BookingId}");
        await _db.SaveChangesAsync();
        Flash($"ปฏิเสธการแจ้งชำระเงินของการจอง #{p.BookingId} แล้ว ลูกค้าแนบสลิปใหม่ได้ภายใน {BookingExpiry.HoldHours} ชม.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveRefund(int id)
    {
        var r = await _db.Payments.Include(x => x.Booking)
            .FirstOrDefaultAsync(x => x.PaymentId == id && x.PaymentType == BookingWorkflow.RefundType);
        if (r == null || !BookingWorkflow.CanApproveRefund(r.Booking.Status, r.Status))
            return Stale(nameof(Refunds));

        var original = await _db.Payments.FirstOrDefaultAsync(x =>
            x.BookingId == r.BookingId && x.PaymentType == BookingWorkflow.PaymentType && x.Status == "PAID");
        if (original == null) return Stale(nameof(Refunds));

        r.Status = "REFUNDED";
        r.PaidAt = pameka_7033_l005.Helpers.AppClock.Now;
        r.HandledBy = CurrentUserId;
        original.Status = "REFUNDED";
        var noShow = r.Booking.Status == "NO_SHOW";
        if (!noShow) r.Booking.Status = "CANCELLED";   // ไม่มาเข้าพัก: คงสถานะเดิมไว้เป็นประวัติ
        Notify.Add(_db, r.Booking.UserId, $"อนุมัติคืนเงินแล้ว · การจอง #{r.BookingId}",
            noShow ? $"คืนเงิน {r.Amount:N2} บาท" : $"คืนเงิน {r.Amount:N2} บาท การจองนี้ถูกยกเลิกแล้ว", Notify.BookingLink(r.BookingId));
        await _db.SaveChangesAsync();
        if (!noShow) await Waitlist.CheckAsync(_db, DateOnly.FromDateTime(AppClock.Today));   // Part 51
        Flash($"อนุมัติคืนเงิน {r.Amount:N2} บาท ของการจอง #{r.BookingId} แล้ว" + (noShow ? "" : " (การจองถูกยกเลิก)"));
        return RedirectToAction(nameof(Refunds));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectRefund(int id)
    {
        var r = await _db.Payments.Include(x => x.Booking)
            .FirstOrDefaultAsync(x => x.PaymentId == id && x.PaymentType == BookingWorkflow.RefundType);
        if (r == null || !BookingWorkflow.CanApproveRefund(r.Booking.Status, r.Status))
            return Stale(nameof(Refunds));

        _db.Payments.Remove(r);
        Notify.Add(_db, r.Booking.UserId, $"คำขอคืนเงินไม่ได้รับอนุมัติ · การจอง #{r.BookingId}",
            "การจองของคุณยังคงอยู่ตามเดิม หากมีข้อสงสัยกรุณาติดต่อโรงแรม", Notify.BookingLink(r.BookingId));
        await _db.SaveChangesAsync();
        Flash($"ปฏิเสธคำขอคืนเงินของการจอง #{r.BookingId} แล้ว การจองยังคงอยู่");
        return RedirectToAction(nameof(Refunds));
    }

    // ---------------------------------------------------------------- รายจ่าย / กำไรรายเดือน
    [HttpGet]
    public async Task<IActionResult> Expenses(int? year, int? month)
    {
        var first = new DateOnly(year is >= 2000 and <= 2100 ? year.Value : pameka_7033_l005.Helpers.AppClock.Today.Year, month is >= 1 and <= 12 ? month.Value : pameka_7033_l005.Helpers.AppClock.Today.Month, 1);
        var next = first.AddMonths(1);
        var (start, end) = (first.ToDateTime(TimeOnly.MinValue), next.ToDateTime(TimeOnly.MinValue));

        var rows = await _db.Expenses.AsNoTracking().Where(e => e.SpentOn >= first && e.SpentOn < next)
            .OrderByDescending(e => e.SpentOn).ThenByDescending(e => e.ExpenseId)
            .Select(e => new { e.ExpenseId, e.SpentOn, e.Category, e.Amount, e.Note, By = e.CreatedByNavigation != null ? e.CreatedByNavigation.FirstName : null })
            .ToListAsync();
        // รายรับนับแบบเดียวกับรายงานผู้จัดการ: ชำระแล้วและยังไม่คืน ตามวันที่ยืนยันรับเงิน
        var revenue = (await _db.Payments.AsNoTracking()
            .Where(p => p.PaymentType == BookingWorkflow.PaymentType && p.Status == "PAID" && p.PaidAt >= start && p.PaidAt < end)
            .Select(p => p.Amount).ToListAsync()).Sum();

        return View(new ExpensePageViewModel
        {
            Year = first.Year, Month = first.Month, Revenue = revenue, Total = rows.Sum(r => r.Amount),
            ByCategory = rows.GroupBy(r => r.Category).Select(g => (g.Key, g.Sum(x => x.Amount))).OrderByDescending(x => x.Item2).ToList(),
            Rows = rows.Select(r => (r.ExpenseId, r.SpentOn, r.Category, r.Amount, r.Note, r.By)).ToList()
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddExpense(ExpenseFormViewModel f)
    {
        if (!ExpenseFormViewModel.Categories.Contains(f.Category)) ModelState.AddModelError(nameof(f.Category), "หมวดไม่ถูกต้อง");
        if (f.SpentOn > DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today)) ModelState.AddModelError(nameof(f.SpentOn), "วันที่จ่ายต้องไม่เกินวันนี้");
        if (!ModelState.IsValid)
        {
            Flash(string.Join(" / ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Distinct()), true);
            return RedirectToAction(nameof(Expenses));
        }
        _db.Expenses.Add(new Expense { Category = f.Category, Amount = Math.Round(f.Amount, 2), SpentOn = f.SpentOn!.Value,
            Note = string.IsNullOrWhiteSpace(f.Note) ? null : f.Note.Trim(), CreatedBy = CurrentUserId, CreatedAt = pameka_7033_l005.Helpers.AppClock.Now });
        await _db.SaveChangesAsync();
        Flash($"บันทึกรายจ่าย {f.Category} {f.Amount:N2} บาท แล้ว");
        return RedirectToAction(nameof(Expenses), new { year = f.SpentOn!.Value.Year, month = f.SpentOn!.Value.Month });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteExpense(int id)
    {
        var e = await _db.Expenses.FindAsync(id);
        if (e == null) return Stale(nameof(Expenses));
        _db.Expenses.Remove(e);
        await _db.SaveChangesAsync();
        Flash($"ลบรายจ่าย {e.Category} {e.Amount:N2} บาท แล้ว");
        return RedirectToAction(nameof(Expenses), new { year = e.SpentOn.Year, month = e.SpentOn.Month });
    }

    // ---------------------------------------------------------------- ตัวช่วย
    private async Task<IActionResult> List(string mode, string title, string description, Expression<Func<Payment, bool>> filter)
    {
        var rows = await _db.Payments.AsNoTracking().Where(filter)
            .OrderByDescending(p => p.PaymentId).Take(200)
            .Select(p => new
            {
                p.PaymentId, p.BookingId, p.Booking.CheckIn, p.Booking.CheckOut,
                Customer = p.Booking.User.FirstName + " " + p.Booking.User.LastName,
                Phone = p.Booking.User.Phone,
                p.Amount, p.PaymentType, p.Method, p.Status, p.PaidAt, HasSlip = p.SlipPath != null, SlipPdf = p.SlipPath != null && p.SlipPath.EndsWith(".pdf"), p.Reference,
                Handler = p.HandledByNavigation != null ? p.HandledByNavigation.FirstName : null,
                Rooms = p.Booking.BookingRooms.Select(r => r.Room.RoomNo).ToList()
            }).ToListAsync();

        return View("Payments", new PaymentListViewModel
        {
            Mode = mode, Title = title, Description = description,
            Rows = rows.Select(r => new PaymentRowViewModel
            {
                PaymentId = r.PaymentId, BookingId = r.BookingId, Customer = r.Customer, Phone = r.Phone,
                Rooms = string.Join(", ", r.Rooms), CheckIn = r.CheckIn, CheckOut = r.CheckOut,
                Amount = r.Amount, Type = r.PaymentType, Method = r.Method, Status = r.Status,
                PaidAt = r.PaidAt, Handler = r.Handler, HasSlip = r.HasSlip, SlipIsPdf = r.SlipPdf, Reference = r.Reference
            }).ToList()
        });
    }
}

// ======================================================================
// FrontDeskController
// ======================================================================
/// <summary>ฝ่ายบริการ (หน้าฟรอนต์): เช็คอิน/เช็คเอาท์, ส่งงานให้ฝ่ายส่งของ/ทำความสะอาด, จัดการสถานะห้อง</summary>
/// ผู้จัดการใช้ได้เฉพาะ "จองห้องให้ลูกค้า" (WalkIn/Preview/Confirm/Availability) งานเช็คอิน-เอาท์ยังเป็นของฝ่ายบริการเท่านั้น
[Authorize(Roles = RoleNames.FrontDesk + "," + RoleNames.Admin)]
public class FrontDeskController : AppController
{
    private readonly Csi402dbContext _db;
    private readonly BookingBuilder _builder;
    public FrontDeskController(Csi402dbContext db) { _db = db; _builder = new BookingBuilder(db); }

    private static DateOnly Today => DateOnly.FromDateTime(pameka_7033_l005.Helpers.AppClock.Today);

    // ---------------------------------------------------------------- จองให้ลูกค้าที่เคาน์เตอร์ (ใช้ฟอร์มเดียวกับลูกค้า)
    private const string BookingForm = "~/Views/Booking/Create.cshtml";

    /// <summary>Part 48: ค้นหาลูกค้า (ชื่อ / อีเมล / เบอร์) สำหรับช่องเลือกลูกค้าแบบพิมพ์ค้นหา (Tom Select)
    /// เฉพาะบัญชีบทบาทลูกค้า · ไม่เกิน 15 รายการ · ส่งเฉพาะชื่อ อีเมล เบอร์ (ไม่มีข้อมูลอื่น)</summary>
    [HttpGet]
    public async Task<IActionResult> CustomerLookup(string? q)
    {
        q = (q ?? "").Trim();
        if (q.Length < 2 || q.Length > 100) return Json(Array.Empty<object>());
        // ค้นด้วยเบอร์เฉพาะเมื่อพิมพ์เป็นตัวเลข (อนุญาตช่องว่าง - + ) ไม่ดึงตัวเลขจากอีเมล/ชื่อมาค้นเบอร์
        var digits = q.All(c => char.IsDigit(c) || c is ' ' or '-' or '+' or '(' or ')') ? new string(q.Where(char.IsDigit).ToArray()) : "";
        var list = await _db.Users.AsNoTracking()
            .Where(u => u.Role.RoleName == RoleNames.Customer
                && (u.Email.Contains(q) || u.FirstName.Contains(q) || u.LastName.Contains(q) || (u.FirstName + " " + u.LastName).Contains(q)
                    || (digits.Length >= 3 && u.Phone.Contains(digits))))
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName).Take(15)
            .Select(u => new { email = u.Email, name = u.FirstName + " " + u.LastName, phone = u.Phone })
            .ToListAsync();
        Response.Headers.CacheControl = "no-store";
        return Json(list);
    }

    [HttpGet]
    public async Task<IActionResult> WalkIn(DateOnly? checkIn, DateOnly? checkOut, List<int>? roomIds, int? guests)
    {
        var ci = checkIn ?? Today;
        var vm = new BookingFormViewModel { CheckIn = ci, CheckOut = checkOut ?? ci.AddDays(1), RoomIds = roomIds ?? new List<int>(), Staff = true,
            Guests = guests is >= 1 and <= BookingRules.MaxGuests ? guests : 1 };
        await _builder.FillListsAsync(vm);
        return View(BookingForm, vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(BookingFormViewModel vm)
    {
        vm.Staff = true;
        await _builder.FillListsAsync(vm);
        if (vm.Error == null && vm.RoomIds.Count > 0)
        {
            var customerId = await FindCustomerAsync(vm);
            if (customerId != null) await _builder.QuoteAsync(vm, customerId.Value);   // 0 = ลูกค้าใหม่ (ยังไม่สร้างบัญชีจนกว่าจะยืนยัน)
        }
        return View(BookingForm, vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(BookingFormViewModel vm)
    {
        vm.Staff = true;
        var customerId = await FindCustomerAsync(vm);
        if (customerId == null) { await _builder.FillListsAsync(vm); return View(BookingForm, vm); }

        // ลูกค้าใหม่: สร้างบัญชีก่อน (การจองต้องผูกกับบัญชี) ถ้าจองไม่สำเร็จจะลบบัญชีที่เพิ่งสร้างออก
        User? created = null;
        string? tempPassword = null;
        if (customerId == 0)
        {
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName == RoleNames.Customer);
            if (role == null) { vm.Error = "ไม่พบบทบาทลูกค้าในฐานข้อมูล"; await _builder.FillListsAsync(vm); return View(BookingForm, vm); }
            tempPassword = PasswordHelper.Generate();
            created = new User
            {
                FirstName = vm.NewFirstName!.Trim(), LastName = vm.NewLastName!.Trim(), Phone = vm.NewPhone!.Trim(),
                Email = NormalizeEmail(vm.CustomerEmail), PasswordHash = PasswordHelper.Hash(tempPassword), SecurityStamp = Guid.NewGuid().ToString("N"),
                Role = role, IsMember = true, CreatedAt = pameka_7033_l005.Helpers.AppClock.Now
            };
            _db.Users.Add(created);
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear();
                vm.Error = "อีเมลหรือเบอร์โทรนี้ถูกใช้แล้ว กรุณาตรวจสอบอีกครั้ง";
                await _builder.FillListsAsync(vm); return View(BookingForm, vm);
            }
            customerId = created.UserId;
        }

        var booking = await _builder.ConfirmAsync(vm, customerId.Value, staffId: CurrentUserId);
        if (booking == null)
        {
            if (created != null)
            {
                _db.ChangeTracker.Clear();
                await _db.Users.Where(u => u.UserId == created.UserId).ExecuteDeleteAsync();
                vm.NewCustomer = true;
            }
            return View(BookingForm, vm);
        }

        Flash($"จองห้องให้ลูกค้าสำเร็จ (การจอง #{booking.BookingId} ยอด {booking.NetTotal:N2} บาท รับเงินสดแล้ว)"
            + (created != null ? $" · สร้างบัญชีลูกค้าใหม่ อีเมล {created.Email} รหัสผ่านชั่วคราว {tempPassword} (แสดงครั้งเดียว แจ้งลูกค้าให้เปลี่ยนรหัสที่หน้าตั้งค่า)" : ""));
        // ผู้จัดการไม่มีหน้าเช็คอินของฝ่ายบริการ → ไปหน้ารายละเอียดการจองแทน (ไม่เด้งไปหน้าไม่มีสิทธิ์)
        return User.IsInRole(RoleNames.FrontDesk) ? RedirectToAction(nameof(Index)) : Redirect($"/Booking/Details/{booking.BookingId}");
    }

    private static string NormalizeEmail(string? email) => (email ?? "").Trim().ToLowerInvariant();

    /// <summary>
    /// หาลูกค้าจากอีเมล: คืน userId, คืน 0 = ลูกค้าใหม่ที่กรอกข้อมูลครบ (สร้างบัญชีตอนยืนยัน), คืน null = ยังจองไม่ได้ (ตั้ง vm.Error)
    /// </summary>
    private async Task<int?> FindCustomerAsync(BookingFormViewModel vm)
    {
        var email = NormalizeEmail(vm.CustomerEmail);
        if (email.Length > 100 || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
        { vm.Error = "กรุณากรอกอีเมลลูกค้าให้ถูกต้อง"; return null; }

        var found = await _db.Users.AsNoTracking().Where(u => u.Email == email)
            .Select(u => new { u.UserId, u.Role.RoleName }).FirstOrDefaultAsync();
        if (found != null)
        {
            if (found.RoleName == RoleNames.Customer) return found.UserId;
            vm.Error = "อีเมลนี้เป็นบัญชีพนักงาน ใช้จองในนามลูกค้าไม่ได้"; return null;
        }

        vm.NewCustomer = true;
        var first = vm.NewFirstName?.Trim() ?? ""; var last = vm.NewLastName?.Trim() ?? ""; var phone = vm.NewPhone?.Trim() ?? "";
        if (first == "" || last == "" || phone == "")
        { vm.Error = "ยังไม่มีบัญชีลูกค้าที่ใช้อีเมลนี้ — กรอกชื่อ นามสกุล และเบอร์โทร เพื่อสร้างบัญชีให้ลูกค้าใหม่"; return null; }
        if (first.Length > 50 || last.Length > 50) { vm.Error = "ชื่อหรือนามสกุลยาวเกินไป"; return null; }
        if (!System.Text.RegularExpressions.Regex.IsMatch(phone, @"^\+?[0-9]{9,14}$")) { vm.Error = "เบอร์โทรต้องเป็นตัวเลข 9-14 หลัก"; return null; }
        if (await _db.Users.AnyAsync(u => u.Phone == phone)) { vm.Error = "เบอร์โทรนี้มีบัญชีอยู่แล้ว กรุณาใช้อีเมลของบัญชีนั้น"; return null; }
        return 0;
    }

    // ---------------------------------------------------------------- เช็คอิน / เช็คเอาท์
    /// <summary>ปฏิทินห้องว่างในหลังบ้าน (หน้าเดียวกับของลูกค้า กดช่องว่างเพื่อจองให้ลูกค้า)</summary>
    [HttpGet]
    public async Task<IActionResult> Availability(int? year, int? month) => View("~/Views/Rooms/Availability.cshtml", await BookingQueries.AvailabilityAsync(_db, year, month));

    [HttpGet]
    [Authorize(Roles = RoleNames.FrontDesk)]
    public async Task<IActionResult> Index(string? q, string? view)
    {
        var today = Today;
        var rows = await _db.Bookings.AsNoTracking()
            .Where(b => b.Status == "CONFIRMED" || b.Status == "CHECKED_IN" || (b.Status == "NO_SHOW" && b.CheckOut > today))
            .OrderBy(b => b.CheckIn)
            .Select(b => new
            {
                b.BookingId, b.CheckIn, b.CheckOut, b.Status,
                Customer = b.User.FirstName + " " + b.User.LastName,
                Phone = b.User.Phone,
                Rooms = b.BookingRooms.Select(r => r.Room.RoomNo).ToList(),
                Unsent = b.BookingServices.Count(s => !s.DeliveryTasks.Any()),
                PendingRefund = b.Payments.Any(p => p.PaymentType == BookingWorkflow.RefundType && p.Status == "PENDING"),
                IdCheck = b.Promo != null && b.Promo.NeedStaffConfirm ? b.Promo.PromoName : null,
                b.ArrivalTime, b.GuestRequest
            }).ToListAsync();

        var all = rows.Select(r =>
        {
            var flags = new PaymentFlags(false, false, r.PendingRefund, false);
            return new FrontDeskRowViewModel
            {
                BookingId = r.BookingId, Customer = r.Customer, Phone = r.Phone, Rooms = string.Join(", ", r.Rooms),
                CheckIn = r.CheckIn, CheckOut = r.CheckOut, Status = r.Status,
                UnsentServices = r.Unsent, PendingRefund = r.PendingRefund, IdCheckPromo = r.IdCheck,
                ArrivalTime = r.ArrivalTime, GuestRequest = r.GuestRequest,
                CanCheckIn = BookingWorkflow.CanCheckIn(r.Status, r.CheckIn, r.CheckOut, today, flags),
                CanCheckOut = BookingWorkflow.CanCheckOut(r.Status),
                CanSendDelivery = BookingWorkflow.CanSendDelivery(r.Status) && r.Unsent > 0,
                CanMarkNoShow = BookingWorkflow.CanMarkNoShow(r.Status, r.CheckIn, today, flags),
                CanUndoNoShow = BookingWorkflow.CanUndoNoShow(r.Status, r.CheckOut, today)
            };
        }).ToList();

        // แท็บ: ต้องเช็คอินวันนี้ / ต้องเช็คเอาท์ (ถึงหรือเลยวันออก) / ทั้งหมด + ค้นหาชื่อ เบอร์ เลขการจอง เลขห้อง
        bool IsIn(FrontDeskRowViewModel r) => r.CanCheckIn;
        bool IsOut(FrontDeskRowViewModel r) => r.Status == "CHECKED_IN" && r.CheckOut <= today;
        bool IsLate(FrontDeskRowViewModel r) => r.Status == "CONFIRMED" && r.CheckIn < today;
        view = view is "in" or "out" or "late" ? view : "all";
        q = q?.Trim();
        var list = all.Where(r => view switch { "in" => IsIn(r), "out" => IsOut(r), "late" => IsLate(r), _ => true });
        if (!string.IsNullOrEmpty(q))
        {
            var key = q.TrimStart('#');
            list = list.Where(r => r.BookingId.ToString() == key || r.Customer.Contains(q, StringComparison.OrdinalIgnoreCase)
                                   || r.Phone.Contains(q) || r.Rooms.Split(", ").Contains(key));
        }
        return View(new FrontDeskPageViewModel
        {
            View = view, Q = q, CountAll = all.Count, CountIn = all.Count(IsIn), CountOut = all.Count(IsOut), CountLate = all.Count(IsLate), Rows = list.ToList()
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.FrontDesk)]
    public async Task<IActionResult> CheckIn(int id)
    {
        var (b, rooms) = await LoadBookingWithRooms(id);
        if (b == null) return Stale();
        var flags = PaymentFlags.From(b.Payments.Select(p => (p.PaymentType, p.Status)));
        if (!BookingWorkflow.CanCheckIn(b.Status, b.CheckIn, b.CheckOut, Today, flags))
        {
            Flash("เช็คอินไม่ได้: ยังไม่ถึงวันเข้าพัก/เลยกำหนด หรือมีคำขอคืนเงินค้างอยู่", true);
            return RedirectToAction(nameof(Index));
        }

        var notReady = rooms.Where(r => r.Status is "OCCUPIED" or "CLEANING" or "MAINTENANCE").ToList();
        if (notReady.Count > 0)
        {
            Flash($"เช็คอินไม่ได้: ห้อง {string.Join(", ", notReady.Select(r => r.RoomNo))} ยังไม่พร้อม ({string.Join(", ", notReady.Select(r => ThaiLabels.RoomStatus(r.Status)).Distinct())}) "
                + "กรุณาให้ห้องว่างก่อน หรือจองห้องใหม่ให้ลูกค้า", true);
            return RedirectToAction(nameof(Index));
        }

        b.Status = "CHECKED_IN";
        foreach (var r in rooms) r.Status = "OCCUPIED";
        await _db.SaveChangesAsync();
        Flash($"เช็คอินการจอง #{b.BookingId} แล้ว");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>ลูกค้าไม่มาตามกำหนด: บันทึก NO_SHOW (เงินที่ชำระแล้วคงไว้ ผู้จัดการพิจารณาคืนเงินได้) ห้องว่างให้จองต่อทันที</summary>
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.FrontDesk)]
    public async Task<IActionResult> MarkNoShow(int id)
    {
        var (b, _) = await LoadBookingWithRooms(id);
        if (b == null) return Stale();
        var flags = PaymentFlags.From(b.Payments.Select(p => (p.PaymentType, p.Status)));
        if (!BookingWorkflow.CanMarkNoShow(b.Status, b.CheckIn, Today, flags))
        {
            Flash("บันทึกไม่มาเข้าพักได้ตั้งแต่วันถัดจากวันเข้าพัก สำหรับการจองที่ยังไม่เช็คอินและไม่มีคำขอคืนเงินค้าง", true);
            return RedirectToAction(nameof(Index), new { view = "late" });
        }
        b.Status = "NO_SHOW";
        Notify.Add(_db, b.UserId, $"การจอง #{b.BookingId}: ไม่ได้เข้าพักตามกำหนด",
            "โรงแรมบันทึกว่าไม่มาเข้าพัก หากเดินทางมาแล้วหรือมีเหตุจำเป็น กรุณาติดต่อเคาน์เตอร์ต้อนรับ", Notify.BookingLink(b.BookingId));
        await _db.SaveChangesAsync();
        Flash($"บันทึกการจอง #{b.BookingId} ว่าไม่มาเข้าพักแล้ว ห้องเปิดให้จองต่อได้");
        return RedirectToAction(nameof(Index), new { view = "late" });
    }

    /// <summary>บันทึกผิดหรือลูกค้ามาทีหลัง: กลับเป็น "จองสำเร็จ" เมื่อยังไม่เลยวันออกและห้องเดิมยังไม่ถูกจองทับ</summary>
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.FrontDesk)]
    public async Task<IActionResult> UndoNoShow(int id)
    {
        var (b, _) = await LoadBookingWithRooms(id);
        if (b == null) return Stale();
        if (!BookingWorkflow.CanUndoNoShow(b.Status, b.CheckOut, Today))
        {
            Flash("ยกเลิกสถานะไม่มาเข้าพักได้เฉพาะการจองที่ยังไม่เลยวันออก", true);
            return RedirectToAction(nameof(Index));
        }
        var from = Today > b.CheckIn ? Today : b.CheckIn;
        var mine = b.BookingRooms.Select(r => r.RoomId).ToList();
        var taken = await BookingQueries.BusyRoomIds(_db, from, b.CheckOut).Where(r => mine.Contains(r)).AnyAsync();
        if (taken)
        {
            Flash("ห้องของการจองนี้ถูกจองให้ผู้อื่นแล้วในช่วงที่เหลือ กรุณาจองห้องใหม่ให้ลูกค้า", true);
            return RedirectToAction(nameof(Index));
        }
        b.Status = "CONFIRMED";
        await _db.SaveChangesAsync();
        Flash($"การจอง #{b.BookingId} กลับเป็น \"จองสำเร็จ\" แล้ว เช็คอินได้ตามปกติ");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>เช็คเอาท์: ห้องเปลี่ยนเป็นกำลังทำความสะอาด และสร้างงานให้ฝ่ายทำความสะอาดอัตโนมัติ</summary>
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.FrontDesk)]
    public async Task<IActionResult> CheckOut(int id)
    {
        var (b, rooms) = await LoadBookingWithRooms(id);
        if (b == null || !BookingWorkflow.CanCheckOut(b.Status)) return Stale();

        b.Status = "CHECKED_OUT";
        foreach (var r in rooms)
        {
            r.Status = "CLEANING";
            _db.HousekeepingTasks.Add(new HousekeepingTask { RoomId = r.RoomId, BookingId = b.BookingId, Status = "PENDING" });
        }
        await _db.SaveChangesAsync();
        Flash($"เช็คเอาท์การจอง #{b.BookingId} แล้ว ส่งงานทำความสะอาด {rooms.Count} ห้องให้ฝ่ายทำความสะอาดแล้ว");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>ส่งรายการบริการเสริมของการจอง (ที่ยังไม่เคยส่ง) ให้ฝ่ายส่งของ</summary>
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.FrontDesk)]
    public async Task<IActionResult> SendDelivery(int id)
    {
        var b = await _db.Bookings.AsNoTracking().FirstOrDefaultAsync(x => x.BookingId == id);
        if (b == null || !BookingWorkflow.CanSendDelivery(b.Status)) return Stale();

        var sentBsIds = await _db.DeliveryTasks.Where(t => t.Bs.BookingId == id).Select(t => t.BsId).ToListAsync();
        var items = await _db.BookingServices.AsNoTracking()
            .Where(s => s.BookingId == id && !sentBsIds.Contains(s.BsId)).ToListAsync();
        if (items.Count == 0)
        {
            Flash("ไม่มีรายการบริการเสริมที่ต้องส่งเพิ่ม", true);
            return RedirectToAction(nameof(Index));
        }

        foreach (var s in items)
            _db.DeliveryTasks.Add(new DeliveryTask { BsId = s.BsId, RoomId = s.RoomId, Status = "PENDING", CreatedAt = pameka_7033_l005.Helpers.AppClock.Now });
        await _db.SaveChangesAsync();
        Flash($"ส่งงานให้ฝ่ายส่งของ {items.Count} รายการแล้ว");
        return RedirectToAction(nameof(Index));
    }

    // ---------------------------------------------------------------- สถานะห้อง
    [HttpGet]
    [Authorize(Roles = RoleNames.FrontDesk)]
    public async Task<IActionResult> Rooms()
    {
        var openRoomIds = await _db.HousekeepingTasks.Where(t => t.Status != "DONE").Select(t => t.RoomId).Distinct().ToListAsync();
        var rooms = await _db.Rooms.AsNoTracking().OrderBy(r => r.RoomNo)
            .Select(r => new { r.RoomId, r.RoomNo, Type = r.Type.TypeName, r.Status }).ToListAsync();

        return View(rooms.Select(r =>
        {
            var open = openRoomIds.Contains(r.RoomId);
            return new RoomStatusRowViewModel
            {
                RoomId = r.RoomId, RoomNo = r.RoomNo, TypeName = r.Type, Status = r.Status,
                CanMakeAvailable = BookingWorkflow.CanSetRoomStatus(r.Status, "AVAILABLE", open),
                CanMaintenance = BookingWorkflow.CanSetRoomStatus(r.Status, "MAINTENANCE", open),
                CanSendCleaning = BookingWorkflow.CanSendCleaning(r.Status, open)
            };
        }).ToList());
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.FrontDesk)]
    public async Task<IActionResult> SetRoomStatus(int roomId, string status)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.RoomId == roomId);
        if (room == null) return Stale(nameof(Rooms));

        var open = await _db.HousekeepingTasks.AnyAsync(t => t.RoomId == roomId && t.Status != "DONE");
        if (!BookingWorkflow.CanSetRoomStatus(room.Status, status, open))
        {
            Flash($"เปลี่ยนสถานะห้อง {room.RoomNo} ไม่ได้ (ห้องมีผู้พัก, สถานะเดิมอยู่แล้ว หรือยังมีงานทำความสะอาดค้าง)", true);
            return RedirectToAction(nameof(Rooms));
        }

        room.Status = status;
        await _db.SaveChangesAsync();
        Flash($"ห้อง {room.RoomNo} เป็น \"{ThaiLabels.RoomStatus(status)}\" แล้ว");
        return RedirectToAction(nameof(Rooms));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = RoleNames.FrontDesk)]
    public async Task<IActionResult> SendCleaning(int roomId)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.RoomId == roomId);
        if (room == null) return Stale(nameof(Rooms));

        var open = await _db.HousekeepingTasks.AnyAsync(t => t.RoomId == roomId && t.Status != "DONE");
        if (!BookingWorkflow.CanSendCleaning(room.Status, open))
        {
            Flash($"ส่งงานทำความสะอาดห้อง {room.RoomNo} ไม่ได้ (ห้องมีผู้พัก หรือมีงานค้างอยู่แล้ว)", true);
            return RedirectToAction(nameof(Rooms));
        }

        room.Status = "CLEANING";
        _db.HousekeepingTasks.Add(new HousekeepingTask { RoomId = room.RoomId, Status = "PENDING" });
        await _db.SaveChangesAsync();
        Flash($"ส่งงานทำความสะอาดห้อง {room.RoomNo} แล้ว");
        return RedirectToAction(nameof(Rooms));
    }

    // ---------------------------------------------------------------- ตัวช่วย
    private async Task<(Booking? Booking, List<Room> Rooms)> LoadBookingWithRooms(int id)
    {
        var b = await _db.Bookings.Include(x => x.BookingRooms).Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.BookingId == id);
        if (b == null) return (null, new List<Room>());
        var roomIds = b.BookingRooms.Select(r => r.RoomId).ToList();
        var rooms = await _db.Rooms.Where(r => roomIds.Contains(r.RoomId)).ToListAsync();
        return (b, rooms);
    }

    // ---------------------------------------------------------------- Part 52: กิจกรรมวันนี้ (รายชื่อผู้ร่วมแต่ละรอบ)
    [HttpGet]
    public async Task<IActionResult> Activities(DateOnly? date)
    {
        var d = date ?? Today;
        var acts = await _db.Activities.AsNoTracking().Where(a => a.IsActive || _db.ActivityBookings.Any(b => b.ActivityId == a.ActivityId && b.ActDate == d))
            .OrderBy(a => a.Name).ToListAsync();
        var rows = await _db.ActivityBookings.AsNoTracking().Where(b => b.ActDate == d && b.Status != "CANCELLED")
            .Select(b => new { b.AbId, b.ActivityId, b.StartTime, b.People, b.Total, b.Status, b.BookingId,
                Name = b.User.FirstName + " " + b.User.LastName, b.User.Phone }).ToListAsync();
        var bids = rows.Where(r => r.BookingId != null).Select(r => r.BookingId!.Value).Distinct().ToList();
        var roomsOf = (await _db.BookingRooms.AsNoTracking().Where(br => bids.Contains(br.BookingId)).Select(br => new { br.BookingId, br.Room.RoomNo }).ToListAsync())
            .GroupBy(x => x.BookingId).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.RoomNo).OrderBy(x => x)));
        var vm = new ActivityRosterViewModel { Date = d };
        foreach (var a in acts)
            foreach (var t in HotelActivities.OpenOn(a, d) ? HotelActivities.ParseTimes(a.Times) : rows.Where(r => r.ActivityId == a.ActivityId).Select(r => r.StartTime).Distinct().ToList())
            {
                var people = rows.Where(r => r.ActivityId == a.ActivityId && r.StartTime == t).OrderBy(r => r.Name)
                    .Select(r => new ActivityBookingRowViewModel { AbId = r.AbId, GuestName = r.Name, Phone = r.Phone, People = r.People, Total = r.Total, Status = r.Status,
                        Rooms = r.BookingId is { } bid ? roomsOf.GetValueOrDefault(bid) : null }).ToList();
                vm.Sessions.Add((a.ActivityId, a.Name, t, a.Location, a.Capacity, people));
            }
        vm.Sessions = vm.Sessions.OrderBy(s => s.Time).ThenBy(s => s.Name).ToList();
        return View(vm);
    }

    /// <summary>บันทึกผลการเข้าร่วม: ATTENDED / NO_SHOW / CONFIRMED (ย้อนกลับ)</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = RoleNames.FrontDesk)]
    public async Task<IActionResult> MarkActivity(int id, string status)
    {
        if (status is not ("ATTENDED" or "NO_SHOW" or "CONFIRMED")) return BadRequest();
        var b = await _db.ActivityBookings.FirstOrDefaultAsync(x => x.AbId == id);
        if (b == null) return NotFound();
        if (b.Status == "CANCELLED") { Flash("รายการนี้ถูกยกเลิกแล้ว", true); return RedirectToAction(nameof(Activities), new { date = b.ActDate.ToString("yyyy-MM-dd") }); }
        if (b.ActDate > Today) { Flash("บันทึกการเข้าร่วมได้ตั้งแต่วันกิจกรรม", true); return RedirectToAction(nameof(Activities), new { date = b.ActDate.ToString("yyyy-MM-dd") }); }
        b.Status = status;
        await _db.SaveChangesAsync();
        await AuditTrail.WriteAsync(HttpContext, "FrontDesk.MarkActivity", id, true, HotelActivities.StatusLabel(status));
        Flash($"บันทึก \"{HotelActivities.StatusLabel(status)}\" แล้ว");
        return RedirectToAction(nameof(Activities), new { date = b.ActDate.ToString("yyyy-MM-dd") });
    }
}

// ======================================================================
// DeliveryController
// ======================================================================
/// <summary>ฝ่ายส่งของ: รับงานส่งบริการเสริม และของใช้ที่ผู้เข้าพักขอเพิ่ม (Part 53) → กำลังนำส่ง → ส่งแล้ว / ไม่พบผู้เข้าพัก</summary>
[Authorize(Roles = RoleNames.Delivery)]
public class DeliveryController : AppController
{
    private readonly Csi402dbContext _db;
    public DeliveryController(Csi402dbContext db) => _db = db;

    /// <summary>ชนิดงาน: S = บริการเสริมที่สั่งไว้ · R = ของใช้ที่ผู้เข้าพักขอเพิ่ม</summary>
    private static bool IsReq(string? kind) => kind == "R";

    [HttpGet]
    public Task<IActionResult> Index() => List(done: false);

    [HttpGet]
    public Task<IActionResult> Done() => List(done: true);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Take(int id, string? kind)
    {
        // อัปเดตแบบมีเงื่อนไขในคำสั่งเดียว: ถ้าอีกคนกดรับไปก่อน จะไม่ทับชื่อ
        var me = CurrentUserId; var doing = BookingWorkflow.DeliveryDoing; var now = AppClock.Now;
        var taken = IsReq(kind)
            ? await _db.ItemRequests.Where(x => x.RequestId == id && x.Status == "PENDING")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, doing).SetProperty(x => x.AssignedTo, me).SetProperty(x => x.PickedAt, now))
            : await _db.DeliveryTasks.Where(x => x.TaskId == id && x.Status == "PENDING")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, doing).SetProperty(x => x.AssignedTo, me).SetProperty(x => x.PickedAt, now));
        if (taken == 0) return Stale();
        Flash("รับงานแล้ว — สถานะเป็น \"กำลังนำส่ง\"");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Finish(int id, string? kind)
    {
        var now = AppClock.Now;
        if (IsReq(kind))
        {
            var r = await _db.ItemRequests.Include(x => x.Room).FirstOrDefaultAsync(x => x.RequestId == id);
            if (r == null || !BookingWorkflow.CanFinishTask(r.Status, BookingWorkflow.DeliveryDoing, r.AssignedTo, CurrentUserId)) return Stale();
            r.Status = "DONE"; r.DoneAt = now;
            Notify.Add(_db, r.UserId, $"ส่ง{RoomRequests.ItemName(r.Item)}ถึงห้อง {r.Room.RoomNo} แล้ว",
                $"{RoomRequests.ItemName(r.Item)} × {r.Qty} ส่งถึงห้องเรียบร้อย ขอให้มีความสุขกับการเข้าพัก", "/Member#mb-req");
        }
        else
        {
            var t = await _db.DeliveryTasks.Include(x => x.Room).Include(x => x.Bs).ThenInclude(x => x.Service).Include(x => x.Bs.Booking)
                .FirstOrDefaultAsync(x => x.TaskId == id);
            if (t == null || !BookingWorkflow.CanFinishTask(t.Status, BookingWorkflow.DeliveryDoing, t.AssignedTo, CurrentUserId)) return Stale();
            t.Status = "DONE"; t.DoneAt = now;
            Notify.Add(_db, t.Bs.Booking.UserId, $"ส่ง{t.Bs.Service.ServiceName}ถึงห้อง {t.Room.RoomNo} แล้ว",
                $"{t.Bs.Service.ServiceName} × {t.Bs.Quantity} ส่งถึงห้องเรียบร้อย", $"/Booking/Details/{t.Bs.BookingId}");
        }
        await _db.SaveChangesAsync();
        Flash("ส่งสำเร็จแล้ว แจ้งผู้เข้าพักเรียบร้อย");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>ไปถึงห้องแล้วไม่พบผู้เข้าพัก: คืนงานเข้าคิว (รอนำส่งใหม่) นับครั้ง และแจ้งผู้เข้าพัก</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> NotHome(int id, string? kind)
    {
        var uid = CurrentUserId;
        int guest; string what, room, link;
        if (IsReq(kind))
        {
            var r = await _db.ItemRequests.Include(x => x.Room).FirstOrDefaultAsync(x => x.RequestId == id);
            if (r == null || !BookingWorkflow.CanFinishTask(r.Status, BookingWorkflow.DeliveryDoing, r.AssignedTo, uid)) return Stale();
            r.Status = "PENDING"; r.AssignedTo = null; r.PickedAt = null; r.Missed = (byte)Math.Min(r.Missed + 1, 255);
            (guest, what, room, link) = (r.UserId, RoomRequests.ItemName(r.Item), r.Room.RoomNo, "/Member#mb-req");
        }
        else
        {
            var t = await _db.DeliveryTasks.Include(x => x.Room).Include(x => x.Bs).ThenInclude(x => x.Service).Include(x => x.Bs.Booking)
                .FirstOrDefaultAsync(x => x.TaskId == id);
            if (t == null || !BookingWorkflow.CanFinishTask(t.Status, BookingWorkflow.DeliveryDoing, t.AssignedTo, uid)) return Stale();
            t.Status = "PENDING"; t.AssignedTo = null; t.PickedAt = null; t.Missed = (byte)Math.Min(t.Missed + 1, 255);
            (guest, what, room, link) = (t.Bs.Booking.UserId, t.Bs.Service.ServiceName, t.Room.RoomNo, $"/Booking/Details/{t.Bs.BookingId}");
        }
        Notify.Add(_db, guest, $"นำ{what}ไปส่งที่ห้อง {room} แต่ไม่พบผู้เข้าพัก",
            "พนักงานจะนำส่งให้อีกครั้ง หากต้องการกำหนดเวลา ติดต่อแผนกต้อนรับได้", link);
        await _db.SaveChangesAsync();
        Flash($"บันทึกแล้ว: ไม่พบผู้เข้าพักห้อง {room} งานกลับเข้าคิวรอนำส่งใหม่");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>ตั้ง/ยกเลิกงานด่วน (งานด่วนขึ้นบนสุดของคิว)</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Urgent(int id, string? kind)
    {
        var n = IsReq(kind)
            ? await _db.ItemRequests.Where(x => x.RequestId == id && (x.Status == "PENDING" || x.Status == "DELIVERING"))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Urgent, x => !x.Urgent))
            : await _db.DeliveryTasks.Where(x => x.TaskId == id && x.Status != "DONE")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Urgent, x => !x.Urgent));
        if (n == 0) return Stale();
        Flash("ปรับความด่วนของงานแล้ว");
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> List(bool done)
    {
        var uid = CurrentUserId; var now = AppClock.Now;
        var rows = new List<TaskRowViewModel>();

        IQueryable<DeliveryTask> q = _db.DeliveryTasks.AsNoTracking();
        q = done ? q.Where(t => t.Status == "DONE").OrderByDescending(t => t.DoneAt)
                 : q.Where(t => t.Status != "DONE" && (t.AssignedTo == null || t.AssignedTo == uid)).OrderBy(t => t.TaskId);
        rows.AddRange((await q.Take(100).Select(t => new
        {
            t.TaskId, RoomNo = t.Room.RoomNo, Service = t.Bs.Service.ServiceName, Qty = t.Bs.Quantity, Guest = t.Bs.Booking.User.FirstName,
            t.Status, t.AssignedTo, Assignee = t.AssignedToNavigation != null ? t.AssignedToNavigation.FirstName : null, t.DoneAt,
            t.CreatedAt, t.PickedAt, t.Urgent, t.Missed
        }).ToListAsync()).Select(r => new TaskRowViewModel
        {
            TaskId = r.TaskId, Kind = "S", RoomNo = r.RoomNo, Detail = $"{r.Service} × {r.Qty}", Source = "บริการเสริม", Guest = r.Guest,
            Status = r.Status, StatusText = RoomRequests.StatusLabel(r.Status, r.Missed), Assignee = r.Assignee, DoneAt = r.DoneAt,
            CreatedAt = r.CreatedAt, PickedAt = r.PickedAt, Urgent = r.Urgent, Missed = r.Missed,
            CanTake = BookingWorkflow.CanTakeTask(r.Status),
            CanFinish = BookingWorkflow.CanFinishTask(r.Status, BookingWorkflow.DeliveryDoing, r.AssignedTo, uid)
        }));

        IQueryable<ItemRequest> rq = _db.ItemRequests.AsNoTracking();
        rq = done ? rq.Where(r => r.Status == "DONE").OrderByDescending(r => r.DoneAt)
                  : rq.Where(r => (r.Status == "PENDING" || r.Status == "DELIVERING") && (r.AssignedTo == null || r.AssignedTo == uid)).OrderBy(r => r.RequestId);
        rows.AddRange((await rq.Take(100).Select(r => new
        {
            r.RequestId, RoomNo = r.Room.RoomNo, r.Item, r.Qty, r.Note, Guest = _db.Users.Where(u => u.UserId == r.UserId).Select(u => u.FirstName).FirstOrDefault(),
            r.Status, r.AssignedTo, Assignee = r.AssignedToNavigation != null ? r.AssignedToNavigation.FirstName : null, r.DoneAt,
            r.CreatedAt, r.PickedAt, r.Urgent, r.Missed
        }).ToListAsync()).Select(r => new TaskRowViewModel
        {
            TaskId = r.RequestId, Kind = "R", RoomNo = r.RoomNo, Detail = $"{RoomRequests.ItemName(r.Item)} × {r.Qty}", Note = r.Note,
            Source = "ลูกค้าขอเพิ่ม", Guest = r.Guest,
            Status = r.Status, StatusText = RoomRequests.StatusLabel(r.Status, r.Missed), Assignee = r.Assignee, DoneAt = r.DoneAt,
            CreatedAt = r.CreatedAt, PickedAt = r.PickedAt, Urgent = r.Urgent, Missed = r.Missed,
            CanTake = BookingWorkflow.CanTakeTask(r.Status),
            CanFinish = BookingWorkflow.CanFinishTask(r.Status, BookingWorkflow.DeliveryDoing, r.AssignedTo, uid)
        }));

        foreach (var r in rows)
        {
            r.Minutes = RoomRequests.Minutes(r.CreatedAt, done && r.DoneAt is { } d ? d : now);
            r.Late = !done && r.Minutes > RoomRequests.LateMinutes;
        }
        rows = done ? rows.OrderByDescending(r => r.DoneAt).Take(100).ToList()
                    // งานด่วนก่อน → งานที่กำลังนำส่งของตัวเอง → รอนานสุดก่อน
                    : rows.OrderByDescending(r => r.Urgent).ThenByDescending(r => r.Status == BookingWorkflow.DeliveryDoing)
                          .ThenBy(r => r.CreatedAt ?? DateTime.MaxValue).ToList();

        return View("TaskList", new TaskListViewModel
        {
            Title = done ? "ห้องที่ส่งสำเร็จ" : "ห้องที่ต้องส่ง",
            Description = done ? "งานส่งของที่เสร็จแล้ว (100 รายการล่าสุด)"
                : $"งานด่วนอยู่บนสุด · รอเกิน {RoomRequests.LateMinutes} นาทีจะเป็นสีแดง · รับงาน → \"ส่งสำเร็จ\" หรือ \"ไม่พบผู้เข้าพัก\" (งานกลับเข้าคิว)",
            EmptyText = done ? "ยังไม่มีงานที่ส่งสำเร็จ" : "ตอนนี้ไม่มีงานส่งของค้างอยู่",
            TakeLabel = "รับงาน", FinishLabel = "ส่งสำเร็จ", ShowDone = done, Delivery = true,
            Rows = rows
        });
    }
}

// ======================================================================
// HousekeepingController
// ======================================================================
/// <summary>ฝ่ายทำความสะอาด: รับงานห้องที่เช็คเอาท์/ที่ฝ่ายบริการส่งมา ทำเสร็จแล้วห้องกลับเป็น "ว่าง"</summary>
[Authorize(Roles = RoleNames.Housekeeping)]
public class HousekeepingController : AppController
{
    private readonly Csi402dbContext _db;
    public HousekeepingController(Csi402dbContext db) => _db = db;

    [HttpGet]
    public Task<IActionResult> Index() => List(done: false);

    [HttpGet]
    public Task<IActionResult> Done() => List(done: true);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Take(int id)
    {
        var me = CurrentUserId; var doing = BookingWorkflow.CleaningDoing;
        var taken = await _db.HousekeepingTasks.Where(x => x.TaskId == id && x.Status == "PENDING")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, doing).SetProperty(x => x.AssignedTo, me));
        if (taken == 0) return Stale();
        Flash("รับงานแล้ว");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Finish(int id)
    {
        var t = await _db.HousekeepingTasks.Include(x => x.Room).FirstOrDefaultAsync(x => x.TaskId == id);
        if (t == null || !BookingWorkflow.CanFinishTask(t.Status, BookingWorkflow.CleaningDoing, t.AssignedTo, CurrentUserId))
            return Stale();

        t.Status = "DONE";
        t.DoneAt = pameka_7033_l005.Helpers.AppClock.Now;
        if (t.Room.Status == "CLEANING") t.Room.Status = "AVAILABLE";
        await _db.SaveChangesAsync();
        Flash($"ทำความสะอาดห้อง {t.Room.RoomNo} เสร็จแล้ว ห้องกลับเป็น \"ว่าง\"");
        return RedirectToAction(nameof(Index));
    }


    private async Task<IActionResult> List(bool done)
    {
        var uid = CurrentUserId;
        IQueryable<HousekeepingTask> q = _db.HousekeepingTasks.AsNoTracking();
        q = done
            ? q.Where(t => t.Status == "DONE").OrderByDescending(t => t.DoneAt)
            : q.Where(t => t.Status != "DONE" && (t.AssignedTo == null || t.AssignedTo == uid)).OrderBy(t => t.TaskId);

        var rows = await q.Take(100).Select(t => new
        {
            t.TaskId, RoomNo = t.Room.RoomNo, t.BookingId, t.Status, t.AssignedTo,
            Assignee = t.AssignedToNavigation != null ? t.AssignedToNavigation.FirstName : null, t.DoneAt
        }).ToListAsync();

        return View("TaskList", new TaskListViewModel
        {
            Title = done ? "ห้องที่ทำไปแล้ว" : "ห้องที่ต้องทำความสะอาด",
            Description = done ? "งานที่ทำเสร็จแล้ว (100 รายการล่าสุด)" : "รับงานก่อน แล้วกด \"ทำเสร็จแล้ว\" ห้องจะกลับเป็น \"ว่าง\" ให้จองได้อีก",
            EmptyText = done ? "ยังไม่มีงานที่ทำเสร็จ" : "ตอนนี้ไม่มีห้องที่ต้องทำความสะอาด",
            TakeLabel = "รับงาน", FinishLabel = "ทำเสร็จแล้ว", ShowDone = done,
            Rows = rows.Select(r => new TaskRowViewModel
            {
                TaskId = r.TaskId, RoomNo = r.RoomNo,
                Detail = r.BookingId.HasValue ? $"เช็คเอาท์ของการจอง #{r.BookingId}" : "ทำความสะอาดทั่วไป (ฝ่ายบริการสั่ง)",
                Status = r.Status, Assignee = r.Assignee, DoneAt = r.DoneAt,
                CanTake = BookingWorkflow.CanTakeTask(r.Status),
                CanFinish = BookingWorkflow.CanFinishTask(r.Status, BookingWorkflow.CleaningDoing, r.AssignedTo, uid)
            }).ToList()
        });
    }
}
