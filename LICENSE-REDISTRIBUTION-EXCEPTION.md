# JauntyQ Redistribution Exception, Version 1.0

**Effective Date:** September 2026

**Copyright (c) 2026 Extrode LLC. All rights reserved.**

---

**What this document is for.** `Extrode.JauntyQ.Runtime` is a small library that every project using
JauntyQ references directly, and its compiled assembly is copied into every build and publish
output of that project. Applied without qualification, the license JauntyQ ships under would defeat
that: ISL-R Section 2 prohibits Distribution of "the Software or any copy thereof to any third
party" without prior written consent, and referencing `Extrode.JauntyQ.Runtime` is Distribution of
it in exactly that sense — it happens on every build, to every recipient of the Licensee's
application. `EXCEPTION.md` (the ISL Output Exception, adopted by JauntyQ) does not reach this case:
its Section 1.2 expressly excludes "a bundled runtime library... remains the Software," and its
Section 5 grants no right to redistribute the Software itself. This Redistribution Exception is the
separate carve-out that authorizes shipping the runtime library, and it is what makes JauntyQ usable
in a built application at all. **The ethical restrictions travel with the redistributed binary by
design** — this is not an unrestricted redistribution permission, and that limitation is deliberate.

**Two exceptions, two jobs.** JauntyQ adopts both `EXCEPTION.md` and this document, and they are
cumulative and independent: `EXCEPTION.md` grants rights over what JauntyQ's generator *emits* into
the Licensee's project (the generated code, schema snapshots, reports); this Redistribution
Exception grants rights over the one compiled component of JauntyQ itself that the Licensee's
project must carry along — `Extrode.JauntyQ.Runtime`. Neither substitutes for the other, and the CLI
tool and the generator/analyzer package need neither: both run only on the developer's machine and
are never copied into a built application.

**Which license this modifies.** This Exception is an additional permission under the Islamic
Software License, Restricted, version 1.2 ("ISL-R"), the license governing JauntyQ's source and its
published packages. It modifies those terms only as stated below; everything not stated remains
governed by ISL-R.

---

## 1. Definitions

**1.1 "Packages"** means the compiled assemblies published by the Licensor under the
`Extrode.JauntyQ.Runtime` package identifier, in unmodified object form, together with their symbol
and documentation files. It does not include `Extrode.JauntyQ.Cli`, `Extrode.JauntyQ.Generator`, or
any other JauntyQ package, which are not redistributed as part of a Licensee Application and remain
governed by ISL-R without this Exception.

**1.2 "Licensee Application"** means a work of the Licensee's own that references one or more
Packages, and whose functionality is not primarily a substitute for the Software itself.

**1.3** Terms defined in ISL-R and not redefined here carry the meaning given in ISL-R.

## 2. Grant of redistribution

Notwithstanding the prohibition on Distribution in ISL-R Section 2 (the lettered list of prohibited
acts beginning "The following are expressly prohibited", not the lettered grants earlier in that
Section), the Licensee may reproduce and distribute the Packages, in unmodified object form only, as
incorporated into or deployed alongside a Licensee Application, in any medium and by any means,
including as part of a container image, an installer, a published artifact, or a hosted service
offered to the Licensee's own customers.

No obligation to disclose the source of the Licensee Application, and no obligation to reproduce the
ISL-R text within the Licensee Application, arises from this Exception. ISL-R Section 3.1
(Attribution) continues to apply to the Licensee's own use.

## 3. What is not granted

This Exception grants no right to distribute the Packages other than as a component of a Licensee
Application. In particular it does not permit republishing the Packages to a package registry,
offering them for download as a library, mirroring them, or distributing them modified. It grants no
rights over the source code beyond the View right in ISL-R Section 2(b), and no right to create
Derivative Works. It grants no rights over any JauntyQ package other than the Packages defined in
Section 1.1.

## 4. Continuing ethical conditions

The rights granted by this Exception are expressly conditioned on ISL-R Section 4 (Ethical Use
Restrictions) and Section 5 (Genocide, Injustice, and State-Level Restrictions), which continue to
apply to the Licensee's use and distribution of the Packages, including their use within any
Licensee Application. A Licensee Application must not be created, operated, offered, or distributed
in service of a Prohibited Activity. Violation terminates this Exception together with ISL-R, per
ISL-R Section 7.

## 5. Scope of downstream obligation

This Exception binds the Licensee. Recipients of a Licensee Application in object form do not
thereby become licensees of the Software, are not required to accept ISL-R, and the Licensee is not
required to impose its terms on such recipients. A recipient who extracts the Packages from a
Licensee Application and uses them other than as part of that application receives no rights from
this Exception.

## 6. No fee, no term

This Exception is royalty-free and does not expire. It is not conditioned on an Order, a
subscription, a seat count, or the purchase of support. A support subscription that lapses has no
effect on the rights granted here.

## 7. Severability and precedence

If any provision of this Exception is held unenforceable, it shall be modified to the minimum extent
necessary to make it enforceable and the remainder shall continue in force. Where this Exception,
`EXCEPTION.md`, and ISL-R conflict as to distribution of the Packages, this Exception controls; in
every other respect ISL-R and `EXCEPTION.md` control their own subject matter as stated above.

---

## Notes for legal review

Recorded rather than resolved, so a reviewer sees what the project already knows is soft:

1. This document is the JauntyQ twin of `LICENSE-DISTRIBUTION-EXCEPTION.md` in the Jaunty repository,
   which already anticipated it: its own legal-review notes (Section 2) reference "the choice already
   made for JauntyQ (2026-08-17)" on downstream scope, and Section 3 of those notes points to this
   document by name before it existed. The two should be reviewed together for consistency.
2. **Section 1.1's exclusion of the generator/CLI packages** rests on the assumption that neither is
   ever copied into a Licensee Application's build or publish output. If a future JauntyQ release adds
   a scenario where a generator-package assembly is also referenced at runtime (for example, a shared
   attributes assembly consumed by generated code), that assembly would need to be added to the
   Packages definition, or the runtime dependency restructured so it does not.
3. **Section 5's downstream depth** matches the choice already made for Jaunty and recorded there: the
   ethical restrictions bind the Licensee's creation, operation and distribution of their application,
   but do not reach the end users of that application.
4. **Whether ISL-R should carry a redistribution permission natively**, rather than each library
   product bolting one on, is a question for the ISL authors — the same open question Jaunty's
   document raises.

*This document was drafted by the project, not by counsel. It should be reviewed by a qualified
attorney — and, for the ethical-scope questions, referred to qualified scholars — before
publication.*
