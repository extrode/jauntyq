// Tests for scripts/skip-audit.js.
//
// Run: deno test -A scripts/skip-audit.test.js
//
// The guard's whole value is that it fails a run somebody else would have read
// as green, so the negative cases matter more than the positive one: an audit
// that never rejects anything passes the happy path perfectly and protects
// nothing.

import { assert, assertEquals } from "jsr:@std/assert@1";
import { auditSkips, parseTrx } from "./skip-audit.js";

/** The exact shape VSTest emits, copied from a real run's trx on 2026-08-17. */
function trx(results) {
  return `<?xml version="1.0" encoding="UTF-8"?>
<TestRun id="x" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
${results}
  </Results>
</TestRun>`;
}

function skipped(name, message) {
  return `    <UnitTestResult executionId="e" testId="t" testName="${name}" computerName="P3" duration="00:00:00.0010000" outcome="NotExecuted" testListId="l" relativeResultsDirectory="r">
      <Output>
        <ErrorInfo>
          <Message>${message}</Message>
        </ErrorInfo>
      </Output>
    </UnitTestResult>`;
}

function passed(name) {
  return `    <UnitTestResult executionId="e" testId="t" testName="${name}" computerName="P3" duration="00:00:00.1116185" outcome="Passed" testListId="l" relativeResultsDirectory="r" />`;
}

Deno.test("parseTrx reads the name and reason off a skipped result", () => {
  const skips = parseTrx(trx(skipped(
    "Extrode.JauntyQ.Tests.PostgresExtractorLiveTests.CitextColumn_IsUnicode",
    "JAUNTYQ_TEST_POSTGRES_CONNECTION not set - live Postgres extractor tests skipped.",
  )));

  assertEquals(skips.length, 1);
  assertEquals(skips[0].name, "Extrode.JauntyQ.Tests.PostgresExtractorLiveTests.CitextColumn_IsUnicode");
  assert(skips[0].reason.startsWith("JAUNTYQ_TEST_POSTGRES_CONNECTION not set"));
});

Deno.test("parseTrx ignores results that ran", () => {
  // The self-closing Passed form is the overwhelming majority of a trx, and a
  // greedy match across it would attribute one skip's reason to another test.
  const skips = parseTrx(trx([
    passed("A.Ran"),
    skipped("A.Skipped", "JAUNTYQ_TEST_MYSQL_CONNECTION not set."),
    passed("A.AlsoRan"),
  ].join("\n")));

  assertEquals(skips.map((s) => s.name), ["A.Skipped"]);
});

Deno.test("parseTrx does not let a test NAMED NotExecuted count as a skip", () => {
  // A real test in this repo is called SelectIsPlannedNotExecuted_EvenWhenItWouldBeSlow,
  // and it passes. Matching the substring rather than the attribute would count it.
  const skips = parseTrx(trx(passed(
    "Extrode.JauntyQ.Explain.Tests.LiveExplainTests.SelectIsPlannedNotExecuted_EvenWhenItWouldBeSlow",
  )));

  assertEquals(skips.length, 0);
});

Deno.test("parseTrx decodes XML entities in the reason", () => {
  const skips = parseTrx(trx(skipped("A.B", "needs &lt;none&gt; &amp; nothing")));

  assertEquals(skips[0].reason, "needs <none> & nothing");
});

Deno.test("an env-gated skip is accounted for", () => {
  const result = auditSkips(
    [{ name: "A.B", reason: "JAUNTYQ_TEST_POSTGRES_CONNECTION not set - skipped." }],
    true,
  );

  assertEquals(result.unexplained.length, 0);
  assertEquals(result.tally.get("env-gated live database test"), 1);
});

Deno.test("the 2026-08-17 regression fails the audit", () => {
  // The measured bad run: three assemblies skipped wholesale while Docker was
  // up, and `dotnet test` exited 0. This is the case the guard exists for.
  const skips = [
    ...Array.from({ length: 69 }, (_, i) => ({
      name: `Extrode.JauntyQ.Northwind.Tests.Tier1Tests.Case${i}`,
      reason: "Docker unavailable: InvalidOperationException: bring-up failed",
    })),
    ...Array.from({ length: 17 }, (_, i) => ({
      name: `Extrode.JauntyQ.AdventureWorksLite.SqlServer.Tests.ViewTests.Case${i}`,
      reason: "Docker unavailable: InvalidOperationException: bring-up failed",
    })),
    ...Array.from({ length: 15 }, (_, i) => ({
      name: `Extrode.JauntyQ.Sakila.MySql.Tests.FilmTests.Case${i}`,
      reason: "Docker unavailable: InvalidOperationException: bring-up failed",
    })),
  ];

  const result = auditSkips(skips, /* dockerReachable */ true);

  assertEquals(result.total, 101);
  assertEquals(result.unexplained.length, 101);
});

Deno.test("the same skips are legitimate when the daemon really was absent", () => {
  // The other half of the classification, and the reason the flag is threaded
  // through at all: a developer with Docker Desktop closed must still get a
  // green run rather than a wall of failures.
  const skips = [{ name: "A.B", reason: "Docker unavailable: ArgumentException: no endpoint" }];

  assertEquals(auditSkips(skips, false).unexplained.length, 0);
  assertEquals(auditSkips(skips, true).unexplained.length, 1);
});

Deno.test("a skip with no recorded reason is unexplained, not excused", () => {
  // Silence is the failure mode the guard exists to catch, so it cannot also
  // be a way past it.
  assertEquals(auditSkips([{ name: "A.B", reason: "" }], true).unexplained.length, 1);
});

Deno.test("an unrecognised reason fails even when it looks harmless", () => {
  // The allowlist is a closed set on purpose. A new skip reason should make
  // somebody decide it is legitimate and say why, not slip in unread.
  const result = auditSkips([{ name: "A.B", reason: "temporarily disabled, see issue 42" }], true);

  assertEquals(result.unexplained.length, 1);
  assertEquals(result.tally.get("UNEXPLAINED"), 1);
});

Deno.test("the opt-in discovery pass is sanctioned, and only in its exact shape", () => {
  // Found 2026-08-29 by the first full run that reached the skip audit: spec
  // 018's discovery pass skipped with a reason no entry matched, so test-all.sh
  // failed on a skip that is correct by design. The pass asserts nothing --
  // it PRODUCES the groupings the assertions in the same file check -- so
  // running it on every build would spend five container suites printing a
  // report nobody read.
  const real = "Set JAUNTYQ_STRESS_DISCOVERY=1 to run the discovery pass.";
  assertEquals(auditSkips([{ name: "A.B", reason: real }], true).unexplained.length, 0);
  assertEquals(auditSkips([{ name: "A.B", reason: real }], false).unexplained.length, 0);

  // The entry must not become a general "mentions an env var" escape hatch:
  // each of these is a skip somebody should have to justify.
  for (
    const notSanctioned of [
      "temporarily disabled, set FIXME=1 to run it",
      "Set OTHER_PRODUCT_FLAG=1 to run the discovery pass.",
      "Set JAUNTYQ_STRESS_DISCOVERY to run the discovery pass.",
      "JAUNTYQ_STRESS_DISCOVERY is unset",
      // The anchor carries this one, and nothing else does: a reason that
      // opens with a real excuse and appends the sanctioned sentence would
      // otherwise launder itself through this entry.
      "Flaky since 2026-08-01. Set JAUNTYQ_STRESS_DISCOVERY=1 to run it anyway.",
    ]
  ) {
    assertEquals(
      auditSkips([{ name: "A.B", reason: notSanctioned }], true).unexplained.length,
      1,
      `expected UNEXPLAINED: ${notSanctioned}`,
    );
  }
});

Deno.test("a reachable server with no canonical schema is sanctioned, narrowly", () => {
  // Found 2026-08-30 while adding spec 014's own SQL Server fixture. The four
  // Northwind tests had only ever skipped for "connection not set", so nobody
  // had run them against a live server that was NOT Northwind -- which fails
  // them for an absent sample database rather than a broken extractor. This
  // entry is the third state; the wording is the contract.
  const real = "JAUNTYQ_TEST_SQLSERVER_CONNECTION names a database without the " +
    "canonical Northwind schema (no dbo.Products) — those live tests skipped.";
  assertEquals(auditSkips([{ name: "A.B", reason: real }], true).unexplained.length, 0);
  assertEquals(
    auditSkips([{ name: "A.B", reason: real }], true).tally.get(
      "live server reachable, canonical sample schema absent",
    ),
    1,
  );

  // It must not widen into "the fixture mentioned a database and gave up".
  for (
    const notSanctioned of [
      "JAUNTYQ_TEST_SQLSERVER_CONNECTION names a database without Northwind",
      "SQLSERVER_CONNECTION names a database without the canonical Northwind schema (no dbo.Products)",
      "names a database without the canonical Northwind schema (no dbo.Products)",
      "JAUNTYQ_TEST_SQLSERVER_CONNECTION names a database without the canonical schema",
      // Anchored, so a real excuse cannot prepend itself onto the sanctioned
      // sentence and inherit the exemption.
      "Times out on this box. JAUNTYQ_TEST_SQLSERVER_CONNECTION names a database " +
      "without the canonical Northwind schema (no dbo.Products).",
    ]
  ) {
    assertEquals(
      auditSkips([{ name: "A.B", reason: notSanctioned }], true).unexplained.length,
      1,
      `expected UNEXPLAINED: ${notSanctioned}`,
    );
  }
});
