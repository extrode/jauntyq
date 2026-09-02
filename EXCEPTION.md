# Islamic Software License - Output Exception (ISL-OE), Version 1.2

**Effective Date:** August 2026

**Copyright (c) 2026 Ikhbat Foundation (ikhbat.org). All rights reserved.**

---

## Preamble

Some software exists in order to produce something else. A code generator, a scaffolding tool, a
compiler, a schema tool: each of these emits material into the project of the person using it, and
that material is the whole reason the program was run. The software licenses in the Islamic
Software License family restrict what a Licensee may do with a work built out of the licensed
software — ISL-P, ISL-C, ISL-R, ISL-NC and ISL-NETC by defining a Derivative Work as one that
incorporates the Software and then prohibiting or conditioning its distribution; ISL-LC by its
Modified File and Larger Work provisions; ISL-EULA by prohibiting derivative works outright.
Applied without qualification to a program of this kind, those provisions defeat it: a Licensee who
compiles emitted code into an application of their own, and ships that application, is on the text
as written distributing something they were not permitted to distribute.

This Exception is the carve-out that prevents that reading. It grants the Licensee the mechanical
freedoms over what the Software emitted — to use it, change it, compile it, combine it, ship it and
sell it as part of their own work — while granting nothing whatever over the Software that did the
emitting.

**This Exception is not a license and grants nothing standing alone.** It is an additional
permission under whichever ISL variant the Licensor has applied to the Software, and it modifies
that variant only as stated below. Everything not stated here remains governed by that variant.

**This Exception is for the software variants only.** It may be adopted over ISL-P, ISL-C, ISL-R,
ISL-NC, ISL-NETC, ISL-LC or ISL-EULA. It is not written for the Creative Works variants, which
govern works rather than programs: those texts define Licensed Material and Adapted Material
instead of Software and Derivative Work, and a creative work does not emit artifacts into a
project. Adopting it over a Creative Works variant would leave its central terms with nothing to
attach to.

**Sections 4 and 5 continue to bind the Licensee.** The Ethical Use Restrictions and the Genocide,
Injustice, and State-Level Restrictions of the governing license apply in full to the Licensee's
own use and distribution of everything this Exception covers, and to any work of the Licensee's
built on it. This is not an unrestricted compiler exception, and that limitation is deliberate: a
permission that let a Licensee shed the family's ethical guarantees by routing work through a
generator would defeat the purpose of the family.

**What this Exception does not do is carry those restrictions to the Licensee's own recipients.**
Section 4 below states the limit plainly. A person who receives a Licensee Work does not become a
licensee of the Software and is bound by nothing in the governing license, which is what makes the
Exception usable at all — a generator whose output could not be shipped without binding every
downstream user to the Licensor's terms would not be adopted. A Licensor who needs the
restrictions to reach further than the Licensee should not adopt this Exception.

## Notice on Reproducing This Exception

This document (the text of this Exception) may be copied, reproduced, published, and
distributed, verbatim and unmodified in its entirety, by any person and for any purpose —
including to apply it to a work, to catalogue or reference it (e.g., in an SPDX license
list or license-comparison resource), or to critique or compare it — without such
reproduction, by itself, constituting a Prohibited Activity or being subject to Section 4
or Section 5 of the governing license. This right extends only to the unmodified text of this
Exception; it does not extend to the Software distributed under it, and does not permit
publishing a modified version of this text under an "ISL" name.

This Exception provides no bracketed fields to complete. The adopter names it, and names the
license it modifies, in that adopter's own notice, as Section 7 describes; the text of this
Exception is applied as it stands.

---

## 1. Definitions

**1.1 "Governing License"** means the Islamic Software License variant the Licensor has applied to
the Software, as identified in the Licensor's notice adopting this Exception. Only ISL-P, ISL-C,
ISL-R, ISL-NC, ISL-NETC, ISL-LC and ISL-EULA may serve as a Governing License; this Exception is
not adoptable over a Creative Works variant. Where the Licensor has applied more than one of those
variants to different components of the Software, and the adoption notice names each, the
Governing License for any given Generated Artifact is the variant applied to the component that
produced it.

**1.2 "Generated Artifacts"** means source code, schema and snapshot files, and reports that the
Software emits into the Licensee's own project as a result of the Licensee's use of the Software,
including such emitted material as subsequently modified by the Licensee. Snapshot files and
reports are included so that material of this kind committed to version control, or produced by an
automated build, is shareable on the same terms as the emitted source code itself. Generated
Artifacts do not include any material that is a copy of the Software or of a component of it,
however that material comes to be placed in the Licensee's project — a bundled runtime library, a
redistributed tool, or a file copied unaltered from the Software remains the Software, and Section
5 governs it.

**1.3 "Licensee Work"** means a work of the Licensee's own that incorporates Generated Artifacts.

**1.4** Terms defined in the Governing License and not redefined here carry the meaning given to
them in the Governing License. In particular, "Licensor", "Licensee", "Software" and "Prohibited
Activity" are the Governing License's terms and are not altered by this Exception. Where this
Exception refers to a Derivative Work or to Distribution and the Governing License does not define
that term, the reference is to whatever provision of the Governing License restricts works built
out of or containing the Software, and to whatever provision restricts making the Software or such
a work available to others — under ISL-LC, its Modified File and Larger Work provisions; under
ISL-EULA, its prohibitions on modification, derivative works and transfer.

## 2. Grant Over Generated Artifacts

Notwithstanding any prohibition or condition in the Governing License on Modification,
Distribution, sublicensing, or the creation of Derivative Works, the Licensee may use, modify,
compile, combine with other works, distribute, sublicense, and sell Generated Artifacts as part of
a Licensee Work, in source or object form, with no obligation to disclose the source of the
Licensee Work and no obligation to reproduce the text of the Governing License within the Licensee
Work.

This grant runs to Generated Artifacts only. It does not run to the Software.

## 3. Continuing Ethical Conditions

The rights granted by this Exception are expressly conditioned on Section 4 (Ethical Use
Restrictions) and Section 5 (Genocide, Injustice, and State-Level Restrictions) of the Governing
License, which continue to apply in full to the Licensee's use and distribution of Generated
Artifacts, including their use within any Licensee Work. A Licensee Work must not be created,
operated, offered, or distributed in service of a Prohibited Activity.

Violation of Section 4 or Section 5 terminates this Exception together with the Governing License,
in accordance with that license's termination provisions. Nothing in this Exception waives,
narrows, suspends, or makes optional any part of those Sections.

## 4. Scope of Downstream Obligation

This Exception binds the Licensee. A recipient of a Licensee Work does not thereby become a
licensee of the Software, is not required to accept the Governing License, and the Licensee is not
required to impose the Governing License on such a recipient.

A recipient who extracts Generated Artifacts from a Licensee Work and uses them otherwise than as
part of that Licensee Work receives no rights under this Exception.

**The consequence is stated here rather than left to be discovered.** Where the Governing License
requires a Licensee who distributes the Software or a work built from it to bind each recipient to
Sections 4 and 5, this Exception displaces that requirement as to a Licensee Work: those Sections
bind the Licensee, and not the persons the Licensee distributes to. A Licensor for whom that is
unacceptable should not adopt this Exception, and should instead license the Software on terms that
permit the Licensee to distribute a work built from it under the Governing License in full.

## 5. No Other Rights

This Exception grants no rights over the Software itself beyond those stated in the Governing
License. In particular, it grants no right to modify, adapt, reverse engineer, redistribute, or
sublicense the Software, or any component of it, including the generator, compiler, or tool that
produced the Generated Artifacts.

Nothing in this Exception widens any right the Governing License withholds over the Software
itself. Where the Governing License permits no modification of the Software — as ISL-R and
ISL-EULA do not — that prohibition stands undisturbed: the modification right granted by Section 2
runs to Generated Artifacts, and never to the Software. Section 1.2 keeps a copy of the Software
outside the Generated Artifacts set for this reason, so no material the Licensee receives can
acquire Section 2's freedoms by being emitted rather than delivered.

## 6. Severability and Precedence

If any provision of this Exception is held unenforceable, it shall be modified to the minimum
extent necessary to make it enforceable, and the remainder shall continue in force.

Where this Exception and the Governing License conflict as to Generated Artifacts, this Exception
controls. In every other respect the Governing License controls.

## 7. Adoption

This Exception takes effect for a given work only where the Licensor of that work has adopted it,
by naming this Exception and its version alongside the Governing License in the work's license
notice. Where the Software is licensed under more than one variant, the notice names each variant
the adoption covers. A Licensee may not adopt it on the Licensor's behalf, and its presence in a
project that has not adopted it grants nothing.

Where the Governing License is ISL-EULA, the Licensor's adoption notice forms part of the Agreement
between Licensor and Licensee, and this Exception is a term of that Agreement rather than a
separate or contemporaneous agreement outside it. An Order may extend this Exception but may not
narrow it as to Generated Artifacts already received.

---

## How to Apply This Exception

A project adopts this Exception with two documents, not one: the ISL variant it is licensed under,
applied as that variant's own *How to Apply* section directs, and this Exception named alongside
it.

State the adoption in `LICENSE.md` or `NOTICE.md`:

```
This software is licensed under the Islamic Software License - Restricted
(ISL-R), Version 1.2, WITH the Islamic Software License - Output Exception
(ISL-OE), Version 1.2.

The Exception grants you the right to use, modify, compile, distribute, and
sell what this software generates into your project, as part of your own
work. It grants no rights over this software itself, and the Ethical Use
Restrictions in Sections 4 and 5 of the license continue to bind your use
and distribution of what it generated.

  https://islamiclicense.org/isl-r/1.2/LICENSE.md
  https://islamiclicense.org/isl-oe/1.2/EXCEPTION.md
```

Substitute the variant the project is licensed under — one of ISL-P, ISL-C, ISL-R, ISL-NC,
ISL-NETC, ISL-LC or ISL-EULA. Cite the version-pinned URLs, not `/isl-r/LICENSE.md` or
`/isl-oe/EXCEPTION.md`, which serve the current version and change at every bump.

Include the following notice in source files:

```
SPDX-License-Identifier: LicenseRef-ISL-R-1.2 WITH AdditionRef-ISL-OE-1.2

Copyright (c) [Year] [Copyright Holder]
```

`WITH AdditionRef-` is the SPDX expression form for an additional-permissions document that is not
on the SPDX exception list, and requires an SPDX 3.0 parser. Where a toolchain accepts only SPDX
2.3 expressions, use the plain `LicenseRef-ISL-R-1.2` identifier and state the Exception in
`LICENSE.md` or `NOTICE.md` as above; the adoption is made by the notice, not by the SPDX header.

This Exception versions with the Islamic Software License family and shares its version number. It
was introduced at 1.2, so no earlier version of it exists.

---

*The Islamic Software License - Output Exception (ISL-OE) v1.2 was drafted so that software whose
purpose is to produce code and artifacts for others can be licensed under one of the family's
software variants without that variant's restrictions on works built from the Software defeating
it. It is an additional permission, not a license, no work is licensed under it alone, and it is
not adoptable over a Creative Works variant.*

*This document is provided as a legal template and should be reviewed by a qualified attorney
before use in production. The Licensor assumes no liability for the legal sufficiency of this
document in any jurisdiction.*
