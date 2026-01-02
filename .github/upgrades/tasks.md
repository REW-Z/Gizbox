# Gizbox .NET net10.0 Upgrade Tasks

## Overview

This document lists executable tasks to perform the All-At-Once atomic upgrade of the Gizbox solution: update target frameworks and package versions for the identified projects, then restore, build, and run tests to validate the upgrade.

**Progress**: 0/4 tasks complete (0%) ![0%](https://progress-bar.xyz/0)

---

## Tasks

### [▶] TASK-001: Verify prerequisites
**References**: Plan §Migration Strategy, Plan §Project-by-Project Plans, Plan §Source Control Strategy

- [✓] (1) Verify required `.NET` SDK that supports `net10.0` is installed per Plan §Migration Strategy.
- [▶] (2) Installed SDK version meets minimum requirements for `net10.0` (**Verify**).
- [ ] (3) Check for a `global.json` file and confirm its SDK pinning is compatible with the target SDK per Plan §Migration Strategy (update guidance only; do not perform source control operations).
- [ ] (4) Verify required CLI/tools (e.g., `dotnet` toolchain) are available and meet version requirements (**Verify**).

---

### [ ] TASK-002: Atomic framework and package upgrade with compilation fixes
**References**: Plan §Migration Strategy, Plan §Project-by-Project Plans, Plan §Package Update Reference, Plan §Breaking Changes Catalog

- [ ] (1) Update `<TargetFramework>` to `net10.0` in `GizboxLSP.csproj` and `GizboxTest.csproj` per Plan §Project-by-Project Plans.
- [ ] (2) All targeted project files updated to `net10.0` (**Verify**).
- [ ] (3) Update `Newtonsoft.Json` PackageReference to `13.0.4` in `GizboxLSP` per Plan §Package Update Reference.
- [ ] (4) All package references specified in the plan updated (**Verify**).
- [ ] (5) Run `dotnet restore` for the solution to restore updated dependencies per Plan §Testing & Validation Strategy.
- [ ] (6) Restore completes successfully (**Verify**).
- [ ] (7) Build the entire solution and fix all compilation errors arising from framework and package changes (address items in Plan §Breaking Changes Catalog: `TimeSpan`, `System.Uri`, `Api.0002`, `Api.0003`, etc.).
- [ ] (8) Solution builds with 0 errors (**Verify**)

---

### [ ] TASK-003: Run tests and validate upgrade
**References**: Plan §Testing & Validation Strategy, Plan §Project-by-Project Plans, Plan §Breaking Changes Catalog

- [ ] (1) Run tests in the `GizboxTest` test project (and any other test projects referenced in the plan) per Plan §Testing & Validation Strategy.
- [ ] (2) Fix any test failures (reference remediation guidance in Plan §Breaking Changes Catalog).
- [ ] (3) Re-run tests after fixes.
- [ ] (4) All tests pass with 0 failures (**Verify**)

---

### [ ] TASK-004: Final commit
**References**: Plan §Source Control Strategy

- [ ] (1) Commit all remaining changes with message: "TASK-004: Complete atomic upgrade to `net10.0` and package updates"

