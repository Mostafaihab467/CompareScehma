# SchemaCompare — agent instructions

Avalonia 12 / net10.0 C# desktop app that compares, syncs and manages SQL Server
databases (SSMS-parity features).

## Read these before exploring

Do not re-analyze the repository from scratch — the navigation layer already exists:

1. [`docs/README.md`](docs/README.md) — folder layout, architecture conventions,
   "where to look for a feature" table, verification rules.
2. [`docs/INDEX.md`](docs/INDEX.md) — every source file, one line each.
3. `docs/files/<path>.md` — per-file pointer: purpose, declared types, public
   surface, named controls, and which files reference it. Open the source only
   after the pointer tells you it is the right file.
4. [`PRODUCTION.md`](PRODUCTION.md) — what is verified working vs not built
   (Tier 1 / 2 / 3). This is the source of truth for feature status.
5. [`FEATURES.md`](FEATURES.md) — product roadmap behind the tiers.

Regenerate the pointer pages after moving/renaming code: add a purpose line to
`docs/purposes.py`, then `python docs/build_docs.py`.

## Build

A bare `dotnet build` in this folder fails with MSB1011 (both `.sln` and
`.csproj` are present). Use the project file and an explicit output directory:

```
dotnet build SchemaCompare.csproj -c Debug -p:OutDir="<scratch>/app/"
```

New `.cs`/`.axaml` files are picked up automatically (SDK-style glob); only new
embedded resources need a `.csproj` entry. `Views/*.axaml` dialog pages warn
`AVLN3001 … no public constructor` by design — dialogs have a private ctor and a
static `ShowAsync`.

## Code conventions

- Namespace mirrors folder: `SchemaCompare.Views`, `.Controls`, `.ViewModels`,
  `.Models`, `.Services`.
- `Services/ManagerScriptBuilder.cs` holds pure T-SQL builders: the script the
  user previews is the script that runs. Do not build SQL in views or VMs.
- View-models expose host hooks (`Func<…>? ShowXxxAsync`) and **fail closed** with
  an explicit status message when the window has not attached one.
- Data dialogs: `Window` subclass, private constructor, `static Task<T?> ShowAsync(owner, …)`.
- Parameters in SQL are always `SqlCommand` parameters, never string interpolation.

## Testing rules (non-negotiable)

- Verification is the headless harness (`ReproSsms`, in the agent scratch
  directory): build the app, build the harness, run it, and read the
  `PASS`/`FAIL`/`FATAL` lines plus the screenshots it writes. A feature is
  "done" only when the harness asserts it.
- The harness must never restore a database, never `KILL`, never call
  `SavedConnectionsService.Save()`, and must redirect `AppLog` to a temp probe dir.
- Local test instance: `sqlcmd -S localhost` (shared memory), database
  `EgyptMart`, Windows auth.
- Never leave credentials in code, logs, or committed files; log lines must not
  contain passwords.
