-- Drops every WeShopAlot table so the migrations can rebuild the schema from scratch.
--
-- Scope: the "WSA" schema (every WeShopAlot table lives there, including EF Core's
-- "__EFMigrationsHistory", which follows the model's default schema), plus a history
-- table in public left by older builds. The first migration recreates the schema.
--
-- Run by DatabaseSeeder.RecreateAsync.

DROP SCHEMA IF EXISTS "WSA" CASCADE;
DROP TABLE IF EXISTS public."__EFMigrationsHistory";
