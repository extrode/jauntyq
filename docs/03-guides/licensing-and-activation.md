# Licensing and activation

JauntyQ's **core is never gated.** The source generator, every `JNTxxxx` build
diagnostic (including the deep `JNT8xxx` query analysis), and the runtime always
work, with or without a license, valid or lapsed. That is the perpetual-use grant in the EULA, and it is enforced
structurally: no core assembly can even reference the licensing code, so
generated output and diagnostics are identical regardless of license state.

What *is* gated are the premium ("moat") CLI features. Today that means:

| Command | Entitlement (feature key) |
|---|---|
| `jauntyq migrate impact` | `migration-impact` |
| `jauntyq schema verify` | `contract-testing` |
| `jauntyq schema verify --service <id>` | `per-service-contracts` (in addition to `contract-testing`) |
| `jauntyq schema verify --usage <path>` | rides the `contract-testing` gate (no extra key) |
| `jauntyq usage export` | `contract-testing` (gated 2026-08-01, it feeds `schema verify --usage`) |
| `jauntyq registry resolve\|dependents\|validate\|list` | `contract-testing` (gated 2026-08-01, same family) |
| `jauntyq explain` | `live-explain` |

That is five commands over four feature keys, enforced inside `JauntyQ.Cli.Premium`;
`schema verify --service` is a second check on top of `schema verify`. Nothing outside
`JauntyQ.Cli.Premium` consults the gate.

The free/paid split across the whole product is described on the
[pricing page](../00-overview/pricing.md).

`deep-query-analysis` is the one remaining reserved key, defined for a
future, not-yet-built feature. It does **not** gate the JNT8006/JNT8007
deeper-query-analysis diagnostics already shipped in the generator (spec
004), those are core, build-time diagnostics like every other JNTxxxx
check, and the core generator is never gated (see below). The embeddable
`JauntyQ.Schema.Contract` assertion library (used from your own test suite)
is **not** gated, only the `schema verify` CLI entry point is.

This is **entitlement gating for a source-viewable product, not DRM.** A license
is a signed, tamper-evident record of what you are entitled to, verified offline.
It optimizes for a clean legitimate path (activate once, works offline forever
within your entitlement), not for defeating a determined bypasser.

## The code JauntyQ generates is yours

The question this page gets asked first, so it is answered before the mechanics.
Both licenses define a Derivative Work as anything that *incorporates* the
Software, which, read without qualification, would make your application a
forbidden derivative the moment you compiled generated code into it. The
[Output Exception](../../EXCEPTION.md) exists to stop
that reading, and has been in force since 2026-08-17. Under it you may modify,
compile, distribute and sell generated code, schema snapshot files and reports
as part of your own applications, with no obligation to disclose source and no
obligation to reproduce the license text; you may also redistribute
`JauntyQ.Runtime` in unmodified object form. Your end users do not become
licensees of JauntyQ.

Two limits are deliberate. The ethical use restrictions (Sections 4 and 5)
continue to bind what you build with the output, this is not an unrestricted
compiler exception, and that is the point of it. And the Exception grants
nothing over the Software itself: the generator and CLI stay unmodifiable and
non-redistributable.

The Exception ships inside every package alongside the license, and applies
under both ISL-R and the ISL-EULA, so it does not matter which half emitted the
artifact.

## The license file

A license is a signed `jaunty.license.json` issued to you out of band. It carries
a payload (licensee, tier, entitlements, seats, issue/term dates, product id,
format version) and a detached RSA-2048/PSS signature over the payload's
canonical bytes. The tool verifies it offline against a public key embedded in
the build; **no network calls are ever made** for licensing.

## Activate

```bash
jauntyq activate --license path/to/jaunty.license.json
```

The tool verifies the signature against the embedded public key, checks the
product and format version, and installs the license to a per-user location:

| OS | Path |
|---|---|
| Windows | `%APPDATA%\JauntyQ\jaunty.license.json` |
| Linux | `~/.config/JauntyQ/jaunty.license.json` |
| macOS | `~/Library/Application Support/JauntyQ/jaunty.license.json` |

If verification fails, **nothing is installed** and the error tells you why: a
bad signature, malformed JSON, a license for a different product, or a license
format newer than your JauntyQ build understands (upgrade JauntyQ).

Activation never checks the term/expiry, so a wrong system clock can never brick
activation, expiry only ever matters at *use* time (see [Lapse](#lapse)).

## Inspect

```bash
jauntyq license status
```

Prints the tier, licensee, seat count, term dates, entitlements, and whether the
license has lapsed, fully offline. A preview license reports its release scope in
place of the term. `jauntyq license deactivate` removes the
installed license (e.g. when moving to another machine).

## Using premium features

With an entitling license installed, the five commands in the table above run
normally. Without one (or with a license that does not grant that feature), they
print an entitlement-required message and exit with the reserved code **`3`**, distinct from `1` (usage/error) and `2` (drift/findings) so CI can tell "not
licensed" apart from "found problems." They do no premium work when not entitled.

## Preview licenses (pre-1.0)

While JauntyQ is pre-1.0 the premium commands are free, and that is implemented by
minting free licenses rather than by ungating anything: a preview license is an
ordinary signed license with `tier: "Preview"`, minted perpetual, granting the paid
entitlements. The gate is unchanged, so an unlicensed caller still exits `3`.

**A preview license is scoped to the releases the preview covers.** Every `0.x`
build honors it forever; a `1.0` or later build classifies it `NotEntitled` with
its own message and an upgrade path, not the generic entitlement-required wording:

```
your free preview license covers pre-1.0 releases only
```

This is a **tier** check, not an expiry check, and it is ordered ahead of the expiry
test. Expiry could not do the job: a lapse runs with a warning by design and never
becomes `NotEntitled`, so a short-dated preview license would keep working on every
release JauntyQ ever ships. `jauntyq license status` reports the scope rather than
printing "perpetual (no expiry)", which would be true of the date and misleading
about what it covers.

Nothing is revoked and no existing build changes behavior, a `0.x` binary keeps
accepting the license after 1.0 ships.

## Lapse

A term can end (the payload may carry an `expiresUtc`; a perpetual license has
none). After expiry, features you were **already entitled to keep working** and
print a non-blocking warning:

```
warning: your license has lapsed; migration impact analysis still works, but renew to keep receiving updates.
```

A lapse **never** turns a granted feature into "not entitled" and never produces
exit `3`. This matches the pricing term that a lapsed customer keeps perpetual
use of the versions they already have.

## CI

CI rarely runs an interactive `activate`. Point `JAUNTYQ_LICENSE` at a license
file instead, the same offline verification applies:

```bash
export JAUNTYQ_LICENSE="$CI_SECRETS/jaunty.license.json"
jauntyq schema verify --provider sqlserver --connection-env CI_DB_CONN \
  --output db/schema/jaunty.schema.json
```

Resolution order for reads is `JAUNTYQ_LICENSE` → the per-user install → none.

## Seats

Seat count is recorded as informational metadata. JauntyQ does **not** attempt
machine-count enforcement, that is unverifiable offline without a phone-home,
which is deliberately out of scope.

## How licenses are issued

The private signing key is **never** in this repository or in any shipped
binary; only the public key is embedded, for verification. Extrode mints
licenses out of band with the production key and sends you the signed file.
No build phones home to check it.
