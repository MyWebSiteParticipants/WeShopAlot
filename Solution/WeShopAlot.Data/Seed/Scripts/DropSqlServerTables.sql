-- Drops every WeShopAlot table so the migrations can rebuild the schema from scratch.
--
-- Scope: the WSA schema only (every WeShopAlot table lives there, including EF Core's
-- __EFMigrationsHistory, which follows the model's default schema), plus a history table
-- in dbo left by older builds. Tables in any other schema are not touched.
--
-- Run by DatabaseSeeder.RecreateAsync. It is one batch (no GO), so EF Core can send it as is.

DECLARE @sql nvarchar(max) = N'';

-- 1. Foreign keys first, or the table drops fail on the referenced tables.
SELECT @sql += N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name)
             + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';' + CHAR(10)
FROM sys.foreign_keys AS fk
JOIN sys.tables  AS t ON fk.parent_object_id = t.object_id
JOIN sys.schemas AS s ON t.schema_id = s.schema_id
WHERE s.name = N'WSA';
EXEC sys.sp_executesql @sql;

-- 2. Then the tables themselves.
SET @sql = N'';
SELECT @sql += N'DROP TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';' + CHAR(10)
FROM sys.tables  AS t
JOIN sys.schemas AS s ON t.schema_id = s.schema_id
WHERE s.name = N'WSA';
EXEC sys.sp_executesql @sql;

-- 3. A migrations history table in dbo, if an older build left one there.
DROP TABLE IF EXISTS [dbo].[__EFMigrationsHistory];
