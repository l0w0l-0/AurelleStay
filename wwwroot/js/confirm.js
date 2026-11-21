/*
 * AurelleStay — กล่องยืนยันแบบเดียวกันทั้งระบบ (Part 46)
 * ใช้ SweetAlert2 (MIT — wwwroot/lib/sweetalert2) แทนกล่อง confirm() ของเบราว์เซอร์ ที่หน้าตาไม่เข้ากับเว็บ
 *
 * ไม่ต้องแก้ฟอร์มเดิม: ฟอร์มที่มี onsubmit="return confirm('...')" ยังเขียนแบบเดิม
 * สคริปต์นี้อ่านข้อความจาก onsubmit เดิม แสดงกล่องยืนยันธีมม่วง-ทอง แล้วส่งฟอร์มต่อเมื่อกด "ยืนยัน"
 * ถ้าไฟล์ SweetAlert2 โหลดไม่ได้ หรือปิด JavaScript ฟอร์มยังใช้ confirm() ของเบราว์เซอร์ตามเดิม
 */
(function () {
    'use strict';
    if (typeof window.Swal === 'undefined') return;

    var danger = /ลบ|ยกเลิก|ปิดห้อง|ล้าง|ปฏิเสธ|ไม่มาเข้าพัก|คืนเงิน|ถาวร/;

    function ask(message) {
        return window.Swal.fire({
            text: message,
            icon: danger.test(message) ? 'warning' : 'question',
            showCancelButton: true,
            confirmButtonText: 'ยืนยัน',
            cancelButtonText: 'ยกเลิก',
            reverseButtons: true,
            focusCancel: danger.test(message),
            buttonsStyling: false,
            customClass: {
                popup: 'as-pop',
                confirmButton: 'cx-btn' + (danger.test(message) ? ' cx-btn--danger' : ''),
                cancelButton: 'cx-btn cx-btn--ghost',
                actions: 'as-actions'
            }
        }).then(function (r) { return r.isConfirmed; });
    }

    function wire(form) {
        var original = form.onsubmit;
        if (typeof original !== 'function' || form.dataset.asConfirm) return;
        var src = form.getAttribute('onsubmit') || '';
        if (src.indexOf('confirm(') < 0) return;
        form.dataset.asConfirm = '1';
        form.onsubmit = function (e) {
            if (form.dataset.asConfirmed === '1') { form.dataset.asConfirmed = ''; return true; }
            // อ่านข้อความจากโค้ดเดิม: เรียก onsubmit เดิมโดยให้ confirm() จดข้อความไว้แทนการเปิดกล่อง
            var message = null, realConfirm = window.confirm;
            window.confirm = function (m) { message = String(m); return false; };
            try { original.call(form, e); } finally { window.confirm = realConfirm; }
            if (message === null) return true;            // onsubmit เดิมไม่ได้ถาม → ส่งตามปกติ
            e.preventDefault();
            var submitter = e.submitter || null;
            ask(message).then(function (ok) {
                if (!ok) return;
                form.dataset.asConfirmed = '1';
                if (form.requestSubmit) form.requestSubmit(submitter && submitter.form === form ? submitter : undefined);
                else form.submit();
            });
            return false;
        };
    }

    function init() { document.querySelectorAll('form[onsubmit]').forEach(wire); }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();
