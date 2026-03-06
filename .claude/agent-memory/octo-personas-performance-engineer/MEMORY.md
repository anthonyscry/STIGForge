# Performance Engineer Memory - STIGForge

## Project Structure
- 12 source projects under `/home/anthonyscry/projects/STIGForge/src/`
- ~38K lines of C# (1442 files), .NET 8, WPF desktop app + CLI
- SQLite via Dapper (no EF Core), WAL mode enabled
- Key hot paths: XML import/parsing, ZIP extraction, DB bulk writes, hash manifests

## Key Performance Observations (2026-03-06 audit)
- DB: 42 `new SqliteConnection` calls = no connection pooling (open/close per query)
- DB: SaveControlsAsync in SqliteJsonControlRepository loops individual inserts inside tx
- SHA256: Repeated `SHA256.Create()` allocation across 8+ files
- JsonSerializerOptions: 70+ allocations of `new JsonSerializerOptions` per call
- ZIP extraction: Synchronous `CopyTo` instead of async `CopyToAsync`
- XML: XDocument.Load (DOM) used for large OVAL/SCAP files
- AuditTrail.VerifyIntegrityAsync: loads entire audit chain into memory
- File I/O: Many `File.ReadAllText` calls for JSON (full string in memory)

## Architecture Notes
- DbConnectionString is a wrapper type registered as singleton in DI
- DbBootstrap.EnsureCreated runs schema DDL on startup (synchronous)
- All repos are registered as Singleton in App.xaml.cs DI
