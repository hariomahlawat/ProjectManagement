-- Run as a PostgreSQL administrator before applying the MediaLibraryDbContext migration.
SELECT name, default_version, installed_version
FROM pg_available_extensions
WHERE name = 'vector';

CREATE EXTENSION IF NOT EXISTS vector;

SELECT extname, extversion
FROM pg_extension
WHERE extname = 'vector';
