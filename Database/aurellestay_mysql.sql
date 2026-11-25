-- =====================================================================
-- AurelleStay  |  MySQL 8.x  |  ฐานข้อมูล csi402db
-- สร้างจากโมเดลของโปรเจกต์ (EF Core + Pomelo) ตรงกับที่โค้ดใช้งานจริง
--
-- ปลอดภัยต่อข้อมูลเดิม: ใช้ CREATE TABLE IF NOT EXISTS และ INSERT แบบไม่ซ้ำ
-- ไม่มีคำสั่ง DROP / DELETE / TRUNCATE — รันซ้ำได้โดยไม่ทับข้อมูลเดิม
-- ฐานข้อมูลเดิมจากเวอร์ชันก่อน: ข้อ 1.1 เพิ่มคอลัมน์ใหม่ให้อัตโนมัติ (เพิ่มอย่างเดียว)
-- =====================================================================
CREATE DATABASE IF NOT EXISTS `csi402db` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE `csi402db`;
SET NAMES utf8mb4;

-- ---------------------------------------------------------------- 1) ตาราง
CREATE TABLE IF NOT EXISTS `extra_services` (
    `service_id` int NOT NULL AUTO_INCREMENT,
    `service_name` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `price` decimal(10,2) NOT NULL,
    `is_active` tinyint(1) NOT NULL DEFAULT '1',
    PRIMARY KEY (`service_id`),
    UNIQUE KEY `uq_service_name` (`service_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `promotions` (
    `promo_id` int NOT NULL AUTO_INCREMENT,
    `promo_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `promo_type` enum('MEMBER','MOTHERS_DAY','EARLY_BIRD','BIRTHDAY','ANNIVERSARY','SEASONAL','LONG_STAY','CODE') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `discount_percent` decimal(5,2) NOT NULL,
    `min_advance_days` smallint NULL,
    `need_staff_confirm` tinyint(1) NOT NULL,
    `start_date` date NULL,
    `end_date` date NULL,
    `is_active` tinyint(1) NOT NULL DEFAULT '1',
    PRIMARY KEY (`promo_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `roles` (
    `role_id` tinyint NOT NULL AUTO_INCREMENT,
    `role_name` varchar(30) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    PRIMARY KEY (`role_id`),
    UNIQUE KEY `uq_role_name` (`role_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `room_themes` (
    `theme_id` int NOT NULL AUTO_INCREMENT,
    `theme_name` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `description` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    PRIMARY KEY (`theme_id`),
    UNIQUE KEY `uq_theme_name` (`theme_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `room_types` (
    `type_id` int NOT NULL AUTO_INCREMENT,
    `type_name` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `base_price` decimal(10,2) NOT NULL,
    `max_guests` tinyint NOT NULL DEFAULT '2',
    `description` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `amenities` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `image_url` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    PRIMARY KEY (`type_id`),
    UNIQUE KEY `uq_type_name` (`type_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `users` (
    `user_id` int NOT NULL AUTO_INCREMENT,
    `role_id` tinyint NOT NULL,
    `first_name` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `last_name` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `phone` varchar(15) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `email` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `password_hash` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `birth_date` date NULL,
    `anniversary_date` date NULL,
    `is_member` tinyint(1) NOT NULL,
    `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`user_id`),
    CONSTRAINT `users_ibfk_1` FOREIGN KEY (`role_id`) REFERENCES `roles` (`role_id`),
    UNIQUE KEY `uq_email` (`email`),
    UNIQUE KEY `uq_phone` (`phone`),
    KEY `ix_role_id` (`role_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `rooms` (
    `room_id` int NOT NULL AUTO_INCREMENT,
    `room_no` varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `type_id` int NOT NULL,
    `theme_id` int NULL,
    `status` enum('AVAILABLE','BOOKED','OCCUPIED','CLEANING','MAINTENANCE') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'AVAILABLE',
    PRIMARY KEY (`room_id`),
    CONSTRAINT `rooms_ibfk_1` FOREIGN KEY (`type_id`) REFERENCES `room_types` (`type_id`),
    CONSTRAINT `rooms_ibfk_2` FOREIGN KEY (`theme_id`) REFERENCES `room_themes` (`theme_id`),
    UNIQUE KEY `uq_room_no` (`room_no`),
    KEY `ix_theme_id` (`theme_id`),
    KEY `ix_type_id` (`type_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `bookings` (
    `booking_id` int NOT NULL AUTO_INCREMENT,
    `user_id` int NOT NULL,
    `promo_id` int NULL,
    `confirmed_by` int NULL,
    `check_in` date NOT NULL,
    `check_out` date NOT NULL,
    `booked_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `status` enum('PENDING','CONFIRMED','CHECKED_IN','CHECKED_OUT','CANCELLED','NO_SHOW') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'PENDING',
    `subtotal` decimal(10,2) NOT NULL,
    `discount_total` decimal(10,2) NOT NULL,
    `net_total` decimal(10,2) NOT NULL,
    `guests` tinyint NULL,
    PRIMARY KEY (`booking_id`),
    CONSTRAINT `bookings_ibfk_1` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`),
    CONSTRAINT `bookings_ibfk_2` FOREIGN KEY (`promo_id`) REFERENCES `promotions` (`promo_id`),
    CONSTRAINT `bookings_ibfk_3` FOREIGN KEY (`confirmed_by`) REFERENCES `users` (`user_id`),
    KEY `ix_confirmed_by` (`confirmed_by`),
    KEY `ix_promo_id` (`promo_id`),
    KEY `ix_user_id` (`user_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `login_history` (
    `login_history_id` int NOT NULL AUTO_INCREMENT,
    `user_id` int NOT NULL,
    `email` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `logged_in_at_utc` datetime NOT NULL,
    PRIMARY KEY (`login_history_id`),
    CONSTRAINT `fk_login_history_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE CASCADE,
    KEY `ix_user_id` (`user_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `booking_rooms` (
    `br_id` int NOT NULL AUTO_INCREMENT,
    `booking_id` int NOT NULL,
    `room_id` int NOT NULL,
    `price_per_night` decimal(10,2) NOT NULL,
    `nights` smallint NOT NULL,
    PRIMARY KEY (`br_id`),
    CONSTRAINT `booking_rooms_ibfk_1` FOREIGN KEY (`booking_id`) REFERENCES `bookings` (`booking_id`) ON DELETE CASCADE,
    CONSTRAINT `booking_rooms_ibfk_2` FOREIGN KEY (`room_id`) REFERENCES `rooms` (`room_id`),
    KEY `ix_booking_id` (`booking_id`),
    KEY `ix_room_id` (`room_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `booking_services` (
    `bs_id` int NOT NULL AUTO_INCREMENT,
    `booking_id` int NOT NULL,
    `room_id` int NOT NULL,
    `service_id` int NOT NULL,
    `quantity` int NOT NULL DEFAULT '1',
    `unit_price` decimal(10,2) NOT NULL,
    PRIMARY KEY (`bs_id`),
    CONSTRAINT `booking_services_ibfk_1` FOREIGN KEY (`booking_id`) REFERENCES `bookings` (`booking_id`) ON DELETE CASCADE,
    CONSTRAINT `booking_services_ibfk_2` FOREIGN KEY (`room_id`) REFERENCES `rooms` (`room_id`),
    CONSTRAINT `booking_services_ibfk_3` FOREIGN KEY (`service_id`) REFERENCES `extra_services` (`service_id`),
    KEY `ix_booking_id` (`booking_id`),
    KEY `ix_room_id` (`room_id`),
    KEY `ix_service_id` (`service_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `housekeeping_tasks` (
    `task_id` int NOT NULL AUTO_INCREMENT,
    `room_id` int NOT NULL,
    `booking_id` int NULL,
    `assigned_to` int NULL,
    `status` enum('PENDING','CLEANING','DONE') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'PENDING',
    `done_at` datetime NULL,
    PRIMARY KEY (`task_id`),
    CONSTRAINT `housekeeping_tasks_ibfk_1` FOREIGN KEY (`room_id`) REFERENCES `rooms` (`room_id`),
    CONSTRAINT `housekeeping_tasks_ibfk_2` FOREIGN KEY (`booking_id`) REFERENCES `bookings` (`booking_id`),
    CONSTRAINT `housekeeping_tasks_ibfk_3` FOREIGN KEY (`assigned_to`) REFERENCES `users` (`user_id`),
    KEY `ix_assigned_to` (`assigned_to`),
    KEY `ix_booking_id` (`booking_id`),
    KEY `ix_room_id` (`room_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `payments` (
    `payment_id` int NOT NULL AUTO_INCREMENT,
    `booking_id` int NOT NULL,
    `amount` decimal(10,2) NOT NULL,
    `payment_type` enum('PAYMENT','REFUND') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'PAYMENT',
    `method` enum('CASH','QR','CARD') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'QR',
    `status` enum('PENDING','PAID','REFUNDED','REJECTED') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'PENDING',
    `handled_by` int NULL,
    `paid_at` datetime NULL,
    `slip_path` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `reference` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    PRIMARY KEY (`payment_id`),
    CONSTRAINT `payments_ibfk_1` FOREIGN KEY (`booking_id`) REFERENCES `bookings` (`booking_id`),
    CONSTRAINT `payments_ibfk_2` FOREIGN KEY (`handled_by`) REFERENCES `users` (`user_id`),
    KEY `ix_booking_id` (`booking_id`),
    KEY `ix_handled_by` (`handled_by`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `reviews` (
    `review_id` int NOT NULL AUTO_INCREMENT,
    `booking_id` int NOT NULL,
    `user_id` int NOT NULL,
    `rating` tinyint NOT NULL,
    `comment` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`review_id`),
    CONSTRAINT `reviews_ibfk_1` FOREIGN KEY (`booking_id`) REFERENCES `bookings` (`booking_id`),
    CONSTRAINT `reviews_ibfk_2` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`),
    UNIQUE KEY `uq_booking_id` (`booking_id`),
    KEY `ix_user_id` (`user_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `delivery_tasks` (
    `task_id` int NOT NULL AUTO_INCREMENT,
    `bs_id` int NOT NULL,
    `room_id` int NOT NULL,
    `assigned_to` int NULL,
    `status` enum('PENDING','DELIVERING','DONE') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'PENDING',
    `done_at` datetime NULL,
    PRIMARY KEY (`task_id`),
    CONSTRAINT `delivery_tasks_ibfk_1` FOREIGN KEY (`bs_id`) REFERENCES `booking_services` (`bs_id`),
    CONSTRAINT `delivery_tasks_ibfk_2` FOREIGN KEY (`room_id`) REFERENCES `rooms` (`room_id`),
    CONSTRAINT `delivery_tasks_ibfk_3` FOREIGN KEY (`assigned_to`) REFERENCES `users` (`user_id`),
    KEY `ix_assigned_to` (`assigned_to`),
    KEY `ix_bs_id` (`bs_id`),
    KEY `ix_room_id` (`room_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `expenses` (
    `expense_id` int NOT NULL AUTO_INCREMENT,
    `category` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `amount` decimal(10,2) NOT NULL,
    `spent_on` date NOT NULL,
    `note` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_by` int NULL,
    `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`expense_id`),
    CONSTRAINT `expenses_ibfk_1` FOREIGN KEY (`created_by`) REFERENCES `users` (`user_id`) ON DELETE SET NULL,
    KEY `ix_spent_on` (`spent_on`),
    KEY `ix_created_by` (`created_by`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- บันทึกกิจกรรม (Part 17): ใครทำอะไร กับรายการไหน สำเร็จหรือไม่ เมื่อไร — เก็บอีเมลผู้ทำไว้ด้วย เผื่อบัญชีถูกลบภายหลัง
CREATE TABLE IF NOT EXISTS `audit_logs` (
    `audit_id` bigint NOT NULL AUTO_INCREMENT,
    `user_id` int NULL,
    `actor` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `actor_role` varchar(30) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `action` varchar(60) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `target_id` int NULL,
    `success` tinyint(1) NOT NULL,
    `detail` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `ip` varchar(45) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`audit_id`),
    CONSTRAINT `audit_logs_ibfk_1` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE SET NULL,
    KEY `ix_audit_created` (`created_at`),
    KEY `ix_audit_user` (`user_id`),
    KEY `ix_audit_action` (`action`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- การแจ้งเตือนในระบบสำหรับลูกค้า (Part 18): สลิปผ่าน/ไม่ผ่าน คืนเงิน การจองถูกยกเลิก — ลบตามบัญชี
CREATE TABLE IF NOT EXISTS `notifications` (
    `notification_id` bigint NOT NULL AUTO_INCREMENT,
    `user_id` int NOT NULL,
    `title` varchar(150) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `body` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `link` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `is_read` tinyint(1) NOT NULL DEFAULT 0,
    `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`notification_id`),
    CONSTRAINT `notifications_ibfk_1` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE CASCADE,
    KEY `ix_notify_user` (`user_id`, `is_read`, `created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Part 51: อีเมลขาออก (ส่งผ่าน SMTP / เก็บไว้ให้ผู้จัดการดูถ้ายังไม่ตั้งค่า) · คำขอรีเซ็ตรหัสผ่าน (เก็บเฉพาะแฮชของโทเคน) · รายชื่อรอห้องว่าง
CREATE TABLE IF NOT EXISTS `email_outbox` (
    `email_id` bigint NOT NULL AUTO_INCREMENT,
    `user_id` int NULL,
    `to_email` varchar(120) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `subject` varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `body` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `link` varchar(300) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `status` varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'PENDING',
    `attempts` int NOT NULL DEFAULT 0,
    `last_error` varchar(300) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `sent_at` datetime NULL,
    PRIMARY KEY (`email_id`),
    KEY `ix_outbox_status` (`status`, `created_at`),
    KEY `ix_outbox_user` (`user_id`),
    CONSTRAINT `email_outbox_ibfk_1` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `password_resets` (
    `reset_id` bigint NOT NULL AUTO_INCREMENT,
    `user_id` int NOT NULL,
    `token_hash` char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `expires_at` datetime NOT NULL,
    `used_at` datetime NULL,
    `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `ip` varchar(45) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    PRIMARY KEY (`reset_id`),
    UNIQUE KEY `ux_reset_token` (`token_hash`),
    KEY `ix_reset_user` (`user_id`, `created_at`),
    CONSTRAINT `password_resets_ibfk_1` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `room_waitlist` (
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Part 52: กิจกรรมของโรงแรม (รอบเวลาประจำวัน + ที่นั่งต่อรอบ) และการจองกิจกรรมของผู้เข้าพัก — ระบบใส่กิจกรรมตัวอย่างให้ตอนเริ่มครั้งแรก
CREATE TABLE IF NOT EXISTS `activities` (
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `activity_bookings` (
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Part 54: แม่บ้านแจ้งซ่อม
CREATE TABLE IF NOT EXISTS `room_issues` (
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Part 53: ของใช้ที่ผู้เข้าพักขอเพิ่มระหว่างเข้าพัก (คิวฝ่ายส่งของ)
CREATE TABLE IF NOT EXISTS `item_requests` (
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ข้อความติดต่อโรงแรมจากหน้า "ติดต่อเรา" (Part 30) รวมคำขอรีเซ็ตรหัสผ่าน — ผู้ส่งที่มีบัญชีผูก user_id (ลบบัญชีแล้วข้อความยังอยู่)
CREATE TABLE IF NOT EXISTS `contact_messages` (
    `message_id` bigint NOT NULL AUTO_INCREMENT,
    `user_id` int NULL,
    `name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `email` varchar(150) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `phone` varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `topic` varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'GENERAL',
    `message` varchar(2000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
    `status` varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'NEW',
    `staff_note` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `handled_by` varchar(150) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `ip` varchar(45) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
    `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `handled_at` datetime NULL,
    PRIMARY KEY (`message_id`),
    CONSTRAINT `contact_messages_ibfk_1` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`) ON DELETE SET NULL,
    KEY `ix_contact_status` (`status`, `created_at`),
    KEY `ix_contact_ip` (`ip`, `created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ผู้ที่ได้รับรหัสโปรโมชั่น (Part 38): ผู้จัดการส่งรหัสให้ลูกค้าเป็นการแจ้งเตือน · โปรที่ตั้ง "เฉพาะผู้ได้รับรหัส" ใช้ได้เฉพาะคนในตารางนี้
CREATE TABLE IF NOT EXISTS `promo_recipients` (
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ช่วงราคาพิเศษ (Part 43): ปรับราคาห้องทุกประเภท +/− % ในช่วงวันที่ (ซ้อนกัน → ใช้ % สูงสุด)
CREATE TABLE IF NOT EXISTS `rate_seasons` (
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------- 1.1) อัปเกรดฐานข้อมูลเดิม (Part 13)
-- ฐานข้อมูลที่สร้างไว้ก่อนหน้า: เพิ่มคอลัมน์ใหม่ให้เฉพาะที่ยังไม่มี (เพิ่มอย่างเดียว ไม่ลบ/ไม่แก้ข้อมูลเดิม รันซ้ำได้)
DROP PROCEDURE IF EXISTS `aurelle_add_column`;
DELIMITER $$
CREATE PROCEDURE `aurelle_add_column`(IN tbl VARCHAR(64), IN col VARCHAR(64), IN def TEXT)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS
                   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND COLUMN_NAME = col) THEN
        SET @ddl = CONCAT('ALTER TABLE `', tbl, '` ADD COLUMN `', col, '` ', def);
        PREPARE st FROM @ddl; EXECUTE st; DEALLOCATE PREPARE st;
    END IF;
END$$
DELIMITER ;
CALL `aurelle_add_column`('room_types', 'description', 'text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL');
CALL `aurelle_add_column`('room_types', 'amenities', 'varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL');
CALL `aurelle_add_column`('room_types', 'image_url', 'varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL');
CALL `aurelle_add_column`('room_types', 'weekend_price', 'decimal(10,2) NULL');
CALL `aurelle_add_column`('bookings', 'guests', 'tinyint NULL');
CALL `aurelle_add_column`('payments', 'slip_path', 'varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL');
CALL `aurelle_add_column`('payments', 'reference', 'varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL');
-- Part 19: ตราประจำเซสชัน เปลี่ยนเมื่อเปลี่ยน/ออกรหัสผ่านใหม่ หรือสั่งออกจากระบบทุกอุปกรณ์ → เซสชันเก่าใช้ไม่ได้ทันที
CALL `aurelle_add_column`('users', 'security_stamp', 'varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL');
-- Part 34: หมวด / คำอธิบาย / รูป ของบริการเสริม
CALL `aurelle_add_column`('extra_services', 'category', 'varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL');
CALL `aurelle_add_column`('extra_services', 'description', 'varchar(300) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL');
CALL `aurelle_add_column`('extra_services', 'image_url', 'varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL');
CALL `aurelle_add_column`('promotions', 'promo_code', 'varchar(30) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL');
CALL `aurelle_add_column`('promotions', 'description', 'varchar(300) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL');
CALL `aurelle_add_column`('promotions', 'min_nights', 'smallint NULL');
CALL `aurelle_add_column`('promotions', 'max_uses', 'int NULL');
CALL `aurelle_add_column`('promotions', 'max_discount', 'decimal(10,2) NULL');
-- Part 38: จำกัดต่อลูกค้า / รหัสเฉพาะผู้ได้รับ
CALL `aurelle_add_column`('promotions', 'max_per_user', 'smallint NULL');
CALL `aurelle_add_column`('promotions', 'recipients_only', 'tinyint(1) NOT NULL DEFAULT 0');
-- Part 49: คูปองกดเก็บ (claimable + ช่วงเวลาเก็บ) · แบนเนอร์หน้าแรก (featured) · แคมเปญเทศกาลอัตโนมัติ (auto_key)
CALL `aurelle_add_column`('promotions', 'claimable', 'tinyint(1) NOT NULL DEFAULT 0');
CALL `aurelle_add_column`('promotions', 'claim_from', 'date NULL');
CALL `aurelle_add_column`('promotions', 'claim_until', 'date NULL');
CALL `aurelle_add_column`('promotions', 'featured', 'tinyint(1) NOT NULL DEFAULT 0');
CALL `aurelle_add_column`('promotions', 'auto_key', 'varchar(40) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL, ADD UNIQUE KEY `ux_promo_auto` (`auto_key`)');
-- Part 50: เช็คอินออนไลน์ล่วงหน้า (เวลาที่จะมาถึง / คำขอพิเศษ)
CALL `aurelle_add_column`('bookings', 'arrival_time', 'varchar(5) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL');
CALL `aurelle_add_column`('bookings', 'guest_request', 'varchar(300) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL');
-- Part 53-54: ฝ่ายส่งของ / แม่บ้าน
CALL `aurelle_add_column`('delivery_tasks', 'created_at', 'datetime NULL DEFAULT CURRENT_TIMESTAMP');
CALL `aurelle_add_column`('delivery_tasks', 'picked_at', 'datetime NULL');
CALL `aurelle_add_column`('delivery_tasks', 'urgent', 'tinyint(1) NOT NULL DEFAULT 0');
CALL `aurelle_add_column`('delivery_tasks', 'missed', 'tinyint unsigned NOT NULL DEFAULT 0');
CALL `aurelle_add_column`('housekeeping_tasks', 'created_at', 'datetime NULL DEFAULT CURRENT_TIMESTAMP');
CALL `aurelle_add_column`('housekeeping_tasks', 'picked_at', 'datetime NULL');
CALL `aurelle_add_column`('housekeeping_tasks', 'urgent', 'tinyint(1) NOT NULL DEFAULT 0');
CALL `aurelle_add_column`('housekeeping_tasks', 'missed', 'tinyint unsigned NOT NULL DEFAULT 0');
CALL `aurelle_add_column`('housekeeping_tasks', 'kind', 'varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL');
CALL `aurelle_add_column`('housekeeping_tasks', 'pref_time', 'varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL');
CALL `aurelle_add_column`('housekeeping_tasks', 'note', 'varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL');
CALL `aurelle_add_column`('bookings', 'do_not_disturb', 'tinyint(1) NOT NULL DEFAULT 0');
-- Part 60: บิลระหว่างพัก (สั่งบริการเพิ่มระหว่างพัก + ค่ากิจกรรม ชำระตอนเช็คเอาท์)
CALL `aurelle_add_column`('booking_services', 'in_stay', 'tinyint(1) NOT NULL DEFAULT 0');
CALL `aurelle_add_column`('booking_services', 'paid_at', 'datetime NULL');
CALL `aurelle_add_column`('booking_services', 'pay_method', 'varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL');
CALL `aurelle_add_column`('booking_services', 'paid_by', 'int NULL');
CALL `aurelle_add_column`('activity_bookings', 'paid_at', 'datetime NULL');
CALL `aurelle_add_column`('activity_bookings', 'pay_method', 'varchar(10) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL');
CALL `aurelle_add_column`('activity_bookings', 'paid_by', 'int NULL');
DROP PROCEDURE IF EXISTS `aurelle_add_column`;

-- Part 14: สถานะ REJECTED = ฝ่ายบัญชีปฏิเสธสลิป (เก็บไว้เป็นประวัติ ไม่ลบทิ้ง)
DROP PROCEDURE IF EXISTS `aurelle_payment_rejected`;
DELIMITER $$
CREATE PROCEDURE `aurelle_payment_rejected`()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'payments'
                   AND COLUMN_NAME = 'status' AND COLUMN_TYPE LIKE '%REJECTED%') THEN
        ALTER TABLE `payments` MODIFY COLUMN `status` enum('PENDING','PAID','REFUNDED','REJECTED')
            CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'PENDING';
    END IF;
END$$
DELIMITER ;
CALL `aurelle_payment_rejected`();
DROP PROCEDURE IF EXISTS `aurelle_payment_rejected`;

-- Part 33: สถานะ NO_SHOW = ชำระแล้วแต่ไม่มาเข้าพัก (เพิ่มค่าใหม่ ค่าเดิมไม่เปลี่ยน)
DROP PROCEDURE IF EXISTS `aurelle_booking_noshow`;
DELIMITER $$
CREATE PROCEDURE `aurelle_booking_noshow`()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'bookings'
                   AND COLUMN_NAME = 'status' AND COLUMN_TYPE LIKE '%NO_SHOW%') THEN
        ALTER TABLE `bookings` MODIFY COLUMN `status` enum('PENDING','CONFIRMED','CHECKED_IN','CHECKED_OUT','CANCELLED','NO_SHOW')
            CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'PENDING';
    END IF;
END$$
DELIMITER ;
CALL `aurelle_booking_noshow`();
DROP PROCEDURE IF EXISTS `aurelle_booking_noshow`;

-- Part 37: โปรโมชั่นแบบใหม่ (ช่วงเทศกาล / พักยาว / รหัสโปรโมชั่น) + เงื่อนไขเพิ่ม — เพิ่มค่า/คอลัมน์อย่างเดียว
DROP PROCEDURE IF EXISTS `aurelle_promo_v2`;
DELIMITER $$
CREATE PROCEDURE `aurelle_promo_v2`()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'promotions'
                   AND COLUMN_NAME = 'promo_type' AND COLUMN_TYPE LIKE '%CODE%') THEN
        ALTER TABLE `promotions` MODIFY COLUMN `promo_type` enum('MEMBER','MOTHERS_DAY','EARLY_BIRD','BIRTHDAY','ANNIVERSARY','SEASONAL','LONG_STAY','CODE') CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL;
    END IF;
END$$
DELIMITER ;
CALL `aurelle_promo_v2`();
DROP PROCEDURE IF EXISTS `aurelle_promo_v2`;

-- ---------------------------------------------------------------- 2) ข้อมูลตั้งต้นที่ระบบต้องมี
-- บทบาท 6 แบบ (ชื่อต้องตรงกับในโค้ดทุกตัวอักษร)
INSERT IGNORE INTO `roles` (`role_name`) VALUES
  ('customer'), ('admin'), ('accounting'), ('frontdesk'), ('delivery'), ('housekeeping');

-- โปรโมชั่น 5 ประเภท (ผู้จัดการแก้ % / เปิด-ปิด / ช่วงวันที่ ได้ที่หน้า /AdminPromotions)
-- วันแม่ตั้งไว้ 1–31 ส.ค. ของปีนี้ (ถ้าเลยไปแล้วใช้ของปีหน้า) เป็นค่าตั้งต้น ให้แก้เป็นช่วงที่ต้องการจริง
INSERT INTO `promotions` (`promo_name`,`promo_type`,`discount_percent`,`min_advance_days`,`need_staff_confirm`,`start_date`,`end_date`,`is_active`)
SELECT * FROM (
  SELECT 'ส่วนลดสมาชิก' AS promo_name,'MEMBER' AS promo_type,10.00 AS discount_percent,NULL AS min_advance_days,0 AS need_staff_confirm,NULL AS start_date,NULL AS end_date,1 AS is_active
  UNION ALL SELECT 'โปรโมชั่นวันแม่','MOTHERS_DAY',20.00,NULL,0,
    STR_TO_DATE(CONCAT(YEAR(CURDATE()) + (CURDATE() > STR_TO_DATE(CONCAT(YEAR(CURDATE()),'-08-31'),'%Y-%m-%d')), '-08-01'),'%Y-%m-%d'),
    STR_TO_DATE(CONCAT(YEAR(CURDATE()) + (CURDATE() > STR_TO_DATE(CONCAT(YEAR(CURDATE()),'-08-31'),'%Y-%m-%d')), '-08-31'),'%Y-%m-%d'),1
  UNION ALL SELECT 'จองล่วงหน้า (Early Bird)','EARLY_BIRD',20.00,30,0,NULL,NULL,1
  UNION ALL SELECT 'ส่วนลดวันเกิด','BIRTHDAY',15.00,NULL,1,NULL,NULL,1
  UNION ALL SELECT 'ส่วนลดครบรอบ','ANNIVERSARY',15.00,NULL,1,NULL,NULL,1
) AS v
WHERE NOT EXISTS (SELECT 1 FROM `promotions` p WHERE p.`promo_type` = v.promo_type);

-- ---------------------------------------------------------------- 3) ตั้งผู้จัดการคนแรก
-- ขั้นตอน: สมัครสมาชิกที่หน้าเว็บก่อน แล้วแก้อีเมลด้านล่างให้ตรง ลบ -- หน้าบรรทัดออก แล้วรัน
-- UPDATE `users` SET `role_id` = (SELECT `role_id` FROM `roles` WHERE `role_name` = 'admin')
-- WHERE `email` = 'อีเมลที่สมัครไว้@example.com';
