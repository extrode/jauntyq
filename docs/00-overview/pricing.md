# Pricing

JauntyQ is a commercial, source-available product with a **free core and a paid
team-safety tier**. This page states the commercial terms. Pricing questions and
orders go to Extrode via <https://extrode.com/jauntyq>.

> **Pre-1.0 preview.** While JauntyQ is `0.x`, the paid commands run free under a
> Preview license that Extrode issues on request. A Preview license is honored by
> every `0.x` build and by no `1.0` build. Nothing is revoked at 1.0: a `0.x` binary
> keeps accepting it. The free core is not licensed by the file at all and is
> unaffected either way.

## Free, everything that ships an app

The core is free for everyone, with no feature flags, no trial clocks, and no
telemetry:

- the source generator and runtime in full: codegen, auto-CRUD with optimistic
  concurrency, upserts, bulk fast paths, sequences, enum capture, batched IN;
- **all** build-time analysis, `JNT1xxx` to `JNT8xxx`, including the deep query
  analysis (FK-graph joins, ORDER-BY-index, N+1 detection);
- `jauntyq schema pull`, migration simulation, and DDL-as-source.

Code the generator emits into your project is yours to modify, compile, ship, and
sell as part of your applications (see [Licensing](#licensing)).

## Paid, team safety

The paid tier is the tooling that catches schema drift and breaking migrations
before they reach production. It ships as `JauntyQ.Cli.Premium`, which installs
the same `jauntyq` command with these verbs added:

- **Migration impact analysis**, `jauntyq migrate impact`: SAFE/RISKY/BREAKING
  classification of pending migrations against your actual query corpus.
- **Database contract testing**, `jauntyq schema verify` in CI, the
  `JauntyQ.Schema.Contract` assertion API for your own test suite, and the
  boot-time startup guard.
- **Per-service contracts and the schema registry** for shared databases.
- **Usage-aware severity**: drift on objects your queries provably never touch
  will not fail your gate.
- **Live EXPLAIN**, `jauntyq explain` plan analysis over your query corpus.

## Tiers

| Tier | Price | Covers | Support |
|---|---|---|---|
| **Community** | free | Organizations under $1M annual revenue, non-commercial use, education | GitHub issues, no SLA |
| **Team** | $349/year | Up to 10 developers | Email, 2-business-day response target |
| **Business** | $999/year | Up to 50 developers | Email, 2-business-day response target |
| **Enterprise** | custom | Unlimited developers | Priority support, custom SLA, named contact |

Pricing is **flat per organization**: one signed `jaunty.license.json` per org, no
seat counting, no named-developer administration. CI and build agents never count
against anything. Support is bundled into every paid tier; there is no separate
support SKU, and the response targets above are commitments, not aspirations.

## Lapse, your tooling keeps working

If a subscription ends, the features you were entitled to **keep working** on the
versions you have; they print a renewal reminder, never a hard failure. Renewing
restores updates and support. A lapse never turns exit codes red in CI; see
[licensing and activation](../03-guides/licensing-and-activation.md#lapse).

## Licensing

- **The core** is licensed under the Islamic Software License - Restricted
  (**ISL-R**), Version 1.2: source viewable, use permitted, no modification or
  redistribution of the tool itself. Text: [LICENSE.md](../../LICENSE.md),
  published at <https://islamiclicense.org/isl-r/1.2/LICENSE.md>.
- **Generated output** is covered by the Islamic Software License - Output
  Exception (**ISL-OE**), Version 1.2, adopted alongside the license: you may
  modify, compile, distribute and sell the code, schema snapshots and reports
  JauntyQ emits into your project, as part of your own work, with no source
  disclosure and no obligation to reproduce the license text. The license's
  ethical use restrictions continue to apply to that output. Text:
  [EXCEPTION.md](../../EXCEPTION.md), published at
  <https://islamiclicense.org/isl-oe/1.2/EXCEPTION.md>. The adoption notice is
  [NOTICE.md](../../NOTICE.md), and all three files ship inside every package.
- **The paid components** ship under the Islamic Software End User License
  Agreement (**ISL-EULA**), Version 1.0, published at
  <https://islamiclicense.org/isl-eula/1.0/LICENSE.md>; the grant is conditioned
  on an Order per the tiers above. The Output Exception applies under it too, so
  it does not matter which half emitted an artifact.

Neither license is OSI-approved; JauntyQ is source-available, not open source,
as a deliberate values decision.

## Distribution

The free core is published to **NuGet.org**; the paid packages ship from a
private feed to subscribers. A paid license is a signed file verified fully
offline, and nothing phones home, ever. Mechanics:
[licensing and activation](../03-guides/licensing-and-activation.md).

## Ordering

To place an order, or to ask about evaluation or Enterprise terms, contact
Extrode via <https://extrode.com>.

---

*Jaunty's commercial model is documented with [Jaunty](https://extrode.com/jaunty),
not here.*
