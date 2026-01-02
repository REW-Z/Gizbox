# .github/upgrades/plan.md

TOC

- Executive Summary
- Migration Strategy
- Detailed Dependency Analysis
- Project-by-Project Plans
- Package Update Reference
- Breaking Changes Catalog
- Testing & Validation Strategy
- Risk Management
- Complexity & Effort Assessment
- Source Control Strategy
- Success Criteria
- Appendices

---

## Executive Summary

Selected Strategy
**All-At-Once Strategy** - All projects that require framework changes will be upgraded simultaneously in a single atomic operation.

Rationale:
- Solution size: 4 projects (small)
- Projects requiring upgrade: 2 (GizboxLSP, GizboxTest)
- Existing projects are SDK-style and homogeneous
- Assessment shows package updates available (Newtonsoft.Json 13.0.3 → 13.0.4)
- User requested direct upgrade to `net10.0` and requested no git branch creation; plan reflects this constraint (see Source Control Strategy)

Scope
- Projects to change target framework to `net10.0`: `GizboxLSP` and `GizboxTest` (as identified in assessment)
- Projects that remain on `netstandard2.0`: `Gizbox` and `GizboxAOT`
- Package updates: `Newtonsoft.Json 13.0.3 -> 13.0.4` for `GizboxLSP`

Key constraints and notes
- The user explicitly requested not to create a git branch; Plan documents source-control implications and recommends safeguards.
- All framework and package updates will be applied in one coordinated operation (atomic upgrade).

## Migration Strategy

Overview
- Use All-At-Once Strategy: update all project TargetFramework values and package versions in a single coordinated change set, then restore and build the whole solution and address compilation errors in the same operation.

Phases (conceptual, not task splits)
- Phase 0: Preparation (local SDK, backups, commit working tree)
- Phase 1: Atomic Upgrade (apply all project and package updates together)
- Phase 2: Test Validation (run unit tests and address test failures)

Atomicity principle
- All TargetFramework updates and NuGet package version updates are performed together to avoid intermediate compatibility states. Compilation and fixes are part of the same atomic pass.

## Detailed Dependency Analysis

Summary
- Total projects: 4
- Projects depending on `Gizbox` (netstandard2.0): `GizboxLSP`, `GizboxTest`, `GizboxAOT`
- Dependency graph is shallow and acyclic. Leaves/roots identified below.

Migration grouping (single atomic scope)
- All projects that require a target framework change are upgraded simultaneously: `GizboxLSP`, `GizboxTest`.
- `Gizbox` and `GizboxAOT` remain on `netstandard2.0` and are not targeted for framework change in this plan.

Critical path
- Because `Gizbox` is consumed by three projects, ensure its public API remains compatible. It remains `netstandard2.0`, so no immediate change expected.

## Project-by-Project Plans

The following sections provide precise, actionable migration guidance for each project. The plan follows the All-At-Once Strategy: update all targeted projects simultaneously.

### `F:\Legacy\MyProjects\Gizbox\Gizbox\Gizbox.csproj` (Class Library, netstandard2.0)

Current state
- TargetFramework: `netstandard2.0`
- SDK-style: True
- Referenced by: `GizboxLSP`, `GizboxTest`, `GizboxAOT`

Target state
- Remain `netstandard2.0` (no change)

Validation
- After atomic upgrade, build solution and confirm no breaking usage from updated projects.
- Run unit tests that reference this library.

Migration steps (if later conversion to net10.0 desired)
- Evaluate public API surface for net10.0 compatibility
- Update TargetFramework to `net10.0` and follow same atomic upgrade steps

### `F:\Legacy\MyProjects\Gizbox\GizboxLSP\GizboxLSP.csproj` (Application, net8.0 -> net10.0)

Current state
- TargetFramework: `net8.0`
- SDK-style: True
- Packages: `Newtonsoft.Json 13.0.3`

Target state
- TargetFramework: `net10.0`
- Packages: `Newtonsoft.Json 13.0.4`

Migration steps (detailed)
1. Update project file
   - Change `<TargetFramework>net8.0</TargetFramework>` to `<TargetFramework>net10.0</TargetFramework>` in `GizboxLSP.csproj`.
2. Update package reference
   - Update `Newtonsoft.Json` PackageReference to `13.0.4`.
3. Check MSBuild imports and Directory.Build.*
   - Ensure no conditional imports depend on target framework values. Adjust conditions that reference `net8.0` explicitly.
4. Restore and build (atomic pass)
   - Restore NuGet packages for the solution.
   - Build the entire solution to surface compile-time issues.
5. Resolve compiler errors
   - Address source-incompatible API errors (see Breaking Changes Catalog). Typical fixes: update call sites, use alternative APIs, add using directives, adapt overload calls.
6. Address behavioral changes at runtime
   - Add/modify unit tests for `System.Uri` and other flagged behaviors. Use `Uri.TryCreate` and explicit `UriKind` where needed.
7. Verify package compatibility
   - Confirm no new vulnerabilities introduced by the package update.
8. Run unit tests for `GizboxLSP` and dependent test projects.

Validation checklist
- TargetFramework set to `net10.0`
- `Newtonsoft.Json` at `13.0.4`
- Build: 0 compile errors
- Tests: pass

Notes
- If conditional compilation symbols reference `NET8_0`, update them to appropriate `NET10_0` or adjust multi-targeting approach if used.

### `F:\Legacy\MyProjects\Gizbox\GizboxAOT\GizboxAOT.csproj` (Class Library, netstandard2.0)

Current state
- TargetFramework: `netstandard2.0`
- No package updates required

Target state
- Remain netstandard2.0

Validation
- Ensure build and unit tests referencing this project succeed after the atomic upgrade.

### `F:\Legacy\MyProjects\Gizbox\GizboxTest\GizboxTest.csproj` (Test project, net8.0 -> net10.0)

Current state
- TargetFramework: `net8.0`
- Test runner packages: none flagged

Target state
- TargetFramework: `net10.0`

Migration steps
1. Update `<TargetFramework>` to `net10.0` in project file.
2. Restore and build as part of atomic upgrade.
3. If test runner incompatibilities appear, update test framework packages (e.g., `Microsoft.NET.Test.Sdk`, `xunit` or `nunit` adapters) as needed to versions compatible with net10.0.
4. Execute test suite and fix failures.

Validation checklist
- TargetFramework set to `net10.0`
- Build: 0 compile errors
- Tests: pass

## Package Update Reference (detailed)

This section consolidates package updates across the solution. Use exact versions from assessment.

- `Newtonsoft.Json`
  - Current: `13.0.3`
  - Target: `13.0.4`
  - Projects affected: `GizboxLSP`
  - Reason: Suggested update in assessment; minor version bump likely compatible; security/bug fixes in patch.
  - Validation: Run unit tests; verify JSON serialization behavior unchanged.

- `LLVMSharp`
  - Current: `16.0.0`
  - Target: (no change)
  - Projects: `GizboxAOT`
  - Reason: Compatible per assessment; no update required.

- `NETStandard.Library`
  - Current: `2.0.3`
  - Target: (no change)
  - Projects: `Gizbox`, `GizboxAOT` (transitive)
  - Reason: Netstandard projects remain unchanged.

## Breaking Changes Catalog (detailed)

1. `System.TimeSpan.FromSeconds(Double)` - Source incompatibility
   - Impact: Call sites that depended on implicit conversions or changed overloads may fail to compile.
   - Remediation guidance: Replace ambiguous calls with explicit `TimeSpan.FromSeconds(x)` usage or cast arguments to `double` where needed; review compiler errors.

2. `System.Uri` constructor behavior change
   - Impact: `new Uri(string)` may throw where it previously produced relative URIs or accepted different inputs.
   - Remediation guidance: Use `Uri.TryCreate` with `UriKind.RelativeOrAbsolute` and validate before use; add defensive code around parsing.

3. `Api.0002` and `Api.0003` (from analysis)
   - Source: Analysis rules flagged these for `GizboxLSP`.
   - Impact: Medium risk; must be resolved during atomic build.
   - Remediation guidance: Inspect rule details in assessment.md and adapt code accordingly.

## Testing & Validation Strategy

Test levels
- Per-project unit tests: Run test projects after the atomic upgrade. `GizboxTest` must be executed and green.
- Full-solution build: Required after atomic upgrade; success criterion: solution builds with 0 errors.

Validation checklist (for executor)
- [ ] Update project TargetFramework values as specified
- [ ] Update package references as specified
- [ ] `dotnet restore` succeeds
- [ ] Build entire solution: 0 compile errors
- [ ] Run unit tests: all tests pass
- [ ] No new high/critical security vulnerabilities in NuGet packages

Test execution guidance
- Run unit tests for `GizboxTest` and any other discovered test projects. Fix test failures before considering migration complete.

## Risk Management

Identified risks
- Risk: No branch created (user requested) — increases risk of losing working state or making unreviewed changes in `master`.
  - Risk level: High
  - Mitigation: Create a local backup, ensure a local commit or stash before applying changes; if repository policy disallows direct changes to `master`, reconsider.

- Risk: Source-incompatible API in `GizboxLSP` leading to compile errors.
  - Risk level: Medium
  - Mitigation: Keep compilation/fix step part of atomic upgrade; prepare developer(s) to resolve errors found in one pass.

- Risk: Behavioral changes (Uri/TimeSpan) — low/medium; require runtime validation and focused tests.
  - Mitigation: Add targeted unit tests for affected areas and run integration checks.

Rollbacks and contingency
- If upgrade introduces blocking compilation or test failures that cannot be resolved quickly, revert changes in source control or restore from the pre-upgrade backup (see Source Control Strategy).

## Complexity & Effort Assessment

Relative complexity (no time estimates):
- `GizboxLSP` — Medium (source + behavioral issues; some code changes expected)
- `GizboxTest` — Low (project file change and test runner validation)
- `Gizbox` — Low (no changes expected, but verify compatibility)
- `GizboxAOT` — Low (no changes expected)

## Source Control Strategy

User constraint: Do NOT create a new git branch. Plan respects this instruction and documents implications.

Recommended practitioner steps prior to applying changes (executor actions, not performed by planner):
- Create a local commit of current `master` state or create a local backup copy of repository tree before applying the atomic upgrade (this preserves ability to roll back). Even if no new branch is requested, a commit or stash is strongly recommended.
- If organizational policy requires PRs, consider creating a branch despite the request; otherwise perform the atomic changes directly on `master` per user instruction.

Note: The scenario guidance originally recommends creating an upgrade branch. The user-specific requirement "不要创建git分支" is respected by this plan, but it increases risk. The executor should explicitly acknowledge the tradeoff.

## Success Criteria

The upgrade is complete when all of the following are true:
1. `GizboxLSP` and `GizboxTest` project files target `net10.0` exactly.
2. `Newtonsoft.Json` updated to `13.0.4` in `GizboxLSP`.
3. `dotnet restore` completes successfully for the solution.
4. The full solution builds with 0 compilation errors.
5. All unit tests (including `GizboxTest`) pass.
6. No outstanding security vulnerabilities flagged for updated packages.

## Appendices

- Assessment source: `.github/upgrades/assessment.md` (analysis used to create this plan)
- Notable issues extracted: `Api.0002`, `Api.0003`, `NuGet.0002`, `Project.0002`


---

[End of plan.md]
