# Versioning & support policy

How JauntyQ is versioned, what compatibility you can rely on across upgrades,
and how support is provided. Commercial terms (tiers, lapse) are in
[pricing](../00-overview/pricing.md); this page covers the technical policy.

## Version scheme

JauntyQ follows Semantic Versioning (`MAJOR.MINOR.PATCH`):

- **MAJOR**, breaking changes to the generated API surface, the consumer
  MSBuild contract, or the snapshot format.
- **MINOR**, new features (new directives, new dialect capabilities, additional
  generated members) that do not break existing code.
- **PATCH**, bug fixes and diagnostics improvements with no API change.

JauntyQ is currently pre-1.0 (`0.x`). While pre-1.0, minor versions may include
breaking changes; each is called out in the [CHANGELOG](../../CHANGELOG.md).

**1.0 is a licensing boundary as well as a compatibility one.** Free preview
licenses, which make the paid commands free during the `0.x` preview, are scoped to
pre-1.0 releases: every `0.x` build honors one permanently, and `1.0` declines it
(see [licensing and activation](../03-guides/licensing-and-activation.md#preview-licenses-pre-10)).
The check reads the version compiled into the build via `LicenseProduct.Version`,
which is generated from MSBuild's `$(Version)`, so the `0.x` → `1.0` bump is the
only thing that has to happen for it to take effect, and there is no second constant
to remember to change.

The `Extrode.JauntyQ.Generator` and `Extrode.JauntyQ.Runtime` packages are versioned and
released together; use matching versions.

## Snapshot-format compatibility

The `.schema.json` snapshot is designed to be forward-tolerant:

- A generator reads snapshots produced by **older** CLI versions, fields added
  later (index metadata, value-safety facets) simply read as absent, and the
  features that depend on them stay off until you re-pull.
- Re-running `jauntyq schema pull` with a newer CLI upgrades the snapshot in
  place; the format has no explicit version field, and no manual migration step
  is required.
- A breaking change to the snapshot shape would be a MAJOR version and would be
  documented with an upgrade note.

Practical guidance: keep the CLI and the generator/runtime packages on the same
release, and re-pull the snapshot after upgrading to opt into new analysis.

## Framework and platform support

- **.NET SDK 8.0 or later** is required to build (the generator runs inside
  Roslyn). Generated code targets whatever framework your project targets.
- The generator is built against **Roslyn 4.12**, so the consuming toolchain
  must provide at least that compiler: .NET SDK 8.0.400+/9.0.100+, or
  **Visual Studio 17.12+** on Windows. Older toolchains skip the analyzer
  (or warn) rather than run it.
- The generator package intentionally declares no NuGet dependencies and
  relies on the compiler host for `System.Text.Json` (pinned to the 8.0.5
  version the supported Roslyn/VS toolchains ship). SDK-based `dotnet build`
  provides it in-box; on Visual Studio it comes from the VS install, another
  reason the 17.12+ floor matters.
- Generated code and `Extrode.JauntyQ.Runtime` are **Native AOT and trim compatible**;
  AOT support ultimately also depends on your database provider.
- Supported dialects: SQL Server / Azure SQL, PostgreSQL, MySQL / MariaDB, and
  SQLite. See [dialect differences](dialects.md).

Support for a new major .NET release is added after it ships; the minimum
supported SDK is raised only on a MAJOR version.

## Releases and changelog

- Releases are published to NuGet.org (the core packages and the free tool)
  and to a private feed for subscribers (the paid packages), tagged `v*`.
- Every release is recorded in the [CHANGELOG](../../CHANGELOG.md), grouped
  Added / Changed / Fixed, with any breaking changes and upgrade notes at the
  top of the entry.

## Support channels

Support level follows your license tier ([pricing](../00-overview/pricing.md)):

- **Community (free)**, GitHub issues, no SLA.
- **Team / Business**, email support bundled, 2-business-day response target.
- **Enterprise**, priority support, a custom SLA, and a named contact.

On lapse you keep the versions you have (perpetual-use fallback) but lose
updates, feed access, and support.

## Reporting issues

Include the JauntyQ package version, the dialect, the relevant `.sql` file and
snapshot excerpt, and any `JNTxxxx` code from the build output. For CLI
problems, re-run with `JAUNTYQ_VERBOSE=1` and include the detailed error (it
omits connection-string secrets). Contact routes are on
<https://extrode.com>.
