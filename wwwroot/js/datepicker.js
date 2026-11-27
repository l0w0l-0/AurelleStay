/*
 * AurelleStay — ตัวเลือกช่วงวันเข้าพัก
 * ใช้กับคู่ช่อง <input type="date" data-dp-in> / <input type="date" data-dp-out> ในฟอร์มเดียวกัน
 *
 * - ทุกอุปกรณ์: วันเช็คเอาท์ต้องหลังเช็คอินอย่างน้อย 1 คืน และแสดงจำนวนคืนที่เลือก
 * - ทุกอุปกรณ์ (Part 45): ใช้ปฏิทินไทยชุดเดียวกัน (flatpickr, MIT — wwwroot/lib/flatpickr) หน้าตาเหมือนกันทุกหน้า
 *   ช่วงวันเข้าพัก: จอคอมแสดง 2 เดือน มือถือ 1 เดือน · คลิกวันเข้าพักแล้ววันออกในปฏิทินเดียว
 *   ถ้าช่องเช็คอินมี data-dp-booked="<url>" จะดึงคืนที่ห้องไม่ว่างมาขีดฆ่าและไม่ให้เลือกช่วงที่ทับ
 * - ช่องวันที่เดี่ยวทุกช่อง (วันเกิด ตัวกรองรายงาน ช่วงราคา ฯลฯ) ใช้ปฏิทินเดียวกัน แสดงแบบ "พ. 30 ธ.ค. 2026"
 *   ช่องที่ไม่บังคับมีปุ่ม "ล้าง" · ไม่ต้องการก็ใส่ data-dp-native ที่ช่องนั้น
 * - ถ้าไฟล์ flatpickr โหลดไม่ได้ หรือปิด JavaScript ฟอร์มยังใช้ช่องวันที่ปกติได้ ค่าที่ส่งยังเป็น yyyy-MM-dd เหมือนเดิม
 */
(function () {
    'use strict';

    function pad(n) { return String(n).padStart(2, '0'); }
    function iso(d) { return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()); }
    function parse(s) { return s ? new Date(s + 'T00:00:00') : null; }
    function addDays(s, n) { var d = parse(s); d.setDate(d.getDate() + n); return iso(d); }
    function nightsBetween(a, b) { return a && b ? Math.round((parse(b) - parse(a)) / 864e5) : 0; }

    var thMonths = ['ม.ค.', 'ก.พ.', 'มี.ค.', 'เม.ย.', 'พ.ค.', 'มิ.ย.', 'ก.ค.', 'ส.ค.', 'ก.ย.', 'ต.ค.', 'พ.ย.', 'ธ.ค.'];
    var thDays = ['อา.', 'จ.', 'อ.', 'พ.', 'พฤ.', 'ศ.', 'ส.'];
    function pretty(s) { var d = parse(s); return d ? thDays[d.getDay()] + ' ' + d.getDate() + ' ' + thMonths[d.getMonth()] + ' ' + d.getFullYear() : ''; }

    var desktop = window.matchMedia && window.matchMedia('(min-width: 700px)').matches;   // ใช้กำหนดจำนวนเดือนที่แสดงเท่านั้น
    var hasFp = typeof window.flatpickr === 'function';

    // ช่องแสดงผลแทนช่องเดิม (ช่องเดิมยังอยู่ในฟอร์มและส่งค่า yyyy-MM-dd เหมือนเดิม) — ใช้ทั้งช่วงวันและวันเดี่ยว
    function makeDisplayFor(src) {
        var t = document.createElement('input');
        t.type = 'text';
        t.readOnly = true;
        t.className = 'dp-display' + (src.className ? ' ' + src.className : '');
        t.setAttribute('inputmode', 'none');
        t.setAttribute('aria-haspopup', 'dialog');
        if (src.style.cssText) t.style.cssText = src.style.cssText;
        var lab = src.closest('label');
        var name = src.getAttribute('aria-label') || (lab && lab.firstChild && lab.firstChild.textContent ? lab.firstChild.textContent.trim() : '') || 'วันที่';
        t.setAttribute('aria-label', name + ' (กด Enter เพื่อเปิดปฏิทิน)');
        if (src.placeholder) t.placeholder = src.placeholder; else t.placeholder = 'เลือกวันที่';
        src.insertAdjacentElement('afterend', t);
        src.hidden = true;
        src.dataset.dpRequired = src.required ? '1' : '';
        src.required = false;   // ช่องที่ซ่อนตรวจด้วยเบราว์เซอร์ไม่ได้ ระบบตรวจวันที่ซ้ำที่เซิร์ฟเวอร์อยู่แล้ว
        src.setAttribute('tabindex', '-1');
        return t;
    }

    document.querySelectorAll('input[data-dp-in]').forEach(function (inp) {
        var form = inp.form;
        var out = form && form.querySelector('input[data-dp-out]');
        if (!out) return;

        // ป้ายจำนวนคืน (อยู่ใต้ช่องเช็คเอาท์)
        var badge = document.createElement('span');
        badge.className = 'dp-nights';
        badge.setAttribute('aria-live', 'polite');
        var outLabel = out.closest('label') || out.parentNode;
        outLabel.classList.add('dp-has-nights');
        outLabel.insertBefore(badge, out);
        function update() {
            var n = nightsBetween(inp.value, out.value);
            badge.textContent = n > 0 ? n + ' คืน' : '';
        }

        // ---- ช่องวันที่ของเครื่อง: เลื่อนวันออกตามวันเข้า ----
        function syncNative() {
            if (inp.value) {
                var min = addDays(inp.value, 1);
                out.min = min;
                if (!out.value || out.value < min) out.value = min;
            }
            update();
        }
        inp.addEventListener('change', syncNative);
        out.addEventListener('change', function () {
            update();
            if (!hasFp && inp.getAttribute('data-dp-refresh') && inp.value && out.value > inp.value) {
                var b = document.querySelector(inp.getAttribute('data-dp-refresh'));
                if (b) { if (form.requestSubmit) form.requestSubmit(b); else b.click(); }
            }
        });
        update();

        if (!hasFp) return;

        // ---- จอคอม: ปฏิทินช่วงวัน ----
        var booked = {};
        var msg = document.createElement('span');
        msg.className = 'dp-msg';
        msg.setAttribute('role', 'alert');
        (inp.closest('label') || inp.parentNode).appendChild(msg);
        var note = document.createElement('span');   // ข้อความแจ้ง (ไม่ใช่ข้อผิดพลาด)
        note.className = 'dp-note';
        note.setAttribute('role', 'status');
        (inp.closest('label') || inp.parentNode).appendChild(note);

        // ช่องแสดงผลแทนช่องเดิม (ช่องเดิมยังอยู่ในฟอร์มและส่งค่า yyyy-MM-dd เหมือนเดิม)
        var makeDisplay = makeDisplayFor;
        var dIn = makeDisplay(inp), dOut = makeDisplay(out);
        function paint() { dIn.value = pretty(inp.value); dOut.value = pretty(out.value); update(); }

        var anchor = document.createElement('input');   // ตัวยึดของ flatpickr (ไม่แสดงผล)
        anchor.type = 'hidden';
        anchor.className = 'dp-anchor';
        anchor.setAttribute('aria-hidden', 'true');
        anchor.setAttribute('aria-label', 'ช่วงวันเข้าพัก');
        anchor.setAttribute('tabindex', '-1');
        form.appendChild(anchor);

        function rangeHasBooked(a, b) {
            for (var d = a; d < b; d = addDays(d, 1)) if (booked[d]) return true;
            return false;
        }

        var fp = window.flatpickr(anchor, {
            locale: 'th',
            mode: 'range',
            showMonths: desktop ? 2 : 1,
            dateFormat: 'Y-m-d',
            minDate: inp.min || 'today',
            maxDate: inp.max || new Date().fp_incr(400),
            clickOpens: false,
            positionElement: dIn,
            ignoredFocusElements: [dIn, dOut],
            // วางปฏิทินไว้นอก <body>: ผู้ใช้ที่ขยายตัวอักษรในหน้า "ตั้งค่า" (CSS zoom ที่ body) ปฏิทินจะไม่เลื่อนเพี้ยนจากช่องวันที่
            appendTo: document.documentElement,     // คลิกช่องแสดงวันที่ไม่ถือว่าเป็นการคลิกนอกปฏิทิน (กันปฏิทินปิด-เปิดกระพริบ)
            defaultDate: inp.value && out.value ? [inp.value, out.value] : null,
            disableMobile: true,
            onDayCreate: function (_, __, ___, el) {
                if (booked[iso(el.dateObj)]) { el.classList.add('is-booked'); el.title = 'ห้องไม่ว่างคืนนี้'; }
            },
            onChange: function (sel) {
                if (!sel.length) return;          // fp.clear() เรียก onChange ซ้ำ: อย่าลบข้อความเตือน
                msg.textContent = '';
                if (sel.length === 1 && booked[iso(sel[0])]) {
                    msg.textContent = 'ห้องไม่ว่างในคืนที่เลือก กรุณาเลือกวันเข้าพักใหม่';
                    fp.clear();
                    return;
                }
                if (sel.length !== 2) return;
                var a = iso(sel[0]), b = iso(sel[1]);
                if (a === b) b = addDays(a, 1);            // คลิกวันเดียวซ้ำ = 1 คืน
                if (rangeHasBooked(a, b)) {
                    msg.textContent = 'ช่วงที่เลือกมีคืนที่ห้องไม่ว่าง กรุณาเลือกช่วงอื่น';
                    fp.clear();
                    return;
                }
                inp.value = a; out.value = b;
                out.min = addDays(a, 1);
                paint();
                fp.close();
                refresh();
            },
            onClose: function (sel) {
                // ปิดก่อนเลือกครบ 2 วัน: คืนค่าช่วงเดิมไว้ในปฏิทิน
                if (sel.length !== 2 && inp.value && out.value) fp.setDate([inp.value, out.value], false);
            }
        });

        function openAt() { fp.open(); }
        [dIn, dOut].forEach(function (el) {
            el.addEventListener('click', function () { openAt(el); });
            el.addEventListener('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ' || e.key === 'ArrowDown') { e.preventDefault(); openAt(el); }
                if (e.key === 'Escape') fp.close();
            });
        });
        paint();

        // หน้าจอง: เลือกช่วงวันเสร็จแล้วอัปเดตรายการห้องว่างให้ทันที (ไม่ต้องกดปุ่มเอง)
        function refresh() {
            var sel = inp.getAttribute('data-dp-refresh');
            var btn = sel && document.querySelector(sel);
            if (!btn) return;
            if (form.requestSubmit) form.requestSubmit(btn); else btn.click();
        }

        // ช่วงที่ตั้งไว้ทับคืนที่ไม่ว่าง: เลื่อนไปคืนว่างถัดไปให้ (จำนวนคืนเท่าเดิมถ้าทำได้)
        function nextFree(from, nights) {
            var limit = addDays(from, 400);
            for (var d = from; d < limit; d = addDays(d, 1)) {
                if (!rangeHasBooked(d, addDays(d, nights))) return d;
            }
            return null;
        }

        // ★ [จุดเชื่อมต่อส่วนหน้า-ส่วนหลัง 2/4] — ฝั่ง JavaScript (ส่วนหน้า)
        // อ่าน URL จาก data-dp-booked แล้วใช้ fetch (AJAX) ยิง HTTP GET ไปที่ส่วนหลัง ขอผลเป็น JSON (Accept: application/json)
        // credentials: 'same-origin' = ส่งคุกกี้ไปด้วย · ได้ ["2026-12-01", ...] กลับมา → เก็บใน booked แล้ววาดปฏิทินใหม่ (fp.redraw)
        // ทำไมใช้ AJAX: ลูกค้าเห็นคืนที่เต็มทันทีโดยไม่ต้องรีโหลดหน้า และถ้าวันที่ตั้งไว้ชนคืนที่เต็มจะเลื่อนไปคืนว่างให้เอง
        var url = inp.getAttribute('data-dp-booked');
        if (url && window.fetch) {
            fetch(url, { headers: { 'Accept': 'application/json' }, credentials: 'same-origin' })
                .then(function (r) { return r.ok ? r.json() : []; })
                .then(function (list) {
                    (list || []).forEach(function (d) { booked[d] = true; });
                    fp.redraw();
                    if (inp.value && out.value && rangeHasBooked(inp.value, out.value)) {
                        var n = Math.max(1, nightsBetween(inp.value, out.value));
                        var start = nextFree(inp.value, n);
                        if (!start) { n = 1; start = nextFree(inp.value, 1); }
                        if (start) {
                            inp.value = start; out.value = addDays(start, n); out.min = addDays(start, 1);
                            fp.setDate([inp.value, out.value], false);
                            paint();
                            note.textContent = 'ห้องนี้ไม่ว่างในวันที่ตั้งไว้ — เลือกคืนว่างถัดไปให้แล้ว เปลี่ยนได้ที่ปฏิทิน';
                        } else {
                            msg.textContent = 'ห้องนี้ไม่ว่างในช่วง 400 วันข้างหน้า';
                        }
                    }
                })
                .catch(function () { /* ดึงไม่ได้ก็ยังเลือกวันได้ ระบบตรวจห้องว่างซ้ำตอนจองอยู่แล้ว */ });
        }
    });

    // ---- Part 45: ช่องวันที่เดี่ยวทุกช่อง ใช้ปฏิทินเดียวกัน ----
    if (!hasFp) return;
    document.querySelectorAll('input[type="date"]:not([data-dp-in]):not([data-dp-out]):not([data-dp-native])').forEach(function (src) {
        if (src.hidden || src.disabled) return;
        var readonly = src.readOnly;
        var disp = makeDisplayFor(src);
        var anchor = document.createElement('input');
        anchor.type = 'hidden';
        anchor.className = 'dp-anchor';
        anchor.setAttribute('aria-hidden', 'true');
        anchor.setAttribute('tabindex', '-1');
        disp.insertAdjacentElement('afterend', anchor);
        function paint() { disp.value = pretty(src.value); if (clr) clr.hidden = !src.value; }
        var clr = null;
        if (!readonly && src.dataset.dpRequired !== '1') {
            clr = document.createElement('button');
            clr.type = 'button';
            clr.className = 'dp-clear';
            clr.textContent = 'ล้าง';
            clr.setAttribute('aria-label', 'ล้างวันที่');
            anchor.insertAdjacentElement('afterend', clr);
            clr.addEventListener('click', function () {
                src.value = ''; fp.clear(); paint();
                src.dispatchEvent(new Event('change', { bubbles: true }));
            });
        }
        if (readonly) { disp.classList.add('is-readonly'); paint(); return; }
        var fp = window.flatpickr(anchor, {
            locale: 'th',
            dateFormat: 'Y-m-d',
            minDate: src.min || null,
            maxDate: src.max || null,
            defaultDate: src.value || null,
            clickOpens: false,
            positionElement: disp,
            ignoredFocusElements: [disp],
            appendTo: document.documentElement,
            disableMobile: true,
            onChange: function (sel) {
                if (!sel.length) return;
                src.value = iso(sel[0]);
                paint();
                fp.close();
                src.dispatchEvent(new Event('change', { bubbles: true }));
                src.dispatchEvent(new Event('input', { bubbles: true }));
            }
        });
        // วันเกิด/วันครบรอบ: เปิดปฏิทินที่ปีที่น่าจะเลือก (เปลี่ยนปีได้ที่หัวปฏิทิน)
        var start = src.getAttribute('data-dp-start');
        function openFp() { fp.open(); if (!src.value && start) fp.jumpToDate(start); }
        disp.addEventListener('click', openFp);
        disp.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' || e.key === ' ' || e.key === 'ArrowDown') { e.preventDefault(); openFp(); }
            if (e.key === 'Escape') fp.close();
            if ((e.key === 'Delete' || e.key === 'Backspace') && clr) { e.preventDefault(); clr.click(); }
        });
        // ฟอร์มที่บังคับกรอกวันที่: ตรวจก่อนส่ง (เพราะช่องจริงถูกซ่อน เบราว์เซอร์ตรวจให้ไม่ได้)
        var f = src.form;
        if (f && src.dataset.dpRequired === '1') {
            f.addEventListener('submit', function (e) {
                if (!src.value) { e.preventDefault(); e.stopImmediatePropagation(); req.textContent = 'กรุณาเลือกวันที่'; disp.classList.add('is-invalid'); disp.focus(); fp.open(); }
            }, true);
            var req = document.createElement('span');
            req.className = 'dp-msg';
            req.setAttribute('role', 'alert');
            (clr || anchor).insertAdjacentElement('afterend', req);
            src.addEventListener('change', function () { if (src.value) { req.textContent = ''; disp.classList.remove('is-invalid'); } });
        }
        paint();
    });
})();

/* ---- หน้ารายละเอียดห้อง: เลือกช่วงเข้าพักจากปฏิทินวันว่าง (แตะวันเช็คอิน → แตะวันเช็คเอาท์) ----
   ไม่มี JS ก็ยังใช้ได้: ลิงก์แต่ละวันจะจอง 1 คืนตามเดิม */
(function () {
    var box = document.querySelector('[data-rd-pick]');
    if (!box) return;
    var days = Array.prototype.slice.call(document.querySelectorAll('.rd-grid a.rd-free[data-d]'));
    if (!days.length) return;
    var byDate = {}; days.forEach(function (a) { byDate[a.dataset.d] = a; });
    var title = box.querySelector('[data-rd-pick-title]'), sub = box.querySelector('[data-rd-pick-sub]');
    var go = box.querySelector('[data-rd-pick-go]'), clr = box.querySelector('[data-rd-pick-clear]');
    var mon = ['ม.ค.', 'ก.พ.', 'มี.ค.', 'เม.ย.', 'พ.ค.', 'มิ.ย.', 'ก.ค.', 'ส.ค.', 'ก.ย.', 'ต.ค.', 'พ.ย.', 'ธ.ค.'];
    var start = null, end = null;
    function parse(s) { var p = s.split('-'); return new Date(+p[0], +p[1] - 1, +p[2]); }
    function iso(d) { return d.getFullYear() + '-' + ('0' + (d.getMonth() + 1)).slice(-2) + '-' + ('0' + d.getDate()).slice(-2); }
    function nice(s) { var d = parse(s); return d.getDate() + ' ' + mon[d.getMonth()]; }
    function add(s, n) { var d = parse(s); d.setDate(d.getDate() + n); return iso(d); }
    function money(n) { return '฿' + Math.round(n).toLocaleString('th-TH'); }
    function nightsBetween(a, b) { var out = [], d = a; while (d < b) { out.push(d); d = add(d, 1); } return out; }
    function paint() {
        days.forEach(function (a) { a.classList.remove('is-start', 'is-end', 'is-in'); a.removeAttribute('aria-pressed'); });
        Array.prototype.forEach.call(document.querySelectorAll('.rd-busy.is-end'), function (s) { s.classList.remove('is-end'); });
        if (!start) { box.hidden = true; return; }
        var co = end || add(start, 1), nights = nightsBetween(start, co), sum = 0;
        nights.forEach(function (d, i) { var a = byDate[d]; if (!a) return; sum += parseFloat(a.dataset.p) || 0; a.classList.add(i === 0 ? 'is-start' : 'is-in'); a.setAttribute('aria-pressed', 'true'); });
        if (end && byDate[end]) byDate[end].classList.add('is-end');
        var u = new URL(byDate[start].href, location.href);
        u.searchParams.set('checkIn', start); u.searchParams.set('checkOut', co);
        go.href = u.pathname + u.search;
        title.textContent = nice(start) + ' → ' + nice(co) + ' · ' + nights.length + ' คืน';
        sub.textContent = end ? 'รวมประมาณ ' + money(sum) + ' (ยังไม่รวมส่วนลด)' : 'แตะวันเช็คเอาท์ หรือกด “จองช่วงนี้” เพื่อจอง 1 คืน (' + money(sum) + ')';
        box.hidden = false;
    }
    days.forEach(function (a) {
        a.addEventListener('click', function (e) {
            e.preventDefault();
            var d = a.dataset.d;
            if (start && !end && d > start) {
                // ทุกคืนในช่วง (ยกเว้นวันเช็คเอาท์) ต้องว่าง
                var ok = nightsBetween(start, d).every(function (n) { return byDate[n]; });
                if (ok) { end = d; paint(); return; }
                sub.textContent = 'ช่วงนี้มีคืนที่ถูกจองแล้ว — เริ่มเลือกใหม่จากวันนี้';
            }
            start = d; end = null; paint();
        });
    });
    // วันที่มีผู้จองแล้วใช้เป็น "วันเช็คเอาท์" ได้ (คืนก่อนหน้าว่าง)
    Array.prototype.forEach.call(document.querySelectorAll('.rd-grid .rd-busy[data-co]'), function (s) {
        if (!s.dataset.co) return;
        s.addEventListener('click', function () {
            var d = s.dataset.co;
            if (start && !end && d > start && nightsBetween(start, d).every(function (n) { return byDate[n]; })) {
                end = d; paint(); s.classList.add('is-end');
            }
        });
    });
    clr.addEventListener('click', function () { start = end = null; paint(); });
})();
