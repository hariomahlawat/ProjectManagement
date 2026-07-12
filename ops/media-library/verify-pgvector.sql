-- Future People intelligence readiness check only.
-- This core external-folder release neither requires nor creates pgvector.
SELECT name, default_version, installed_version
FROM pg_available_extensions
WHERE name = 'vector';

SELECT extname, extversion
FROM pg_extension
WHERE extname = 'vector';
