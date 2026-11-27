/* Part 48: ต่อยอดหน้าเว็บด้วยไลบรารีโอเพนซอร์ส (ไฟล์อยู่ใน wwwroot/lib ทั้งหมด ไม่โหลดจากเน็ต)
   · Notyf      — แถบแจ้งผล (บันทึกแล้ว / ผิดพลาด) เด้งมุมจอ
   · GLightbox  — กดรูปห้อง/บริการ/สลิป เพื่อดูเต็มจอ
   · Tom Select — ช่องเลือกสวยขึ้น + ค้นหาลูกค้าด้วยชื่อ/อีเมล/เบอร์
   · Chart.js   — กราฟแดชบอร์ดและรายงานแบบชี้ดูตัวเลขได้
   ทุกส่วนเป็น "ส่วนเสริม": ถ้าไลบรารีโหลดไม่ได้หรือปิด JS หน้าเว็บยังใช้งานได้ครบเหมือนเดิม */
(function () {
    'use strict';
    var reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches
        || document.documentElement.getAttribute('data-motion') === 'reduce';
    function each(sel, fn) { Array.prototype.forEach.call(document.querySelectorAll(sel), fn); }
    function safe(name, fn) { try { fn(); } catch (e) { if (window.console) console.warn('[enhance] ' + name, e); } }

    /* ---------------- 1) Notyf: แถบแจ้งผลเด้งมุมจอ ---------------- */
    safe('notyf', function () {
        if (!window.Notyf) return;
        var flashes = document.querySelectorAll('.cx-alert[data-toast]');
        var mobile = window.matchMedia('(max-width: 640px)').matches;
        // แถบการ์ดสีขาว ขีดสีด้านซ้าย หัวข้อ + ข้อความ + แถบนับเวลาถอยหลัง (ไม่ใช้ ripple ของ Notyf ที่ทำให้พื้นหลังสีเพี้ยน)
        var notyf = new Notyf({
            duration: 5500, dismissible: true, ripple: false,
            position: { x: mobile ? 'center' : 'right', y: 'top' },
            types: [
                { type: 'success', background: '#fff', className: 'as-toast as-toast--ok', icon: { className: 'as-toast__ico', tagName: 'span', text: '' } },
                { type: 'error', background: '#fff', duration: 8000, className: 'as-toast as-toast--err', icon: { className: 'as-toast__ico', tagName: 'span', text: '' } }
            ]
        });
        var escT = function (t) { return String(t).replace(/[<>&"]/g, function (c) { return { '<': '&lt;', '>': '&gt;', '&': '&amp;', '"': '&quot;' }[c]; }); };
        // ข้อความเดิมที่ผู้ใช้กดปิดไปแล้ว ไม่เด้งซ้ำภายใน 2 นาที (กันแถบเดิมเด้งกลับมาจากการเช็คงานใหม่ทุก 5 วินาที)
        var closed = {};
        var show = function (ok, text) {
            var key = (ok ? '1' : '0') + text;
            if (closed[key] && Date.now() - closed[key] < 120000) return;
            var t = notyf.open({ type: ok ? 'success' : 'error',
                message: '<b class="as-toast__t">' + (ok ? 'เรียบร้อย' : 'ทำรายการไม่สำเร็จ') + '</b><span class="as-toast__m">' + escT(text) + '</span>' });
            if (!t || !t.on) return;
            // กดที่ตัวแถบตรงไหนก็ได้ = ปิด (ไม่ต้องเล็งปุ่ม × เล็ก ๆ) · กดปิดแล้วจำไว้ ไม่เด้งข้อความเดิมซ้ำ
            t.on('click', function () { closed[key] = Date.now(); notyf.dismiss(t); });
            t.on('dismiss', function () { closed[key] = Date.now(); });
        };
        // ให้ส่วนอื่นเรียกใช้แถบแจ้งผลได้ (เช่น กดเก็บคูปอง)
        window.asToast = function (ok, text) { show(ok, text); };
        Array.prototype.forEach.call(flashes, function (el) {
            var text = (el.textContent || '').trim();
            if (!text) return;
            show(!el.classList.contains('cx-alert--error'), text);
            // ข้อความเดิมยังอยู่ในหน้า (โปรแกรมอ่านจอ/พิมพ์หน้า) แต่ซ่อนจากสายตาเพื่อไม่ให้ซ้ำกับแถบที่เด้ง
            el.classList.add('is-toasted');
        });
    });

    /* ---------------- 2) GLightbox: ดูรูปเต็มจอ ---------------- */
    safe('glightbox', function () {
        if (!window.GLightbox) return;
        var imgs = Array.prototype.slice.call(document.querySelectorAll('img[data-zoom]'));
        function open(elements, start) {
            var lb = GLightbox({ elements: elements, touchNavigation: true, loop: elements.length > 1, openEffect: reduceMotion ? 'none' : 'zoom', closeEffect: reduceMotion ? 'none' : 'fade', moreLength: 0 });
            lb.openAt(start || 0);
        }
        var gallery = imgs.map(function (img) { return { href: img.currentSrc || img.src, type: 'image', title: img.getAttribute('data-zoom-title') || img.alt || '' }; });
        imgs.forEach(function (img, i) {
            img.classList.add('is-zoomable');
            if (!img.alt) img.alt = gallery[i].title || 'รูปภาพ';   // รูปตกแต่ง (alt ว่าง) ที่กดได้ ต้องมีชื่อ
            img.setAttribute('tabindex', '0'); img.setAttribute('role', 'button');
            img.setAttribute('aria-label', 'ดูรูปเต็มจอ' + (gallery[i].title ? ': ' + gallery[i].title : ''));
            img.addEventListener('click', function (e) { e.preventDefault(); open(gallery, i); });
            img.addEventListener('keydown', function (e) { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); open(gallery, i); } });
        });
        each('a[data-zoom-link]', function (a) {
            a.addEventListener('click', function (e) {
                if (e.ctrlKey || e.metaKey || e.shiftKey || e.button === 1) return;   // เปิดแท็บใหม่ได้ตามปกติ
                e.preventDefault();
                open([{ href: a.href, type: 'image', title: a.getAttribute('data-zoom-title') || '' }], 0);
            });
        });
    });

    /* ---------------- 3) Tom Select: ช่องเลือก + ค้นหาลูกค้า ---------------- */
    safe('tomselect', function () {
        if (!window.TomSelect) return;
        var emailRe = /^[^\s@,;]+@[^\s@,;]+\.[^\s@,;]+$/;
        var esc = function (s) { return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); };
        var common = { maxOptions: null, hideSelected: true, closeAfterSelect: true };
        // ชื่อของช่อง (สำหรับโปรแกรมอ่านจอ): aria-label เดิม หรือข้อความของ <label> ที่ครอบอยู่ (ไม่รวมตัวเลือกข้างใน)
        function nameOf(el) {
            var n = el.getAttribute('aria-label');
            if (n) return n;
            var lb = el.labels && el.labels[0];
            if (!lb) return el.getAttribute('placeholder') || el.name || '';
            var c = lb.cloneNode(true);
            Array.prototype.forEach.call(c.querySelectorAll('select, input, textarea, .ts-wrapper, small'), function (x) { x.remove(); });
            return c.textContent.replace(/\s+/g, ' ').trim();
        }
        function label(ts, el) {
            var n = nameOf(el); if (!n) return;
            if (ts.control_input) ts.control_input.setAttribute('aria-label', n);
            if (ts.control && ts.control.getAttribute('role') === 'combobox') ts.control.setAttribute('aria-label', n);
            if (ts.focus_node) ts.focus_node.setAttribute('aria-label', n);
        }

        // ทุกช่องเลือกแบบเลือกได้ค่าเดียวใช้ดรอปดาวน์ธีมเดียวกันทั้งระบบ (ยกเว้น multiple / size / data-native)
        each('select[data-ts], select:not([multiple]):not([size]):not([data-native])', function (el) {
            if (el.tomselect || el.closest('[data-native]')) return;
            var many = el.options.length > 8;
            var ts = new TomSelect(el, Object.assign({}, common, {
                create: false, allowEmptyOption: true, hideSelected: false, controlInput: many ? undefined : null,
                dropdownParent: 'body', maxItems: 1,
                placeholder: many ? 'พิมพ์เพื่อค้นหา…' : undefined,
                render: { no_results: function () { return '<div class="no-results">ไม่พบรายการ</div>'; } }
            }));
            label(ts, el);
        });

        function customerOptions(el, multi) {
            var url = el.getAttribute('data-lookup');
            return Object.assign({}, common, {
                valueField: 'email', labelField: 'email', searchField: ['email', 'name', 'phone'],
                maxItems: multi ? null : 1, delimiter: ',', persist: false, createOnBlur: true,
                create: function (input) { input = input.trim(); return emailRe.test(input) ? { email: input, name: '', phone: '' } : false; },
                createFilter: function (input) { return emailRe.test(input.trim()); },
                loadThrottle: 250,
                shouldLoad: function (q) { return q.trim().length >= 2; },
                load: function (q, cb) {
                    fetch(url + '?q=' + encodeURIComponent(q.trim()), { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
                        .then(function (r) { return r.ok ? r.json() : []; }).then(cb).catch(function () { cb(); });
                },
                placeholder: multi ? 'พิมพ์ชื่อ อีเมล หรือเบอร์ลูกค้า…' : 'พิมพ์ชื่อ อีเมล หรือเบอร์เพื่อค้นหาลูกค้า…',
                render: {
                    option: function (d) {
                        return '<div class="ts-cust"><b>' + esc(d.name || d.email) + '</b><span>' + esc(d.email) + (d.phone ? ' · ' + esc(d.phone) : '') + '</span></div>';
                    },
                    item: function (d) { return '<div title="' + esc(d.name || '') + '">' + esc(d.email) + '</div>'; },
                    option_create: function (d) { return '<div class="create">ใช้อีเมล <b>' + esc(d.input) + '</b> (ลูกค้าใหม่)</div>'; },
                    no_results: function () { return '<div class="no-results">ไม่พบลูกค้า — พิมพ์อีเมลให้ครบเพื่อใช้อีเมลใหม่</div>'; },
                    loading: function () { return '<div class="ts-loading">กำลังค้นหา…</div>'; }
                }
            });
        }
        each('input[data-ts-customer]', function (el) {
            if (el.tomselect) return;
            el.type = 'text';   // ช่องเดิมเป็น type=email: ให้ Tom Select คุมรูปแบบอีเมลแทน (เซิร์ฟเวอร์ตรวจซ้ำอยู่แล้ว)
            label(new TomSelect(el, customerOptions(el, false)), el);
        });
        each('input[data-ts-emails]', function (el) {
            if (el.tomselect) return;
            label(new TomSelect(el, customerOptions(el, true)), el);
        });
    });

    /* ---------------- 4) Chart.js: กราฟแดชบอร์ด/รายงาน ---------------- */
    safe('charts', function () {
        if (!window.Chart) return;
        var C = window.Chart;
        var bodyFont = getComputedStyle(document.body).fontFamily;
        C.defaults.font.family = bodyFont; C.defaults.font.size = 12; C.defaults.color = '#5b5468';
        if (reduceMotion) C.defaults.animation = false; else { C.defaults.animation.duration = 700; C.defaults.animation.easing = 'easeOutQuart'; }
        var th = function (v) { return Number(v).toLocaleString('th-TH', { maximumFractionDigits: 1 }); };
        function fmt(v, unit) { return unit === '฿' ? '฿' + th(v) : unit === '%' ? th(v) + '%' : th(v) + ' ' + unit; }
        function axis(v, unit) { if (unit === '฿') return v >= 1000 ? '฿' + th(v / 1000) + 'k' : '฿' + th(v); return unit === '%' ? v + '%' : v; }
        function grad(ctx, area, top, bottom) {
            if (!area) return top;
            var g = ctx.createLinearGradient(0, area.top, 0, area.bottom); g.addColorStop(0, top); g.addColorStop(1, bottom); return g;
        }
        var tooltip = {
            backgroundColor: '#2d1b47', titleColor: '#e3c57e', bodyColor: '#fff', padding: 10, cornerRadius: 10, displayColors: true, boxPadding: 4,
            titleFont: { family: bodyFont, weight: '600' }, bodyFont: { family: bodyFont }
        };
        var centerText = {
            id: 'asCenter',
            afterDraw: function (chart, args, opts) {
                if (!opts || opts.value == null) return;
                var m = chart.getDatasetMeta(0); if (!m || !m.data.length) return;
                var x = m.data[0].x, y = m.data[0].y, ctx = chart.ctx;
                ctx.save(); ctx.textAlign = 'center'; ctx.fillStyle = '#2d1b47';
                ctx.font = '700 24px ' + bodyFont; ctx.fillText(String(opts.value), x, y + 4);
                ctx.font = '500 12px ' + bodyFont; ctx.fillStyle = '#6b6478'; ctx.fillText(opts.label || '', x, y + 22);
                ctx.restore();
            }
        };

        each('script[data-chart]', function (s) {
            var d; try { d = JSON.parse(s.textContent); } catch (e) { return; }
            if (!d || !d.labels || !d.labels.length) return;
            var host = s.parentElement, label = '';
            // ซ่อนกราฟ SVG เดิม (ยังเป็นสำรองเมื่อไม่มี JS) แล้ววาดกราฟใหม่แทนที่ตำแหน่งเดิม
            var olds = [];
            if (s.getAttribute('data-replace')) s.getAttribute('data-replace').split(',').forEach(function (sel) { var o = host.querySelector(sel.trim()); if (o) olds.push(o); });
            else if (s.previousElementSibling && s.previousElementSibling.tagName.toLowerCase() === 'svg') olds.push(s.previousElementSibling);
            olds.forEach(function (o) { if (!label) label = o.getAttribute('aria-label') || ''; o.style.display = 'none'; o.setAttribute('aria-hidden', 'true'); });

            var wrap = document.createElement('div');
            wrap.className = 'as-chart as-chart--' + d.kind;
            wrap.style.height = (s.getAttribute('data-height') || (d.kind === 'doughnut' ? 210 : 240)) + 'px';
            var canvas = document.createElement('canvas');
            canvas.setAttribute('role', 'img');
            canvas.setAttribute('aria-label', label || (d.series[0].name + ': ' + d.labels.map(function (l, i) { return l + ' ' + fmt(d.series[0].data[i], d.unit); }).join(', ')));
            wrap.appendChild(canvas); host.insertBefore(wrap, s);

            var unit = d.unit || '', cfg;
            if (d.kind === 'doughnut') {
                var hasLegend = !!host.parentElement && !!host.querySelector('.db-legend');
                cfg = {
                    type: 'doughnut',
                    data: { labels: d.labels, datasets: [{ data: d.series[0].data, backgroundColor: d.series[0].colors, borderColor: '#fff', borderWidth: 3, hoverOffset: 8, borderRadius: 4 }] },
                    options: {
                        cutout: '68%', maintainAspectRatio: false, responsive: true,
                        plugins: {
                            legend: { display: !hasLegend, position: 'right', labels: { usePointStyle: true, pointStyle: 'circle', padding: 14 } },
                            tooltip: Object.assign({}, tooltip, { callbacks: { label: function (c) { return ' ' + c.label + ': ' + fmt(c.parsed, unit); } } }),
                            asCenter: d.center != null ? { value: d.center, label: d.centerLabel } : {}
                        }
                    },
                    plugins: [centerText]
                };
            } else {
                var stacked = d.kind === 'stacked', line = d.kind === 'line';
                var sets = d.series.map(function (se, si) {
                    var n = se.data.length;
                    return {
                        type: line ? 'line' : 'bar', label: se.name, data: se.data, order: 2,
                        borderRadius: line ? 0 : 6, borderSkipped: false, maxBarThickness: 30,
                        borderColor: se.color, borderWidth: line ? 2.5 : 0, tension: 0.35, pointRadius: 0, pointHoverRadius: 5,
                        pointBackgroundColor: '#fff', pointBorderColor: se.color, fill: line,
                        backgroundColor: function (ctx) {
                            var ch = ctx.chart;
                            if (line) return grad(ch.ctx, ch.chartArea, 'rgba(95,61,142,.28)', 'rgba(95,61,142,0)');
                            if (d.highlightLast && ctx.dataIndex === n - 1) return '#c9a14a';
                            return si === 0 ? grad(ch.ctx, ch.chartArea, '#7a5c9e', '#45296b') : se.color;
                        },
                        hoverBackgroundColor: si === 0 && !line ? '#c9a14a' : undefined
                    };
                });
                if (d.cap != null) sets.push({
                    type: 'line', label: (d.capLabel || 'ทั้งหมด') + ' ' + d.cap, data: d.labels.map(function () { return d.cap; }),
                    borderColor: '#b08a35', borderDash: [6, 5], borderWidth: 1.5, pointRadius: 0, pointHoverRadius: 0, fill: false, order: 1
                });
                cfg = {
                    type: line ? 'line' : 'bar',
                    data: { labels: d.labels, datasets: sets },
                    options: {
                        maintainAspectRatio: false, responsive: true, interaction: { mode: 'index', intersect: false },
                        scales: {
                            x: { stacked: stacked, grid: { display: false }, ticks: { maxRotation: 0, autoSkip: true, autoSkipPadding: 14 } },
                            y: {
                                stacked: stacked, beginAtZero: true, max: d.max, suggestedMax: d.cap != null ? d.cap + 1 : undefined,
                                grid: { color: 'rgba(45,27,71,.07)' }, border: { display: false },
                                ticks: { precision: 0, callback: function (v) { return axis(v, unit); } }
                            }
                        },
                        plugins: {
                            legend: { display: sets.length > 1, position: 'bottom', labels: { usePointStyle: true, pointStyle: 'rectRounded', padding: 16 } },
                            tooltip: Object.assign({}, tooltip, { callbacks: { label: function (c) { return ' ' + c.dataset.label + ': ' + fmt(c.parsed.y, unit); } } })
                        }
                    }
                };
            }
            new C(canvas, cfg);
        });
    });
})();

/* ---- Part 49: คูปองกดเก็บ · นับถอยหลัง · สไลด์แบนเนอร์ ---- */
(function () {
    'use strict';
    function toast(ok, msg) { if (window.asToast) window.asToast(ok, msg); else window.alert(msg); }
    function post(url, tokenValue, id) {
        var body = new URLSearchParams(); body.set('id', id); body.set('__RequestVerificationToken', tokenValue);
        return fetch(url, { method: 'POST', credentials: 'same-origin', headers: { 'Accept': 'application/json', 'Content-Type': 'application/x-www-form-urlencoded' }, body: body })
            .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); });
    }
    // การ์ดคูปอง (หน้าโปร/หน้าแรก): เก็บโดยไม่ต้องโหลดหน้าใหม่ · ถ้า JS/เครือข่ายมีปัญหา ฟอร์มปกติยังทำงาน
    Array.prototype.forEach.call(document.querySelectorAll('form.cp-claim'), function (f) {
        f.addEventListener('submit', function (e) {
            if (!window.fetch) return;
            e.preventDefault();
            var btn = f.querySelector('button'), tok = f.querySelector('input[name=__RequestVerificationToken]');
            if (!tok) { f.submit(); return; }
            btn.disabled = true; btn.textContent = 'กำลังเก็บ…';
            post(f.action, tok.value, f.querySelector('input[name=id]').value).then(function (j) {
                toast(j.ok, j.message);
                if (!j.ok) { btn.disabled = false; btn.textContent = 'เก็บคูปอง'; return; }
                var card = f.closest('.cp-card');
                if (card) {
                    card.classList.add('is-claimed');
                    var tag = card.querySelector('.cp-tag'); if (tag) { tag.textContent = 'เก็บแล้ว · ใช้ให้อัตโนมัติ'; }
                    var cd = card.querySelector('.cp-countdown'); if (cd) cd.remove();
                }
                var done = document.createElement('span'); done.className = 'cp-done'; done.textContent = '✓ เก็บแล้ว';
                f.replaceWith(done);
            }).catch(function () { f.submit(); });
        });
    });
    // หน้าจอง: กด "เก็บและใช้" → เก็บคูปอง แล้วคำนวณราคาใหม่ทันที
    Array.prototype.forEach.call(document.querySelectorAll('[data-claim-inline]'), function (b) {
        b.addEventListener('click', function () {
            var form = b.closest('form'), tok = form && form.querySelector('input[name=__RequestVerificationToken]');
            if (!tok) return;
            b.disabled = true; b.textContent = 'กำลังเก็บ…';
            post(b.getAttribute('data-claim-url'), tok.value, b.getAttribute('data-claim-inline')).then(function (j) {
                toast(j.ok, j.message);
                if (!j.ok) { b.disabled = false; b.textContent = 'เก็บและใช้'; return; }
                if (form.requestSubmit) form.requestSubmit(); else form.submit();
            }).catch(function () { b.disabled = false; b.textContent = 'เก็บและใช้'; toast(false, 'เก็บคูปองไม่สำเร็จ กรุณาลองใหม่'); });
        });
    });
    // นับถอยหลังเวลาเก็บคูปอง
    var cds = Array.prototype.slice.call(document.querySelectorAll('[data-countdown]'));
    if (cds.length) {
        var pad = function (n) { return (n < 10 ? '0' : '') + n; };
        var tick = function () {
            var now = Date.now();
            cds.forEach(function (el) {
                var t = Date.parse(el.getAttribute('data-countdown')); if (isNaN(t)) return;
                var s = Math.max(0, Math.floor((t - now) / 1000));
                if (s <= 0) { el.textContent = el.getAttribute('data-cd-label') === 'เปิดเก็บใน' ? 'เปิดให้เก็บแล้ว — รีเฟรชหน้า' : 'หมดเวลาเก็บแล้ว'; return; }
                var d = Math.floor(s / 86400), h = Math.floor(s % 86400 / 3600), m = Math.floor(s % 3600 / 60), sec = s % 60;
                el.textContent = el.getAttribute('data-cd-label') + ' ' + (d > 0 ? d + ' วัน ' : '') + pad(h) + ':' + pad(m) + ':' + pad(sec);
            });
        };
        tick(); setInterval(tick, 1000);
    }
    // สไลด์แบนเนอร์หน้าแรก (เลื่อนด้วยปุ่ม/ปัด/ล้อเมาส์แนวนอน — ไม่เลื่อนเอง)
    Array.prototype.forEach.call(document.querySelectorAll('[data-slider]'), function (track) {
        var sec = track.closest('section'); if (!sec) return;
        Array.prototype.forEach.call(sec.querySelectorAll('[data-slide]'), function (btn) {
            btn.addEventListener('click', function () {
                track.scrollBy({ left: (+btn.getAttribute('data-slide')) * Math.max(280, track.clientWidth * 0.8), behavior: 'smooth' });
            });
        });
        var upd = function () {
            var max = track.scrollWidth - track.clientWidth - 2;
            Array.prototype.forEach.call(sec.querySelectorAll('[data-slide]'), function (b) {
                b.disabled = (+b.getAttribute('data-slide') < 0) ? track.scrollLeft <= 2 : track.scrollLeft >= max;
            });
            sec.classList.toggle('no-overflow', max <= 0);
        };
        track.addEventListener('scroll', upd, { passive: true }); window.addEventListener('resize', upd); upd();
    });
})();

/* ---- Part 50/56: ตัวเลขสด — งานใหม่ของพนักงาน / การแจ้งเตือนใหม่ของลูกค้า (ถามเซิร์ฟเวอร์ทุก 5 วินาทีเมื่อเปิดแท็บอยู่) ---- */
(function () {
    'use strict';
    var url = document.body && document.body.getAttribute('data-live');
    if (!url || !window.fetch) return;
    var last = null;
    function toast(msg) { if (window.asToast) window.asToast(true, msg); }
    function setBadge(link, n) {
        var b = link.querySelector('.menu-badge');
        if (n > 0) {
            if (!b) { b = document.createElement('b'); b.className = 'menu-badge'; b.title = 'รอดำเนินการ'; link.appendChild(b); }
            b.textContent = n;
        } else if (b) b.remove();
    }
    function setBell(n) {
        Array.prototype.forEach.call(document.querySelectorAll('.nav-bell'), function (bell) {
            var c = bell.querySelector('.nav-bell__count');
            if (n > 0) {
                if (!c) { c = document.createElement('span'); c.className = 'nav-bell__count'; bell.appendChild(c); }
                c.textContent = n > 9 ? '9+' : String(n);
                bell.setAttribute('aria-label', 'การแจ้งเตือน ยังไม่อ่าน ' + n + ' รายการ');
            } else { if (c) c.remove(); bell.setAttribute('aria-label', 'การแจ้งเตือน'); }
        });
    }
    function poll() {
        if (document.hidden) return;
        fetch(url, { credentials: 'same-origin', headers: { 'Accept': 'application/json' }, cache: 'no-store' })
            .then(function (r) { return r.ok && (r.headers.get('content-type') || '').indexOf('json') >= 0 ? r.json() : null; })
            .then(function (j) {
                if (!j) return;
                if (j.menu) {
                    var news = [];
                    Array.prototype.forEach.call(document.querySelectorAll('[data-badge-key]'), function (a) {
                        var k = a.getAttribute('data-badge-key');
                        var key = Object.keys(j.menu).filter(function (x) { return x.toLowerCase() === k.toLowerCase(); })[0];
                        if (key === undefined) return;
                        var n = j.menu[key] || 0, prev = last && last.menu ? (last.menu[key] || 0) : n;
                        if (n > prev && news.indexOf(key) < 0) news.push(key + '|' + (a.getAttribute('data-badge-label') || a.textContent.replace(/\d+\s*$/, '').trim()) + ' +' + (n - prev));
                        setBadge(a, n);
                    });
                    if (news.length) toast('มีงานใหม่: ' + news.map(function (x) { return x.split('|')[1]; }).join(' · '));
                } else if (typeof j.unread === 'number') {
                    setBell(j.unread);
                    if (last && j.latestId && j.latestId !== last.latestId && j.unread > (last.unread || 0)) toast('การแจ้งเตือนใหม่: ' + j.latest);
                }
                last = j;
            }).catch(function () { /* เครือข่ายสะดุด รอบหน้าค่อยลองใหม่ */ });
    }
    poll();
    setInterval(poll, 5000);
    document.addEventListener('visibilitychange', function () { if (!document.hidden) poll(); });
})();

/* ---- Part 56: ลบการแจ้งเตือนทีละรายการโดยไม่โหลดหน้าใหม่ (ปิด JavaScript ก็ยังลบได้ด้วยฟอร์มปกติ) ---- */
(function () {
    'use strict';
    if (!window.fetch) return;
    document.addEventListener('submit', function (e) {
        var f = e.target;
        if (!f.classList || !f.classList.contains('nt-del')) return;
        e.preventDefault();
        var li = f.closest('.nt-item');
        fetch(f.action, { method: 'POST', body: new FormData(f), credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (j) {
                if (!j || !j.ok) { f.submit(); return; }
                if (li) { li.classList.add('is-removing'); setTimeout(function () { var ul = li.parentNode; li.remove(); if (ul && !ul.children.length) location.reload(); }, 220); }
            }).catch(function () { f.submit(); });
    });
})();

/* ---- แถบแจ้งเตือนในหน้าสมาชิก: กด × เพื่อปิด (จำไว้จนปิดเบราว์เซอร์ · แถบใหม่/สถานะใหม่จะแสดงอีกครั้ง) ---- */
(function () {
    'use strict';
    function store() { try { return window.sessionStorage; } catch (e) { return null; } }
    function hide(key) { var s = store(); if (!s) return; try { s.setItem('as-alert:' + key, '1'); } catch (e) { } }
    function hidden(key) { var s = store(); try { return !!(s && s.getItem('as-alert:' + key)); } catch (e) { return false; } }
    function init() {
        Array.prototype.forEach.call(document.querySelectorAll('[data-alert-key]'), function (el) {
            if (hidden(el.getAttribute('data-alert-key'))) el.remove();
        });
    }
    document.addEventListener('click', function (e) {
        var b = e.target.closest && e.target.closest('[data-dismiss-alert]');
        if (!b) return;
        var box = b.closest('[data-alert-key]');
        if (!box) return;
        hide(box.getAttribute('data-alert-key'));
        box.remove();
    });
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();

/* ---- หน้ากิจกรรม: กดรอบ (ชิป) เพื่อเลือกรอบนั้นในฟอร์มจองของการ์ดเดียวกัน ---- */
(function () {
    'use strict';
    document.addEventListener('click', function (e) {
        var chip = e.target.closest && e.target.closest('.ac-slot[data-slot]');
        if (!chip) return;
        var card = chip.closest('article'); var sel = card && card.querySelector('select[name="slot"]');
        if (!sel) return;
        var v = chip.getAttribute('data-slot');
        if (sel.tomselect) { sel.tomselect.setValue(v); sel.tomselect.focus(); } else { sel.value = v; sel.focus(); }
    });
})();

/* ---- Part 67: กล่องแจ้งในหน้า (cx-alert) ทุกกล่องกด × ปิดได้ ---- */
(function () {
    'use strict';
    function add() {
        Array.prototype.forEach.call(document.querySelectorAll('.cx-alert:not(.is-toasted):not([data-no-close])'), function (el) {
            if (el.querySelector('.cx-alert__x')) return;
            var b = document.createElement('button');
            b.type = 'button'; b.className = 'cx-alert__x'; b.setAttribute('aria-label', 'ปิดข้อความนี้'); b.textContent = '×';
            el.appendChild(b);
        });
    }
    document.addEventListener('click', function (e) {
        var b = e.target.closest && e.target.closest('.cx-alert__x');
        if (!b) return;
        var box = b.closest('.cx-alert');
        if (!box) return;
        box.classList.add('is-closing');
        setTimeout(function () { box.remove(); }, 180);
    });
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { setTimeout(add, 0); }); else setTimeout(add, 0);
})();
