# Extending the Module

The following guidelines help when tailoring the authentication and user management features.

* **Adding new roles** – update `IdentitySeeder` and run the seeder again or create migration scripts.
* **Custom user properties** – extend `ApplicationUser` and update any forms or view models accordingly.
* **Alternative email providers** – implement `IEmailSender` and register it in `Program.cs`.
* **Additional account pages** – place new Razor Pages under `Areas/Identity/Pages/Account` and secure them with the existing Identity configuration.
