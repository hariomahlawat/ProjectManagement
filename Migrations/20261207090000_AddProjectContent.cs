// Compatibility neutralizer for the superseded Phase 11 migration.
//
// The original Phase 11 package used migration ID
// 20261207090000_AddProjectContent, which collides with
// 20261207090000_AddArppFoundation.
//
// The actual Project Content migration is now:
// Migrations/20261207140000_AddProjectContent.cs
//
// This comment-only file safely overwrites the obsolete migration during a
// manual folder copy. It may be deleted after the corrected package is applied.
