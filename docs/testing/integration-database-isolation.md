# Integration database isolation

Each integration-test assembly creates a unique PostgreSQL schema when its `DatabaseFixture`
starts. The schema name includes the test assembly, process ID, and a random suffix. Every Core,
Billing, and Scheduling connection—including in-process API hosts—uses that schema through the
PostgreSQL `Search Path` connection setting.

This allows `dotnet test Shine.Backend.slnx` to execute test assemblies concurrently without one
assembly's migrations, cleanup, or pending worker records affecting another. The fixture drops
its generated schema with `CASCADE` after the assembly completes. An aborted test host can leave
an orphan test schema, but a later run always receives a new name and remains isolated.

The CI regression command is the aggregate solution command, so the parallel path is exercised
on every pull request. `scripts/Test-Backend.ps1` remains available for sequential diagnosis.
