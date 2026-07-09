# rocloud-api

The backend Web API for **ROCloud**, a multi-tenant SaaS platform for RO (reverse
osmosis) water delivery businesses in India. Built on .NET 10 using Clean
Architecture (Domain / Application / Infrastructure / API), it serves both the RO
Owner Portal and the Super Admin Portal over a REST interface. It handles
authentication (custom JWT + Google OAuth), multi-tenant data isolation, customer
and order management, deliveries, inventory, GST invoicing, payments (Razorpay),
AMC service requests, reporting, and background jobs — backed by PostgreSQL 16,
in-memory caching, and local file storage for v1.
