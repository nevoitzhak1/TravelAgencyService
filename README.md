# TravelAgencyService 🌍✈️

A full-featured Travel Agency web system built with **ASP.NET Core MVC**, **Entity Framework Core**, and **SQL Server**, including online payments, booking management, recurring trip series, and admin dashboards.

## 🚀 Features

### 👤 User Side
- Browse trips by country, dates, and categories
- View trip details with images, prices, and availability
- Add trips to cart or buy instantly
- Secure checkout with PayPal
- Download PDF vouchers
- My Bookings page with upcoming & past trips
- Email confirmations

### 🛠️ Admin Side
- Create, edit, and manage trips
- Define **Recurring Trip Series** (same trip across multiple years)
- Bulk update shared fields across all occurrences
- Manage availability and waiting lists
- Dashboard with statistics
- Manage bookings and statuses
- Upload and manage trip images
- Configure reminders and trip rules

### 🔁 Recurring Trip System
- Each date is stored as a separate `Trip`
- All occurrences are linked using a shared `RecurringGroupKey` (GUID)
- Admin can:
  - Create multi-year trips in one action
  - Edit once and apply to all years
  - Keep bookings & availability per occurrence

### 💳 Payments
- PayPal integration (Cart + Buy Now)
- Order creation, approval, and capture flow
- Secure server-side verification

### 📄 PDF & Email
- Automatic PDF vouchers generation
- Email sending via SMTP (Gmail)
- Attachments and HTML templates

---

## 🧱 Tech Stack
- ASP.NET Core MVC
- Entity Framework Core
- SQL Server (Local & Azure)
- ASP.NET Identity
- PayPal REST API
- Razor Pages + Bootstrap

---

## ⚙️ Setup

1. Configure database in `appsettings.json`
2. Run `Update-Database`
3. Add PayPal & Email credentials
4. Run the project

---

## 👨‍💻 Authors

Nevo Itzhak  
Ayala Faber
