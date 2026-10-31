# Third-Party Software / 第三方开源组件

CA-Manager v1.7.1 uses the following open-source components.
本系统使用了以下开源组件，感谢这些项目的作者。

## Backend (.NET 10) — 后端

| Component | Version | License | Purpose |
|-----------|---------|---------|---------|
| BouncyCastle.Cryptography | 2.7.0 | MIT | PGP key generation, CSR/cert/CRL parsing |
| MailKit (incl. MimeKit) | 4.18.1 | MIT | SMTP email notifications |
| Microsoft.EntityFrameworkCore.Sqlite (incl. EF Core 10.0.12) | 10.0.12 | MIT | Database access |
| Microsoft.Data.Sqlite.Core / SQLitePCLRaw.bundle_e_sqlite3 | 10.0.12 / 2.1.12 | MIT / Public Domain | SQLite engine |
| Microsoft.Extensions.Hosting.WindowsServices | 10.0.12 | MIT | Windows service hosting |
| Microsoft.Extensions.Identity.Core | 10.0.12 | MIT | Password hashing (PBKDF2) |
| System.DirectoryServices.Protocols | 10.0.12 | MIT | LDAP authentication |
| System.Security.Cryptography.Pkcs | 10.0.0 | MIT | PKCS support (transitive) |

Runtime: .NET 10 runtime (self-contained deployment). Frontend assets are served by
ASP.NET Core Kestrel (Apache-2.0).

## Frontend (Vue 3) — 前端

| Component | Version | License | Purpose |
|-----------|---------|---------|---------|
| vue | 3.5.43 | MIT | UI framework |
| element-plus | 2.14.7 | MIT | UI component library |
| @element-plus/icons-vue | 2.3.2 | MIT | Icons |
| echarts | 5.6.0 | Apache-2.0 | Dashboard charts |
| vue-i18n | 11.4.13 | MIT | Internationalization |
| vue-router | 4.6.4 | MIT | Routing |
| vite (build tool) | 6.4.3 | MIT | Frontend build |
| @vitejs/plugin-vue | 5.2.4 | MIT | Vite plugin |

Build-time only (not shipped): Node.js, npm.

## Full license texts
All packages ship their license files inside the NuGet/npm packages
(`~/.nuget/packages/<id>/<ver>/` and `node_modules/<pkg>/LICENSE*`).

## MIT License (summary)
Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files, to deal in the software without
restriction. The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.
