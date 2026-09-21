// Fails a test run that reported success while silently skipping tests.
//
// Run:   deno run -A scripts/skip-audit.js --results artifacts/test-results
// Tests: deno test -A scripts/skip-audit.test.js
//
// `scripts/test-all.sh` invokes this after every full-solution run. It also
// works standalone against any --results-directory that a
// `dotnet test --logger trx` produced.
//
// WHY THIS EXISTS
//
// `dotnet test` exits 0 when an assembly skips every test it owns. The
// 2026-07-30 work
// stopped FixtureGate laundering arbitrary failures into "Docker unavailable",
// but nothing enforced the result at the RUN level, so a regression in that
// classification goes invisible again the moment it happens.
//
// Measured 2026-08-17, eight full-solution runs on the same commit (dev at
// 96c8c58), warm build, one machine: seven ran everything; one skipped 101
// tests across three assemblies -- Northwind (69), AdventureWorksLite.SqlServer
// (17), Sakila.MySql (15) -- and exited 0 with no reason recorded anywhere,
// because a bare `dotnet test` carries no trx logger. That is a 1-in-8 chance
// of reading a green suite that never touched three of its databases.
//
// The rule: every skip must be one the repo has decided is legitimate.
// Everything else fails the run and is printed with its reason, so the NEXT
// occurrence arrives diagnosed instead of invisible.

/**
 * Skips the repo has decided are legitimate. Each entry carries its reason: an
 * allowlist whose entries nobody can justify is how the suite starts lying
 * again, one forgotten line at a time.
 *
 * `dockerReachable` is probed by the caller BEFORE the run. When the daemon is
 * genuinely absent a "Docker unavailable" skip is the correct outcome and must
 * not fail the run -- that is the entire purpose of the local soft-skip path.
 * When Docker IS reachable the same message means the gate misclassified a real
 * bring-up failure, which is the defect being guarded.
 */
export function allowedSkips(dockerReachable) {
  const allowed = [
    {
      // Live-DB tests gated on an opt-in connection string. Absent by default
      // on a developer machine and on CI; the count only moves when someone
      // sets or unsets the variable.
      pattern: /JAUNTYQ_TEST_[A-Z_]+_CONNECTION not set/,
      why: "env-gated live database test",
    },
    {
      // An opt-in pass that asserts nothing and exists to report what it
      // measured -- spec 018's discovery pass is the first. It is off by
      // default because its output is the INPUT to the assertions in the same
      // file, so running it on every build would cost five container suites to
      // print something nobody read. Deliberately narrower than "any env
      // gate": the reason has to name a JAUNTYQ_ variable and the value that
      // turns it on, so "temporarily disabled, set FIXME=1" does not qualify.
      pattern: /^Set JAUNTYQ_[A-Z0-9_]+=\S+ to run /,
      why: "opt-in diagnostic pass, off by default",
    },
    {
      // A live server that accepted the connection but holds none of the
      // canonical sample schema the tests assert against. Distinct from "not
      // set": the variable IS set, the server IS up, and the tests still
      // cannot run. Narrow on purpose -- the reason has to name the variable,
      // the schema and the specific object that was looked for and missing,
      // so it cannot be reached for by a fixture that merely failed.
      pattern:
        /^JAUNTYQ_TEST_[A-Z_]+_CONNECTION names a database without the canonical \S+ schema \(no \S+\)/,
      why: "live server reachable, canonical sample schema absent",
    },
    {
      // FixtureGateSkipClassificationTests guards its own arms: under
      // GITHUB_ACTIONS the gate rethrows unconditionally, so its "skips" arm
      // would fail for the wrong reason and read as a real regression.
      pattern: /rethrows unconditionally under CI/,
      why: "FixtureGate classification test's own CI guard",
    },
  ];

  if (!dockerReachable) {
    allowed.push({
      pattern: /^Docker unavailable: /,
      why: "Docker daemon was not reachable when the run started",
    });
  }

  return allowed;
}

function decodeEntities(s) {
  return s
    .replaceAll("&lt;", "<")
    .replaceAll("&gt;", ">")
    .replaceAll("&quot;", '"')
    .replaceAll("&apos;", "'")
    // Ampersand last: decoding it first would let "&amp;lt;" become "<".
    .replaceAll("&amp;", "&");
}

// Deliberately a regex rather than an XML parser: the shape is fixed by VSTest,
// the files are large, and a parser would be the only dependency in scripts/.
// Both the self-closing and the element form appear -- a skip carries a
// <Message>, a passing test usually does not.
const RESULT =
  /<UnitTestResult\b([^>]*?)outcome="NotExecuted"([^>]*?)(?:\/>|>([\s\S]*?)<\/UnitTestResult>)/g;
const NAME = /testName="([^"]*)"/;
const MESSAGE = /<Message>([\s\S]*?)<\/Message>/;

/** Extracts every skipped test and its recorded reason from one trx document. */
export function parseTrx(text) {
  const skips = [];
  for (const m of text.matchAll(RESULT)) {
    const attrs = m[1] + m[2];
    const body = m[3] ?? "";
    skips.push({
      name: decodeEntities(NAME.exec(attrs)?.[1] ?? "<unnamed test>"),
      reason: decodeEntities(MESSAGE.exec(body)?.[1] ?? "").trim(),
    });
  }
  return skips;
}

/**
 * Classifies skips against the allowlist. A skip with no recorded reason at all
 * is unexplained by construction -- silence is the failure mode being guarded,
 * so it cannot also be an excuse.
 */
export function auditSkips(skips, dockerReachable) {
  const allowed = allowedSkips(dockerReachable);
  const tally = new Map();
  const unexplained = [];

  for (const skip of skips) {
    const hit = allowed.find((a) => a.pattern.test(skip.reason));
    const key = hit ? hit.why : "UNEXPLAINED";
    tally.set(key, (tally.get(key) ?? 0) + 1);
    if (!hit) unexplained.push(skip);
  }

  return { total: skips.length, unexplained, tally };
}

async function* trxFiles(dir) {
  for await (const entry of Deno.readDir(dir)) {
    const path = `${dir}/${entry.name}`;
    if (entry.isDirectory) yield* trxFiles(path);
    else if (entry.name.endsWith(".trx")) yield path;
  }
}

async function main(args) {
  const flag = (name, fallback = null) => {
    const i = args.indexOf(name);
    return i >= 0 && i + 1 < args.length ? args[i + 1] : fallback;
  };

  const resultsDir = flag("--results");
  if (!resultsDir) {
    console.error("usage: skip-audit.js --results <dir> [--docker-reachable true|false]");
    return 2;
  }
  const dockerReachable = flag("--docker-reachable", "true") !== "false";

  let isDir = false;
  try {
    isDir = (await Deno.stat(resultsDir)).isDirectory;
  } catch {
    isDir = false;
  }
  if (!isDir) {
    console.error(`skip-audit: no results directory at ${resultsDir}.`);
    console.error("The run produced no trx, so nothing can be verified. That is a failure, not a pass.");
    return 1;
  }

  const skips = [];
  let fileCount = 0;
  for await (const file of trxFiles(resultsDir)) {
    fileCount++;
    skips.push(...parseTrx(await Deno.readTextFile(file)));
  }

  if (fileCount === 0) {
    console.error(`skip-audit: ${resultsDir} contains no .trx files.`);
    console.error("The run produced no results to verify. Treating that as a failure.");
    return 1;
  }

  const { total, unexplained, tally } = auditSkips(skips, dockerReachable);

  console.log(`skip-audit: ${fileCount} trx file(s), ${total} skipped test(s).`);
  for (const [why, n] of [...tally].sort((a, b) => b[1] - a[1])) {
    console.log(`  ${String(n).padStart(4)}  ${why}`);
  }

  if (unexplained.length === 0) {
    console.log("skip-audit: every skip is accounted for.");
    return 0;
  }

  console.error("");
  console.error(`skip-audit: ${unexplained.length} skipped test(s) with no sanctioned reason.`);
  console.error("A run that skips these and exits 0 is reporting success for work it did not do.");
  console.error("");

  // Grouped by reason: a wholesale assembly skip is one cause and hundreds of
  // lines, and the cause is the part worth reading.
  const byReason = new Map();
  for (const s of unexplained) {
    if (!byReason.has(s.reason)) byReason.set(s.reason, []);
    byReason.get(s.reason).push(s.name);
  }

  for (const [reason, names] of [...byReason].sort((a, b) => b[1].length - a[1].length)) {
    console.error(`  ${names.length} test(s): ${reason || "<no reason recorded>"}`);
    for (const n of names.slice(0, 3)) console.error(`      ${n}`);
    if (names.length > 3) console.error(`      ... and ${names.length - 3} more`);
    console.error("");
  }

  if (dockerReachable) {
    console.error("Docker WAS reachable when this run started, so a 'Docker unavailable' reason above");
    console.error("means FixtureGate misclassified a real bring-up failure. See tests/Shared/FixtureGate.cs.");
  }

  return 1;
}

if (import.meta.main) {
  Deno.exit(await main(Deno.args));
}
