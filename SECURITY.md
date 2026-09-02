# Security Policy

## Trust model (read this first)

JauntyQ is a **compile-time** code generator. At build time it reads two kinds
of input and emits C# that is compiled into your application:

- **`.sql` files** (your queries), and
- **`*.schema.json`** schema snapshots,

both supplied to the Roslyn generator as `AdditionalFiles`.

**Treat these inputs as source code.** A `.sql` file or schema snapshot is as
trusted as the C# in your repository: the generator turns their contents into
compiled code. Do not point JauntyQ at `.sql` files or schema snapshots from an
untrusted source (an unreviewed pull request, a third-party snapshot, a
compromised upstream) without reviewing them, exactly as you would not merge
unreviewed source code.

JauntyQ defends this boundary in depth. Identifiers derived from SQL aliases,
file paths, schema table/column names, and parameter names are validated or
neutralized before they reach emitted C# (diagnostic **JNT2004**), and embedded
string literals are encoded. Review remains your first line of defense.

### What JauntyQ does *not* do at build time
- No network access.
- No code execution from schema/SQL content (names are validated/escaped).
- No secrets are embedded in generated code.

### Runtime
Generated data access is fully parameterized; values are never concatenated
into SQL, so there is no runtime SQL-injection surface introduced by JauntyQ.

### CLI secrets
The `jauntyq` CLI reads a database to produce a schema snapshot. Prefer supplying
the connection string via `--connection-env <VAR>` or the `JAUNTYQ_CONNECTION`
environment variable rather than `--connection` on the command line (which is
visible in process listings, shell history, and CI logs). Error output is
redacted by default; set `JAUNTYQ_VERBOSE=1` only when diagnosing locally.

## Supported versions

Pre-1.0, only the latest released version receives security fixes.

## Reporting a vulnerability

Please report suspected vulnerabilities privately rather than opening a public
issue. Use GitHub's private vulnerability reporting on this repository:
<https://github.com/extrode/jauntyq/security/advisories/new> (Security tab >
"Report a vulnerability"). We aim to acknowledge reports within 3 business days
and coordinate a fix and disclosure timeline with you.
