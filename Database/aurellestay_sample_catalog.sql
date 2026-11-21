-- =====================================================================
-- (ไม่บังคับ) ข้อมูลตัวอย่างสำหรับเริ่มใช้งาน/สาธิต: ธีม ประเภทห้อง ห้อง และบริการเสริม
-- ราคาและห้องเป็นค่าสมมติ ให้แก้เป็นของจริงที่หน้าผู้จัดการ (/AdminRooms, /AdminServices)
-- ไม่ทับข้อมูลเดิม: เพิ่มเฉพาะรายการที่ชื่อ/เลขห้องยังไม่มี
-- =====================================================================
USE `csi402db`;
SET NAMES utf8mb4;

INSERT IGNORE INTO `room_themes` (`theme_name`,`description`) VALUES
  ('ธีมม่วง','โทนม่วงอบอุ่น'), ('ธีมเหลือง','โทนเหลืองสดใส');

INSERT IGNORE INTO `room_types` (`type_name`,`base_price`,`max_guests`) VALUES
  ('Standard Double',1200.00,3), ('Deluxe Twin',1800.00,3), ('Family Suite',2800.00,5);

INSERT IGNORE INTO `rooms` (`room_no`,`type_id`,`theme_id`,`status`) VALUES
  ('101',(SELECT `type_id` FROM `room_types` WHERE `type_name`='Standard Double'),(SELECT `theme_id` FROM `room_themes` WHERE `theme_name`='ธีมม่วง'),'AVAILABLE'),
  ('102',(SELECT `type_id` FROM `room_types` WHERE `type_name`='Standard Double'),(SELECT `theme_id` FROM `room_themes` WHERE `theme_name`='ธีมเหลือง'),'AVAILABLE'),
  ('201',(SELECT `type_id` FROM `room_types` WHERE `type_name`='Deluxe Twin'),(SELECT `theme_id` FROM `room_themes` WHERE `theme_name`='ธีมม่วง'),'AVAILABLE'),
  ('202',(SELECT `type_id` FROM `room_types` WHERE `type_name`='Deluxe Twin'),(SELECT `theme_id` FROM `room_themes` WHERE `theme_name`='ธีมเหลือง'),'AVAILABLE'),
  ('301',(SELECT `type_id` FROM `room_types` WHERE `type_name`='Family Suite'),(SELECT `theme_id` FROM `room_themes` WHERE `theme_name`='ธีมม่วง'),'AVAILABLE');

INSERT IGNORE INTO `extra_services` (`service_name`,`price`,`is_active`) VALUES
  ('อาหารเช้า',200.00,1), ('อาหารเย็น',350.00,1), ('เตียงเสริม',500.00,1),
  ('นวดแผนไทย (60 นาที)',450.00,1), ('สปาอโรมา (90 นาที)',1200.00,1);

-- หมวด คำอธิบาย และรูปบริการเสริม (รูปฟรีลิขสิทธิ์จาก Unsplash ใน wwwroot/images/services) — เติมเฉพาะที่ยังว่าง
UPDATE `extra_services` SET `category` = COALESCE(`category`, 'DINE'), `image_url` = COALESCE(`image_url`, '/images/services/breakfast.jpg'),
  `description` = COALESCE(`description`, 'อาหารเช้าสำหรับผู้เข้าพัก เสิร์ฟทุกเช้าระหว่างการเข้าพัก') WHERE `service_name` = 'อาหารเช้า';
UPDATE `extra_services` SET `category` = COALESCE(`category`, 'DINE'), `image_url` = COALESCE(`image_url`, '/images/services/dinner.jpg'),
  `description` = COALESCE(`description`, 'อาหารเย็นสำหรับผู้เข้าพัก') WHERE `service_name` = 'อาหารเย็น';
UPDATE `extra_services` SET `category` = COALESCE(`category`, 'ROOM'), `image_url` = COALESCE(`image_url`, '/images/services/extra-bed.jpg'),
  `description` = COALESCE(`description`, 'เตียงเสริมพร้อมชุดเครื่องนอน จัดเตรียมในห้องก่อนเช็คอิน') WHERE `service_name` = 'เตียงเสริม';
UPDATE `extra_services` SET `category` = COALESCE(`category`, 'SPA'), `image_url` = COALESCE(`image_url`, '/images/services/thai-massage.jpg'),
  `description` = COALESCE(`description`, 'นวดผ่อนคลาย 60 นาที') WHERE `service_name` = 'นวดแผนไทย (60 นาที)';
UPDATE `extra_services` SET `category` = COALESCE(`category`, 'SPA'), `image_url` = COALESCE(`image_url`, '/images/services/aroma-spa.jpg'),
  `description` = COALESCE(`description`, 'สปาน้ำมันหอมระเหย 90 นาที') WHERE `service_name` = 'สปาอโรมา (90 นาที)';

-- รายละเอียด สิ่งอำนวยความสะดวก (คั่นด้วย ,) และรูป (รูปฟรีลิขสิทธิ์จาก Unsplash ใน wwwroot/images/rooms)
-- เติมเฉพาะประเภทที่ยังไม่มีข้อมูล ไม่ทับที่ผู้จัดการแก้ไว้
UPDATE `room_types` SET
  `description` = COALESCE(`description`, 'ห้องเตียงใหญ่ขนาดควีน แสงธรรมชาติจากหน้าต่างบานกว้าง เหมาะกับคู่รักหรือนักเดินทางที่ต้องการความสงบ'),
  `amenities`   = COALESCE(`amenities`, 'เตียงควีน,Wi-Fi ฟรี,แอร์,ทีวี 43 นิ้ว,ฝักบัวน้ำอุ่น,มินิบาร์,ตู้เซฟ'),
  `image_url`   = COALESCE(`image_url`, '/images/rooms/standard-double.jpg')
WHERE `type_name` = 'Standard Double';
UPDATE `room_types` SET
  `description` = COALESCE(`description`, 'ห้องสองเตียงเดี่ยวพร้อมมุมนั่งเล่นริมหน้าต่าง เหมาะกับเพื่อนหรือครอบครัวเล็ก'),
  `amenities`   = COALESCE(`amenities`, '2 เตียงเดี่ยว,Wi-Fi ฟรี,แอร์,ทีวี 50 นิ้ว,มุมนั่งเล่น,เครื่องชงกาแฟ,ตู้เซฟ'),
  `image_url`   = COALESCE(`image_url`, '/images/rooms/deluxe-twin.jpg')
WHERE `type_name` = 'Deluxe Twin';
UPDATE `room_types` SET
  `description` = COALESCE(`description`, 'ห้องสวีทพร้อมห้องนั่งเล่นแยก พื้นที่กว้างสำหรับครอบครัวสูงสุด 5 ท่าน'),
  `amenities`   = COALESCE(`amenities`, 'ห้องนั่งเล่นแยก,เตียงคิง + โซฟาเบด,Wi-Fi ฟรี,แอร์,ทีวี 2 เครื่อง,อ่างอาบน้ำ,มินิบาร์,ตู้เซฟ'),
  `image_url`   = COALESCE(`image_url`, '/images/rooms/family-suite.jpg')
WHERE `type_name` = 'Family Suite';

-- Part 37: ตัวอย่างโปรโมชั่นแบบใหม่ (เพิ่มเฉพาะเมื่อยังไม่มีชื่อนี้)
INSERT INTO `promotions` (`promo_name`,`promo_type`,`discount_percent`,`min_advance_days`,`need_staff_confirm`,`start_date`,`end_date`,`is_active`,`promo_code`,`description`,`min_nights`,`max_uses`,`max_discount`)
SELECT * FROM (
  SELECT 'พักยาว 3 คืนขึ้นไป' AS n,'LONG_STAY' AS t,12.00 AS d,NULL AS a,0 AS c,NULL AS s,NULL AS e,1 AS i,NULL AS code,'พักตั้งแต่ 3 คืนรับส่วนลดค่าห้องทันที' AS descr,3 AS mn,NULL AS mu,NULL AS md
  UNION ALL SELECT 'ต้อนรับปีใหม่','SEASONAL',15.00,NULL,0,'2026-12-28','2027-01-03',1,NULL,'ฉลองปีใหม่กับเรา เข้าพักช่วงเทศกาลรับส่วนลด',2,NULL,3000.00
  UNION ALL SELECT 'รหัสต้อนรับ AURELLE10','CODE',10.00,NULL,0,NULL,NULL,1,'AURELLE10','กรอกรหัส AURELLE10 ตอนจอง (ลดสูงสุด 1,000 บาท จำกัด 100 สิทธิ์)',NULL,100,1000.00
) AS v
WHERE NOT EXISTS (SELECT 1 FROM `promotions` p WHERE p.`promo_name` = v.n);

-- Part 38: รหัสสาธารณะใช้ได้ท่านละ 1 ครั้ง · รหัสเฉพาะลูกค้า (ผู้จัดการส่งให้จากหน้าโปรโมชั่น)
UPDATE `promotions` SET `max_per_user` = COALESCE(`max_per_user`, 1) WHERE `promo_code` = 'AURELLE10';
INSERT INTO `promotions` (`promo_name`,`promo_type`,`discount_percent`,`need_staff_confirm`,`is_active`,`promo_code`,`description`,`max_per_user`,`recipients_only`,`max_discount`)
SELECT 'ขอบคุณลูกค้าคนสำคัญ','CODE',15.00,0,1,'THANKYOU15','รหัสพิเศษสำหรับลูกค้าที่เคยเข้าพัก ส่งให้ทางการแจ้งเตือนในระบบ',1,1,2000.00
WHERE NOT EXISTS (SELECT 1 FROM `promotions` WHERE `promo_code` = 'THANKYOU15');
