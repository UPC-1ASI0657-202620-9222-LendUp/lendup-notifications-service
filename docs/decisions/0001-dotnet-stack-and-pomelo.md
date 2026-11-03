# ADR 0001: C# · ASP.NET Core con EF Core 9 y Pomelo

Estado: aceptada.

## Contexto

Notificaciones se implementa en C# · ASP.NET Core sobre .NET 10 (LTS) para mantener el argumento de CD-11 (tecnologías distintas por servicio, unidas por contratos REST y eventos). Se pidió MySQL con el proveedor Pomelo. La última versión estable de `Pomelo.EntityFrameworkCore.MySql` es la 9.0.0, que exige EF Core 9.0.x; no existe todavía una versión para EF Core 10.

## Decisión

- .NET 10 para la aplicación y ASP.NET Core; **EF Core 9.0.22 con Pomelo 9.0.0** para la persistencia (EF Core 9 funciona sobre el runtime de .NET 10).
- `MySqlConnector` fijado en 2.6.2: la 2.4.0 que trae Pomelo tiene la vulnerabilidad GHSA-473q-m89c-ghf8 y la compilación trata los avisos de seguridad como error.
- Migraciones de EF Core (`Infrastructure/Persistence/Migrations`), aplicadas al arrancar o como script.
- Misma forma de repositorio, variables de entorno, rutas de health y formato de error que los servicios Java.

## Consecuencias

EF Core 9 deja de tener soporte de Microsoft el 10-nov-2026. Cuando Pomelo publique su versión para EF Core 10, basta con subir los paquetes (`Pomelo`, `Microsoft.EntityFrameworkCore.*`, `dotnet-ef`) y regenerar el snapshot; el modelo y las migraciones no cambian. La alternativa evaluada fue el proveedor oficial de Oracle (`MySql.EntityFrameworkCore` 10.x), que sí soporta EF Core 10, pero se mantuvo Pomelo por ser el requerido y el más usado.
