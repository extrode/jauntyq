# Reference

Precise, lookup-oriented documentation for JauntyQ.

- **[CLI reference](cli.md)**, the `jauntyq schema pull` / `verify` commands,
  options, providers, and exit codes.
- **[Schema snapshot format](schema-snapshot-format.md)**, the on-disk
  `.schema.json` structure.
- **[Configuration reference](configuration.md)**, MSBuild properties,
  `AdditionalFiles` globs, and folder conventions.
- **[Directives reference](directives.md)**, every `-- @` directive with
  syntax and constraints.
- **[Dialect differences](dialects.md)**, per-dialect emitted SQL for
  identity, upsert, sequences, and bulk insert.
- **[Cross-dialect compatibility](cross-dialect-compatibility.md)**, where the
  same query means the same thing on all five engines, and the measured
  exceptions.
- **[Supported SQL surface](supported-sql.md)**, which query shapes the parser
  accepts, which it refuses, and the diagnostic for each refusal.
- **[Diagnostics](diagnostics.md)**, every `JNTxxxx` code.
- **[Troubleshooting & FAQ](troubleshooting.md)**, symptoms mapped to causes.
- **[Versioning & support policy](versioning-and-support.md)**, version scheme,
  snapshot compatibility, framework support, and support channels.
- **[API overview](api-overview.md)**, the shape of the generated surface.
