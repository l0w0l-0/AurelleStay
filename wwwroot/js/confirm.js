/*
 * AurelleStay — กล่องยืนยันแบบเดียวกันทั้งระบบ (Part 46, ปรับ Part 53)
 * ใช้ SweetAlert2 (MIT — wwwroot/lib/sweetalert2) แทนกล่อง confirm() ของเบราว์เซอร์
 *
 * วิธีใช้ (แนะนำ): <form ... data-confirm="ลบห้อง 101 ?">
 *   ข้อความอยู่ในแอตทริบิวต์ จึงปลอดภัยแม้ชื่อมีเครื่องหมาย ' หรือ " (เดิมเขียนเป็น onsubmit="return confirm('ลบ @ชื่อ')"
 *   ถ้าชื่อมี ' โค้ด JavaScript จะพัง เบราว์เซอร์ข้ามการถาม แล้ว "ลบทันทีโดยไม่ยืนยัน")
 * ยังรองรับแบบเดิม onsubmit="return confirm(...)" สำหรับข้อความที่คำนวณตอนกด (เช่น ล้างประวัติตามตัวเลือก)
 * ถ้าไฟล์ SweetAlert2 โหลดไม่ได้ ใช้ confirm() ของเบราว์เซอร์แทน — ไม่มีกรณีที่ส่งฟอร์มโดยไม่ถาม
 */
(function () {
    'use strict';
    var danger = /ลบ|ยกเลิก|ปิดห้อง|ล้าง|ปฏิเสธ|ไม่มาเข้าพัก|คืนเงิน|ถาวร|ออกจากระบบ|ออกรหัส/;
    var hasSwal = typeof window.Swal !== 'undefined';
    var nativeConfirm = window.confirm.bind(window);

    function ask(message) {
        if (!hasSwal) return Promise.resolve(nativeConfirm(message));
        var isDanger = danger.test(message);
        return window.Swal.fire({
            text: message,
            icon: isDanger ? 'warning' : 'question',
            showCancelButton: true,
            confirmButtonText: 'ยืนยัน',
            cancelButtonText: 'ยกเลิก',
            reverseButtons: true,
            focusCancel: isDanger,
            buttonsStyling: false,
            customClass: {
                popup: 'as-pop',
                confirmButton: 'cx-btn' + (isDanger ? ' cx-btn--danger' : ''),
                cancelButton: 'cx-btn cx-btn--ghost',
                actions: 'as-actions'
            }
        }).then(function (r) { return r.isConfirmed; });
    }

    function resubmit(form, submitter) {
        form.dataset.asConfirmed = '1';
        if (form.requestSubmit) form.requestSubmit(submitter && submitter.form === form ? submitter : undefined);
        else form.submit();
    }

    // แบบใหม่: data-confirm (ดักตั้งแต่ capture phase ก่อนสคริปต์อื่น เช่น ตัวกันกดซ้ำ)
    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (!(form instanceof HTMLFormElement) || !form.hasAttribute('data-confirm')) return;
        if (form.dataset.asConfirmed === '1') { form.dataset.asConfirmed = ''; return; }
        e.preventDefault();
        e.stopImmediatePropagation();
        var submitter = e.submitter || null;
        ask(form.getAttribute('data-confirm')).then(function (ok) { if (ok) resubmit(form, submitter); });
    }, true);

    // แบบเดิม: onsubmit="return confirm(...)" (ข้อความคำนวณตอนกด)
    function wire(form) {
        var original = form.onsubmit;
        if (!hasSwal || typeof original !== 'function' || form.dataset.asConfirm) return;
        var src = form.getAttribute('onsubmit') || '';
        if (src.indexOf('confirm(') < 0) return;
        form.dataset.asConfirm = '1';
        form.onsubmit = function (e) {
            if (form.dataset.asConfirmed === '1') { form.dataset.asConfirmed = ''; return true; }
            var message = null, realConfirm = window.confirm;
            window.confirm = function (m) { message = String(m); return false; };
            try { original.call(form, e); } finally { window.confirm = realConfirm; }
            if (message === null) return true;
            e.preventDefault();
            var submitter = e.submitter || null;
            ask(message).then(function (ok) { if (ok) resubmit(form, submitter); });
            return false;
        };
    }

    function init() { document.querySelectorAll('form[onsubmit]').forEach(wire); }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();
