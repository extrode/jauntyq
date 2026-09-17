# Islamic Software License - Contributor License Agreement (ISL-CLA), Version 1.1

**Status:** Draft — not in force

**Copyright (c) 2026 Ikhbat Foundation (ikhbat.org). All rights reserved.**

---

> **DRAFT — NOT YET IN FORCE.**
>
> This document has not been reviewed by counsel. It is published so that it can be read,
> criticised, and reviewed before anyone relies on it. **Do not adopt it, and do not accept
> Contributions under it, until it is marked final.** If you are a lawyer or a qualified scholar
> and you find fault in this text, tell us, so that the next version is better.

---

## Preamble

This Agreement belongs to the Islamic Software Licenses. It quotes the same principles the
licenses do, and it exists because a license that governs what a Licensee may do says nothing
about what happens when someone offers work *back*.

> *"...And cooperate in righteousness and piety, but do not cooperate in sin and
> aggression..."* — Quran 5:2

> *"O you who have believed, when you contract a debt for a specified term, write it down.
> And let a scribe write [it] between you in justice..."* — Quran 2:282

Quotations from the Quran are the Saheeh International translation (Al-Muntada Al-Islami /
Abul-Qasim Publishing House), reproduced verbatim; an ellipsis marks words omitted from a
verse.

Contribution is cooperation, and cooperation is written down so that neither party is wronged
later. That is what this document is for.

## Why this Agreement exists

The ISL family has three different gaps, and this Agreement closes all three.

**For projects licensed under ISL-R**, contribution is prohibited *unless the Licensor consents*.
ISL-R Section 2 prohibits modification, distribution, derivative works, and reverse engineering
for replication — but it prohibits them **"without prior written consent from the Licensor."**
That trailing clause is a permission mechanism, and it has gone unused because no instrument
existed to exercise it. **A signed copy of this Agreement is that prior written consent.** It is
what makes contributing lawful without changing the license, without weakening the ethical use
restrictions in Sections 4 and 5, and without the project becoming open source. ISL-R is the only
software variant in the family with no Contributions section of its own.

**For projects licensed under ISL-P, ISL-C, ISL-LC, ISL-NETC, or ISL-NC**, the position is
different. Those licenses permit modification outright, and each already carries a Section 6
(Contributions) that sets default inbound terms. Under it, a Contribution "is submitted under, and
is licensed to the Licensor and to every Licensee under, **the terms of this License**." Two
consequences follow, and they are why a Licensor may still want this Agreement:

  - (a) **The default grant is only as broad as the Project License.** A Contribution to an ISL-C
    project arrives under ISL-C and must stay there. That is the right default for a single-license
    project, and it is an obstacle for a Licensor who also ships the work commercially — under
    ISL-EULA, or under a paid build — because the default terms do not permit it. Section 3 of this
    Agreement takes a grant broad enough to do so, and says plainly that it is doing it.
  - (b) **The default representations are thin.** Section 6.2 of those licenses asks only that a
    Contributor be "legally entitled to grant the licenses" and that, "to its knowledge", the
    Contribution infringes nobody. There is no employer clause, no duty to disclose third-party
    material and its license, and no provenance representation. Section 5 of this Agreement supplies
    them.

**Those Section 6 defaults apply "unless a Contributor and the Licensor have executed a separate
written agreement stating otherwise." This Agreement is that separate written agreement.** It does
not conflict with them; it is the mechanism they themselves point to.

**For projects licensed under a Creative Works variant** — ISL-CW, ISL-CW-SA, ISL-CW-NC,
ISL-CW-NC-SA, ISL-CW-ND, ISL-CW-NC-ND, or ISL-CW-0 — the position is different again, and thinner
than either. **No Creative Works variant has a Contributions section at all.** There are no default
inbound terms to fall back on, no representations asked of a contributor, and nothing saying under
what license submitted material arrives. A Licensor accepting a translation, an illustration, or a
correction to a Creative Works project has whatever the two parties happened to write down, which
is usually nothing. This Agreement supplies the whole of it.

Two of those variants need it most and are furthest from having it. **ISL-CW-ND and ISL-CW-NC-ND
grant no right to produce Adapted Material**, so preparing a correction is itself outside the
license before it is ever submitted. Section 2 is what makes that preparation lawful.

The same document serves all three cases. Sections 1 and 3 to 9 apply to every project, Section 2
applies where the Project License grants no right to modify or adapt, and Section 6 of this
Agreement is adopted only by the projects that want it.

## Notice on Reproducing This Agreement

This document (the text of this Agreement) may be copied, reproduced, published, and distributed,
verbatim and unmodified in its entirety, by any person and for any purpose — including to adopt it
for a project, to catalogue or reference it, or to critique or compare it. This right extends only
to the unmodified text of this document. It does not permit publishing a modified version of this
text under an "ISL" name.

## How a Project adopts this Agreement

**A Project adopts this Agreement by reference, not by editing it.** The text is reproduced whole
or linked to, and which of its conditional parts apply is stated separately. Nothing here is
deleted to adopt it: deleting would produce a modified ISL-CLA, which the notice above forbids, and
would break the section numbering that Sections 5, 6 and 9 refer to.

A Project adopts this Agreement by identifying it — by name and version — in the Project's
`CONTRIBUTING.md` or equivalent, and stating there:

  - (a) which ISL variant the Project is licensed under. **Section 2 applies only where that
    variant is ISL-R, ISL-CW-ND or ISL-CW-NC-ND**, and has no effect for any other variant,
    whether or not it is reproduced;
  - (b) **whether the Project adopts optional Section 6 of this Agreement** (No AI-Generated
    Contributions) — not to be confused with Section 6 (Contributions) of the ISL license texts,
    which is a different provision discussed above;
  - (c) **the jurisdiction whose law governs this Agreement**, as Section 9.2 requires;
  - (d) **who the Licensor is** — the party named in the Project License's `[LICENSOR]` field;
    or, where the Project is on a version 1.0 text that named Ikhbat Foundation, the party that
    applied the text; or, where the Project is on a 1.0 copy amended to name a party in the
    Licensor definition of that text, that party. That definition is Section 1.2 in the software
    variants and Section 1.7 in the Creative Works variants. See the note to Section 1.2 below; and
  - (e) **the forum in which disputes are brought**, as Section 9.2 also requires.

Absent a statement under (b), Section 6 does not apply to that Project. A section that does not
apply is inert: it remains in the text, and imposes nothing.

Items (c), (d) and (e) are stated here because Section 9.2 leaves the governing law and the forum
to the Licensor, and the note to the Licensor definition directs it to say who the Licensor is,
each "where it adopts this Agreement", and this is that place. This Agreement carries no blanks to
fill: what a Project must settle, it settles in its own file. A Project that omits (c) has left its
governing
law unsettled; one that omits (e) has left the court unsettled, which is the more expensive of the
two to leave open. Either is a defect in the adoption, not a permission to proceed without one.

---

## 1. Definitions

**1.1 "Project"** means the software or other work identified in the Project's `CONTRIBUTING.md`
or equivalent as governed by this Agreement, together with the repository in which it is
maintained.

**1.2 "Licensor"** means the copyright holder of the Project, being the person or entity that
licenses the Project under an Islamic Software License and adopts this Agreement for it.

> **Note.** This note is about the versions of the ISL *license texts* a Project is licensed
> under, not about versions of this Agreement.
>
> From version 1.1, every ISL text defines "Licensor" as a `[LICENSOR]` placeholder the party
> applying it fills in. Whoever is named there is the Licensor for the purposes of this Agreement,
> and the consent granted under Section 2 is theirs to give. The Project should state who that is
> where it adopts this Agreement, so that a contributor need not go and read the license file to
> find out who they are agreeing with.
>
> **Version 1.0 copies are different.** Every 1.0 text but ISL-EULA named Ikhbat Foundation
> specifically, because the family was drafted as the Foundation's own license. A Project still
> licensed under such a copy ships a text that names a party who granted nothing and who cannot
> give the Section 2 consent. It must therefore say, where it adopts this Agreement, that
> "Licensor" is to be read as the party that applied the text to the work — and it should replace
> the text with the 1.1 version, which is what actually fixes it.
>
> **A 1.0 copy edited to name its own Licensor is a third case.** Some Projects amended the
> Licensor definition of their copy — Section 1.2 in the software variants, Section 1.7 in the
> Creative Works variants — rather than overriding it elsewhere. The text then names the right
> party, and that party is the Licensor for the purposes of this Agreement; the statement made
> under this Agreement records what the copy already says. Whether such a copy may be published
> under an ISL name is a separate question, governed by the Notice on Reproducing This License
> in the text itself. From version 1.1 the fill is provided for, and no amendment is needed.

**1.3 "You"** means the individual or legal entity agreeing to these terms. Where You agree on
behalf of an employer or other entity, You represent that You are authorized to bind that entity.

**1.4 "Contribution"** means any work of authorship You intentionally Submit to the Project for
inclusion — source code, documentation, tests, configuration, translations, artwork, or other
material.

**1.5 "Submit"** means any form of communication sent to the Licensor or its representatives for
inclusion in the Project — including by pull request, merge request, patch, issue attachment, or
email — excluding communication conspicuously marked in writing as "Not a Contribution."

**1.6 "Project License"** means the Islamic Software License under which the Licensor licenses the
Project.

**1.7 "Distribute"** has the meaning given in the Project License. Where the Project License does
not define it, it means to make the Project or a derivative work of it available to any third
party, in source or object form, by sale, lease, transfer, sublicense, or any other means of
conveyance.

**1.8 "Ethical Use Restrictions"** means the provisions of the Project License restricting the
activities and industries the Project may not be used in service of, together with its state-level
restrictions — Sections 4 and 5 in every current ISL text.

---

## 2. Limited Consent to Modify or Adapt

> **Applicability.** This Section applies **only where the Project License grants no right to
> modify the Project or to produce material adapted from it**. Three variants are in that position:
> **ISL-R**, whose Section 2 prohibits modification without the Licensor's prior written consent,
> and **ISL-CW-ND** and **ISL-CW-NC-ND**, which grant no right to produce Adapted Material. Every
> other software variant — ISL-P, ISL-C, ISL-LC, ISL-NETC, ISL-NC — permits modification already
> and needs no consent, as do the Creative Works variants that permit adaptation: ISL-CW,
> ISL-CW-SA, ISL-CW-NC, ISL-CW-NC-SA and ISL-CW-0. Where the Project License already grants the
> right, this Section has no effect and grants nothing.
>
> **The two ND variants work differently from ISL-R, and the grant below reflects it.** ISL-R
> states a prohibition and attaches a consent mechanism to it, so this Section exercises a
> permission that license already contemplates. The ND texts state the absence of a grant and
> attach no such mechanism, so there is nothing in them to exercise. The consent below is therefore
> given by the Licensor in its own right as copyright holder, which it may do whatever the license
> text says, and it operates alongside the Project License rather than through it.
>
> **ISL-EULA is outside this Section entirely.** ISL-EULA is an end-user agreement for object code:
> it provides no source ("No source code is provided or licensed under this Agreement"), its
> restrictions are absolute rather than consent-gated, and its licensees therefore have nothing to
> prepare a Contribution against. A Licensor who both sells under ISL-EULA and accepts
> contributions does so through the source-side license — usually ISL-R — not through the EULA.

**2.1 Grant.** Subject to Your compliance with this Agreement, the Licensor grants You written
consent to reproduce and modify the Project, and to produce material adapted from it, **solely**
for the purpose of preparing and Submitting a Contribution. Where the Project License is ISL-R,
this is the **prior written consent contemplated by Section 2 of ISL-R**. Where the Project License
is ISL-CW-ND or ISL-CW-NC-ND, it is a separate grant the Licensor makes as copyright holder,
adding to the Project License and taking nothing away from it.

**2.2 Scope of the consent.** This consent is:

  - (a) **limited** — it extends only to preparing Contributions, and to the copies and forks
    necessary to do so, including a fork hosted publicly for the sole purpose of opening a pull
    request (see the note below);
  - (b) **non-transferable** — it authorizes You, and does not extend to anyone to whom You give a
    copy;
  - (c) **revocable** — the Licensor may withdraw it at any time on written notice; and
  - (d) **not a grant of any right to Distribute**, save for the narrow exception in 2.4. It does
    not permit You to publish, release, sublicense, sell, or otherwise convey the Project or any
    modified version of it to any third party. Publishing a modified Project remains prohibited by
    the Project License, and nothing in this Agreement makes it permitted.

**2.3 Everything else still binds.** Every other term of the Project License, including the
Ethical Use Restrictions, continues to apply to You in full.

**2.4 Public forks, and their limits.** A fork on a hosting platform is normally public, and
publishing modified source is otherwise an act of Distribution. This Section therefore permits it,
narrowly and on conditions, because a pull request cannot be opened without it:

  - (a) the fork is authorized **only** while it exists to prepare, Submit, or revise a
    Contribution;
  - (b) it must identify the upstream Project and must not present itself as a release, product,
    or distribution of Your own, under any name;
  - (c) You may not invite, encourage, or support use of the fork as a substitute for the Project;
    and
  - (d) the authorization **ends** when the Contribution is merged, declined, or abandoned, or when
    the Licensor revokes consent under 2.2(c). On that event You must, within a reasonable period,
    remove the fork from public availability or reset it to unmodified upstream content. **You may
    keep a private copy of Your own work.** What ends is the public fork, not Your retained copy;
    Section 3.3 confirms that Your Contribution remains Yours whether or not it is accepted.

For the avoidance of doubt, the passage of time does not convert a fork permitted under this
Section into a distribution permitted by the Project License.

---

## 3. Copyright License to the Licensor

**3.1 Grant.** You grant the Licensor and its successors and assigns a **perpetual, worldwide,
non-exclusive, royalty-free, irrevocable** license to reproduce, prepare derivative works of,
publicly display, publicly perform, sublicense, and distribute Your Contribution and any
derivative works of it.

**The onward terms are limited.** Where the Licensor licenses, sublicenses, or distributes Your
Contribution to any third party, it does so **only** under an Islamic Software License, or under
terms whose restrictions on the activities and industries the work may not be used in service of
are at least as broad as the Ethical Use Restrictions of the Project License. This limit binds the
Licensor's successors and assigns, and it survives any transfer of the Project. Nothing in this
Section permits the Licensor to place Your Contribution under terms carrying no such restrictions.

**3.2 What this allows, in plain terms.** This grant is deliberately broad, and You should
understand what it permits before agreeing to it. A Licensor may license the same work under more
than one ISL variant — for example offering a project under ISL-C to everyone while also selling it
to a customer who cannot accept copyleft terms, or publishing source under ISL-R while distributing
binaries commercially. **This Section is what permits
Your Contribution to be included in any of them, including in paid commercial releases, without
further permission from You and without any payment to You.**

It also permits the Licensor to **move Your Contribution out of the repository You sent it to.** A
Licensor running a free public component and a paid private one — often in separate repositories,
under different ISL variants — may take a Contribution submitted to the public component and
include it in the private, paid one. The Contribution does not have to stay where You put it, and
the Project it ends up in need not be public or free. This is stated plainly because it is the
consequence contributors are most likely to find surprising, and Section 5 asks You to represent
that You had the right to grant it.

**What it does not permit** is taking Your Contribution outside the family's ethical restrictions.
Every destination described above is an ISL text and carries them. Section 3.1 makes that a limit
rather than a habit: wherever Your Contribution goes, the restrictions go with it.

**3.3 You keep your work.** You retain full ownership of Your Contribution. You remain free to
use, license, and publish it elsewhere, however You wish, with no obligation to the Licensor.

---

## 4. Patent License

**4.1 Grant.** You grant the Licensor and every recipient of the Project a perpetual, worldwide,
non-exclusive, royalty-free, irrevocable patent license to make, have made, use, offer to sell,
sell, import, and otherwise transfer the Project. This license covers only those patent claims You
own or control that are necessarily infringed by Your Contribution alone, or by the combination of
Your Contribution with the Project to which You Submitted it.

**4.2 Termination on litigation.** If any entity institutes patent litigation against any entity —
including a cross-claim or counterclaim in a lawsuit — alleging that the Project or a Contribution
to it constitutes direct or contributory patent infringement, then any patent license granted
under this Agreement to that entity terminates as of the date the litigation is filed.

---

## 5. Your Representations

You represent that, for each Contribution You Submit:

  1. **It is Your original work**, except for any part disclosed under item 3. You wrote it
     yourself. Where You agree as a legal entity, this means it was written by a person acting for
     You whose work You are entitled to grant.
  2. **You have the right to grant the licenses above.** If Your employer, client, or educational
     institution has rights in work You create, You have obtained their permission, or they have
     waived those rights, or they have themselves agreed to this Agreement.
  3. **It is not encumbered.** It is not subject to any third-party license, patent claim, or
     other obligation that would conflict with the grants in Sections 3 and 4. If any part of it
     is not Your own work, You have identified that part, its source, and its license,
     conspicuously and in writing, at the time You Submit it.
  4. **Where the Project has adopted Section 6 of this Agreement, it complies with that Section.**

You are not expected to provide support for Your Contribution. Unless required by applicable law
or agreed in writing, Your Contribution is provided **"AS IS", without warranties or conditions of
any kind**, express or implied.

---

## 6. No AI-Generated Contributions

> **[OPTIONAL SECTION.]** This Section applies to a Project **only where the Licensor has stated
> that the Project adopts it**, in the manner described under *How a Project adopts this
> Agreement*. Where the Licensor has not so stated, this Section does not apply and imposes no
> obligation. It is offered as a modifier a Project opts into, in the same way the NonCommercial
> and NoDerivatives modifiers vary the Creative Works licenses — not as a position the family
> imposes on every Licensor.

**6.1 The rule.** Contributions must be written by a human being. **Contributions generated in
whole or in part by an artificial intelligence or machine-learning system are not accepted.**

This includes, without limitation, code produced by large language models, AI coding assistants,
code-completion systems that emit more than trivial single-token or single-identifier suggestions,
and any automated code generator trained on third-party source code.

**6.2 Your representation.** By Submitting a Contribution to a Project that has adopted this
Section, You represent that **it was authored by a human being** — You, or a person acting for You
whose work You are entitled to grant — and that no such system produced or substantially assisted
in producing it.

**6.3 What you may do instead.** This prohibition applies to **material Submitted for inclusion**.
It does not stop You participating:

**Welcome:**

  - (a) bug reports, in your own words;
  - (b) reproduction steps and failing input;
  - (c) a prose description of a proposed design or change; and
  - (d) a written explanation of a defect and where in the Project it lies.

**Not accepted:**

  - (a) patches written by an AI assistant;
  - (b) AI-generated tests or documentation;
  - (c) AI-translated or AI-refactored code; and
  - (d) code You cannot explain line by line.

**Describe the problem or the change in writing, and the maintainers will implement it.** A
precise, well-argued issue is more useful to a Project under this Section than a patch, and it is
the contribution route such Projects prefer.

**6.4 Why.** Three reasons, stated plainly so the rule does not look arbitrary:

  1. **Provenance.** AI systems are trained on code under licenses that cannot be audited and
     whose terms may conflict with the Project License. A Contribution whose copyright status
     cannot be established is one for which the grants in Sections 3 and 4 cannot honestly be
     warranted under Section 5.
  2. **Accountability.** Every line in the Project should have a human who understood it when it
     was written and can answer for it afterwards.
  3. **A Licensor's own use of such tooling is a decision about its own liability.** Where a
     Licensor uses AI tooling on its own code, under its own review and responsibility, that is a
     choice it makes for itself. It is not a choice it can make on Your behalf, or on behalf of
     the code You send it.

**6.5 Enforcement.** The Licensor may ask You to confirm the authorship of a Contribution. A
Contribution the Licensor believes to be AI-generated will be declined, and the Licensor is not
obliged to explain how it reached that view.

---

## 7. Ethical Use Restrictions Unaffected

Nothing in this Agreement waives, narrows, suspends, or creates an exception to the Ethical Use
Restrictions of the Project License, as defined in Section 1.8. They bind You as a Contributor
exactly as they bind any other licensee, and they continue to bind every recipient of the Project
after Your Contribution is included in it.

---

## 8. No Obligation

The Licensor is under **no obligation** to review, accept, merge, ship, or retain any
Contribution, and may decline any Contribution for any reason or for none.

Submitting a Contribution does not make You an employee, partner, agent, or joint venturer of the
Licensor, and creates no entitlement to compensation, to attribution beyond the Project's ordinary
practice, or to the continued inclusion of Your Contribution in the Project.

---

## 9. Miscellaneous

**9.1 Notice of inaccuracy.** You agree to notify the Licensor promptly if You become aware that
any representation in Section 5, or in Section 6 of this Agreement where it applies, has become
inaccurate.

**9.2 Governing law and forum.** This Agreement is governed by the laws of the jurisdiction stated
by the Licensor where it adopts this Agreement (see *How a Project adopts this Agreement*), without
regard to conflict-of-laws principles.

Any dispute arising out of or relating to this Agreement is brought exclusively in the courts
stated by the Licensor in the same place, and You and the Licensor each submit to the personal
jurisdiction of those courts.

Neither is stated in this text. Both are stated by the Licensor in the Project's `CONTRIBUTING.md`
or equivalent, because this Agreement is adopted by reference and is not edited to adopt it.

> **Note.** Governing law and forum are separate choices and a Licensor should make both. Naming
> the law without naming a court leaves each side to argue where the case is heard, which is the
> expensive part of a dispute that is not about the merits. A Licensor with contributors in other
> countries should take advice on this clause specifically: an exclusive forum a contributor cannot
> practically reach may be unenforceable against them, and may be worth less than a narrower one.

**9.3 Entire agreement.** This Agreement, together with the Project License, is the entire
agreement between You and the Licensor concerning Contributions, and supersedes any prior
understanding on that subject.

**9.4 Severability.** If any provision of this Agreement is held unenforceable, it is modified to
the minimum extent necessary to make it enforceable, and the remaining provisions stay in force.

**9.5 Versions.** The Licensor may adopt revised versions of this Agreement as they are published.
**A Contribution is governed by the version in force for that Project at the time the Contribution
was Submitted.** A later version does not apply retroactively to a Contribution already Submitted.

**9.6 Acceptance.** You accept this Agreement by a signed entry in the Project's `CONTRIBUTORS.md`
file, or in whatever other file the Project names where it adopts this Agreement. The entry records
Your name, Your account on the hosting platform where You have one, the date, and the version of
this Agreement You are signing. There are two ways to make it:

  - (a) **You add it yourself**, as part of the first pull request in which You Submit a
    Contribution; or
  - (b) **the Licensor records it on Your behalf**, from a written statement You send it — by
    email or with a patch — saying that You have read this Agreement, that You agree to it, and at
    which version. Section 1.5 recognises submission by patch and by email, and a Contributor
    using either cannot open a pull request; this is how such a Contributor signs. The statement
    You send is the record of Your agreement, and the Licensor should keep it.

**Acceptance runs from the first act of preparation, not from the merge.** For a project under
ISL-R, ISL-CW-ND or ISL-CW-NC-ND this matters, because copying and altering the work in order to
prepare that first submission is itself exercising the Section 2 consent. So: by beginning to
prepare a Contribution You accept this Agreement, and the Section 2 consent takes effect at that
moment, conditional on the signed entry being made — by You under (a), or by the Licensor under (b)
— with the submission that follows. **If no entry is made, acceptance fails and the consent never
took effect** — the preparatory copying was then unlicensed, and the Licensor may treat it
accordingly. A Contributor who wants certainty before touching the work may sign first: by opening
a pull request that adds only the entry, or by sending the written statement under (b) before
preparing anything.

```markdown
## Contributors

Each person below has read and agreed to the Islamic Software License Contributor
License Agreement (ISL-CLA) at the version stated against their name.

| Name | Account | Date | Agreement |
|---|---|---|---|
| [Your full name] | [@your-account] | [YYYY-MM-DD] | ISL-CLA-1.1 |
```

The bracketed cells are placeholders in the Project's file, filled by whoever makes the entry, in
the same way as `[LICENSOR]` in the license texts. They are not blanks in this Agreement, which has
none. The Agreement column is not a placeholder: it states the version actually being signed.

Where You sign under (a), the commit that adds Your entry, made from Your own account, is the
record of Your agreement. Where the Licensor records the entry under (b), the written statement You
sent is that record, and the commit is the Licensor's transcription of it.
Where a Project has adopted a later version of this Agreement, add a new entry for that version;
Your earlier entry continues to govern the Contributions Submitted under it.

---

**End of the Islamic Software License Contributor License Agreement (ISL-CLA), Version 1.1.**
