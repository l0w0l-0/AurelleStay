-- =====================================================================
-- AurelleStay — ข้อมูลเพิ่มเติม (Part 59): ห้องพัก · พนักงาน · บริการเสริม · กิจกรรม
-- เพิ่มอย่างเดียว ไม่แก้/ไม่ลบข้อมูลเดิม · รันซ้ำได้ (ข้ามรายการที่มีอยู่แล้ว)
-- วิธีรันกับ Docker:  .\docker.ps1 moredata
-- พนักงานใหม่ใช้รหัสผ่านเดียวกับบัญชีหลักของฝ่ายนั้น (คัดลอกจากฐานข้อมูล ไม่มีรหัสผ่านในไฟล์นี้)
--   ถ้ายังไม่มีบัญชีหลักของฝ่ายนั้น จะข้ามการสร้างพนักงานฝ่ายนั้นไป
-- =====================================================================
SET NAMES utf8mb4;

-- ---------------------------------------------------------------- ห้องพัก (ใช้ประเภทห้องเดิม)
INSERT IGNORE INTO rooms (room_no, type_id, status)
SELECT x.no, t.type_id, 'AVAILABLE'
FROM (SELECT '103' no, 'Standard Double' tn UNION ALL SELECT '104', 'Standard Double' UNION ALL SELECT '105', 'Standard Double'
      UNION ALL SELECT '106', 'Standard Double' UNION ALL SELECT '107', 'Standard Double' UNION ALL SELECT '108', 'Standard Double'
      UNION ALL SELECT '203', 'Deluxe Twin' UNION ALL SELECT '204', 'Deluxe Twin' UNION ALL SELECT '205', 'Deluxe Twin'
      UNION ALL SELECT '206', 'Deluxe Twin' UNION ALL SELECT '207', 'Deluxe Twin'
      UNION ALL SELECT '302', 'Family Suite' UNION ALL SELECT '303', 'Family Suite' UNION ALL SELECT '304', 'Family Suite') x
JOIN room_types t ON t.type_name = x.tn;

-- ---------------------------------------------------------------- พนักงาน
INSERT INTO users (role_id, first_name, last_name, phone, email, password_hash, is_member, created_at, security_stamp)
SELECT b.role_id, x.fn, x.ln, x.ph, x.em, b.password_hash, 0, NOW(), b.security_stamp
FROM (SELECT 'frontdesk@aurellestay.local' base, 'สมใจ' fn, 'ใจดี' ln, '0891000101' ph, 'frontdesk2@aurellestay.local' em
      UNION ALL SELECT 'frontdesk@aurellestay.local', 'วรรณา', 'ศรีสุข', '0891000102', 'frontdesk3@aurellestay.local'
      UNION ALL SELECT 'housekeeping@aurellestay.local', 'มาลี', 'ทองดี', '0891000201', 'housekeeping2@aurellestay.local'
      UNION ALL SELECT 'housekeeping@aurellestay.local', 'สมพร', 'แก้วใส', '0891000202', 'housekeeping3@aurellestay.local'
      UNION ALL SELECT 'housekeeping@aurellestay.local', 'บุญมี', 'สายทอง', '0891000203', 'housekeeping4@aurellestay.local'
      UNION ALL SELECT 'delivery@aurellestay.local', 'ธนพล', 'รุ่งเรือง', '0891000301', 'delivery2@aurellestay.local'
      UNION ALL SELECT 'delivery@aurellestay.local', 'กิตติ', 'พรหมมา', '0891000302', 'delivery3@aurellestay.local'
      UNION ALL SELECT 'accounting@aurellestay.local', 'ปวีณา', 'บุญมา', '0891000401', 'accounting2@aurellestay.local') x
JOIN users b ON b.email = x.base
WHERE NOT EXISTS (SELECT 1 FROM users u WHERE u.email = x.em OR u.phone = x.ph);

-- ---------------------------------------------------------------- บริการเสริม
INSERT IGNORE INTO extra_services (service_name, price, is_active, category, description) VALUES
('อาหารกลางวัน', 300, 1, 'DINE', 'เมนูไทยและนานาชาติ เสิร์ฟที่ห้องอาหารหรือส่งถึงห้อง'),
('ชุดอาหารว่างยามบ่าย (2 ที่)', 390, 1, 'DINE', 'ชา/กาแฟ ขนมไทยและเบเกอรี่ ช่วง 14:00–17:00'),
('เค้กวันเกิด 1 ปอนด์', 650, 1, 'DINE', 'เขียนข้อความบนเค้กได้ สั่งล่วงหน้า 1 วัน'),
('ประคบสมุนไพร (60 นาที)', 550, 1, 'SPA', 'ลูกประคบสมุนไพรไทยอุ่น ๆ คลายกล้ามเนื้อ'),
('นวดเท้า (45 นาที)', 350, 1, 'SPA', 'กดจุดฝ่าเท้า ผ่อนคลายหลังเที่ยวทั้งวัน'),
('เช็คเอาท์ล่าช้า (ถึง 15:00)', 500, 1, 'ROOM', 'อยู่ต่อได้ถึงบ่ายสามโมง ขึ้นกับห้องว่าง'),
('บริการซักรีด (ต่อถุง)', 250, 1, 'ROOM', 'ซัก อบ รีด คืนภายใน 24 ชั่วโมง'),
('ตกแต่งห้องวันพิเศษ', 1500, 1, 'ROOM', 'ดอกไม้ ลูกโป่ง และการ์ด สำหรับวันเกิดหรือครบรอบ'),
('รถรับส่งสนามบิน (เที่ยวเดียว)', 600, 1, 'OTHER', 'รถตู้ส่วนตัว นัดเวลาล่วงหน้าอย่างน้อย 6 ชั่วโมง'),
('เช่าจักรยาน (1 วัน)', 150, 1, 'OTHER', 'จักรยานพร้อมหมวกกันน็อกและแผนที่เส้นทาง');

-- ---------------------------------------------------------------- กิจกรรม
INSERT INTO activities (name, category, description, price, duration_min, capacity, times, days, location, is_active, created_at)
SELECT x.n, x.c, x.d, x.p, x.m, x.cap, x.t, x.dy, x.loc, 1, NOW()
FROM (SELECT 'ล่องเรือชมพระอาทิตย์ตก' n, 'TOUR' c, 'ล่องเรือริมแม่น้ำชมแสงสุดท้ายของวัน พร้อมเครื่องดื่มเย็น ๆ' d, 450 p, 90 m, 20 cap, '17:00' t, '0123456' dy, 'ท่าเรือหน้าโรงแรม' loc
      UNION ALL SELECT 'มวยไทยเบื้องต้น', 'WELLNESS', 'เรียนท่าพื้นฐานกับครูมวย เหมาะกับผู้เริ่มต้น', 300, 60, 8, '08:00,16:00', '123456', 'ลานกิจกรรมชั้น 1'
      UNION ALL SELECT 'ทำสบู่และยาดมสมุนไพร', 'CULTURE', 'ทำสบู่และยาดมจากสมุนไพรไทย กลับบ้านไปเป็นของฝากได้', 250, 90, 12, '13:00', '0246', 'ห้องเวิร์กช็อป'
      UNION ALL SELECT 'ทัวร์ใส่บาตรยามเช้า', 'CULTURE', 'เดินไปวัดใกล้โรงแรม ใส่บาตรและฟังเรื่องเล่าชุมชน', 0, 60, 15, '06:00', '06', 'ล็อบบี้'
      UNION ALL SELECT 'คลาสทำขนมไทย', 'FOOD', 'ทำขนมชั้นและลูกชุบกับเชฟขนมไทย', 350, 120, 10, '10:00', '135', 'ครัวเรียนทำอาหาร') x
WHERE NOT EXISTS (SELECT 1 FROM activities a WHERE a.name = x.n);

-- ---------------------------------------------------------------- Part 62: รูปตั้งต้น (ใส่เฉพาะรายการที่ยังไม่มีรูป)
UPDATE extra_services SET image_url = '/images/services/lunch.jpg' WHERE service_name = 'อาหารกลางวัน' AND image_url IS NULL;
UPDATE extra_services SET image_url = '/images/services/afternoon-tea.jpg' WHERE service_name = 'ชุดอาหารว่างยามบ่าย (2 ที่)' AND image_url IS NULL;
UPDATE extra_services SET image_url = '/images/services/birthday-cake.jpg' WHERE service_name = 'เค้กวันเกิด 1 ปอนด์' AND image_url IS NULL;
UPDATE extra_services SET image_url = '/images/services/herbal-compress.jpg' WHERE service_name = 'ประคบสมุนไพร (60 นาที)' AND image_url IS NULL;
UPDATE extra_services SET image_url = '/images/services/foot-massage.jpg' WHERE service_name = 'นวดเท้า (45 นาที)' AND image_url IS NULL;
UPDATE extra_services SET image_url = '/images/services/late-checkout.jpg' WHERE service_name = 'เช็คเอาท์ล่าช้า (ถึง 15:00)' AND image_url IS NULL;
UPDATE extra_services SET image_url = '/images/services/laundry.jpg' WHERE service_name = 'บริการซักรีด (ต่อถุง)' AND image_url IS NULL;
UPDATE extra_services SET image_url = '/images/services/room-decoration.jpg' WHERE service_name = 'ตกแต่งห้องวันพิเศษ' AND image_url IS NULL;
UPDATE extra_services SET image_url = '/images/services/airport-transfer.jpg' WHERE service_name = 'รถรับส่งสนามบิน (เที่ยวเดียว)' AND image_url IS NULL;
UPDATE extra_services SET image_url = '/images/services/bicycle-rental.jpg' WHERE service_name = 'เช่าจักรยาน (1 วัน)' AND image_url IS NULL;
UPDATE activities SET image_url = '/images/activities/thai-cooking.jpg' WHERE name = 'คลาสทำอาหารไทย' AND image_url IS NULL;
UPDATE activities SET image_url = '/images/activities/yoga.jpg' WHERE name = 'โยคะยามเช้าริมสระ' AND image_url IS NULL;
UPDATE activities SET image_url = '/images/activities/floating-market.jpg' WHERE name = 'ทัวร์ตลาดน้ำครึ่งวัน' AND image_url IS NULL;
UPDATE activities SET image_url = '/images/activities/garland.jpg' WHERE name = 'เวิร์กช็อปพวงมาลัยและดอกไม้ไทย' AND image_url IS NULL;
UPDATE activities SET image_url = '/images/activities/bike-tour.jpg' WHERE name = 'ปั่นจักรยานชมเมืองเก่า' AND image_url IS NULL;
UPDATE activities SET image_url = '/images/activities/kids-art.jpg' WHERE name = 'ศิลปะเด็กวันหยุด' AND image_url IS NULL;
UPDATE activities SET image_url = '/images/activities/sunset-cruise.jpg' WHERE name = 'ล่องเรือชมพระอาทิตย์ตก' AND image_url IS NULL;
UPDATE activities SET image_url = '/images/activities/muay-thai.jpg' WHERE name = 'มวยไทยเบื้องต้น' AND image_url IS NULL;
UPDATE activities SET image_url = '/images/activities/herbal-soap.jpg' WHERE name = 'ทำสบู่และยาดมสมุนไพร' AND image_url IS NULL;
UPDATE activities SET image_url = '/images/activities/alms.jpg' WHERE name = 'ทัวร์ใส่บาตรยามเช้า' AND image_url IS NULL;
UPDATE activities SET image_url = '/images/activities/thai-dessert.jpg' WHERE name = 'คลาสทำขนมไทย' AND image_url IS NULL;
