# Contributing to JauntyQ

Thank you for your interest in JauntyQ. **Read this section before writing any code** — JauntyQ is
source-available rather than open source, and two rules here are unusual enough that finding out
about them after you have done the work would waste your time.

## Read this first

### 1. JauntyQ is not open source, and you need a signed CLA to contribute

The source is published under the **Islamic Software License – Restricted (ISL-R) v1.2**
(`LICENSE.md`), with the Islamic Software License – Output Exception (ISL-OE) v1.2 and the JauntyQ
Redistribution Exception v1.0 (`EXCEPTION.md`, `LICENSE-REDISTRIBUTION-EXCEPTION.md`). You may read
it and use it. You may **not** modify or redistribute it — except that ISL-R Section 2 allows
exactly that "with prior written consent from the Licensor."

**The [Islamic Software License Contributor License Agreement (ISL-CLA)](CLA.md) is that written
consent.** Signing it authorises you to fork and patch for the purpose of contributing, and licenses
your contribution to Extrode LLC broadly enough that it can ship in the commercial binaries. It does
not let you publish a modified JauntyQ as your own product; that stays prohibited.

> The ISL-CLA is currently a **draft pending legal review**, so contributions are not yet being
> accepted. Issues and discussion are welcome now.

#### How JauntyQ adopts the ISL-CLA

JauntyQ adopts the **ISL-CLA, Version 1.1**, by reference. The text is reproduced whole and
unmodified in [CLA.md](CLA.md) and published at
[islamiclicense.org/isl-cla](https://islamiclicense.org/isl-cla/1.1/CLA.md). As that Agreement requires, the
Project states here:

| | Statement |
|---|---|
| (a) Project License | ISL-R, Version 1.2, as applied by Extrode LLC in `LICENSE.md`. ISL-CLA Section 2 therefore applies: signing is the prior written consent ISL-R Section 2 requires. |
| (b) Section 6, No AI-Generated Contributions | **Adopted.** |
| (c) Governing law | The laws of the Commonwealth of Virginia, United States of America. |
| (d) Licensor | Extrode LLC, the party named in Section 1.2 of `LICENSE.md`. |
| (e) Forum | The state and federal courts sitting in the Commonwealth of Virginia. |

**How to sign.** ISL-CLA Section 9.6: add a row to [CONTRIBUTORS.md](CONTRIBUTORS.md) in the
first pull request that carries your contribution, from your own account; or, if you contribute by
patch or email, send a written statement that you have read the ISL-CLA, agree to it, and at which
version, and a maintainer records the row for you. Acceptance runs from the moment you begin
preparing a contribution, conditional on that row being made.

### 2. No AI-generated contributions

**Contributions must be written by a human being.** Code produced in whole or in part by an AI
assistant or language model is not accepted — see [CLA.md](CLA.md) Section 6, which JauntyQ adopts,
for the full rule, the scope, and the reasoning.

**Describing a change in writing is the preferred way to contribute.** A precise bug report with
reproduction steps, or a written argument for a design, is more useful to this project than a patch,
and we will implement it ourselves. What we cannot accept is generated code whose copyright
provenance we are unable to establish.

| Welcome | Not accepted |
|---|---|
| Bug reports and reproduction steps in your own words | Patches written by an AI assistant |
| A prose description of a proposed design | AI-generated tests or documentation |
| A written explanation of a defect and where it is | Code you cannot explain line by line |

Every pull request must carry the contributor certification from the template below, and it must
be true.

---

## Table of Contents

- [Read this first](#read-this-first)
- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Workflow](#development-workflow)
- [Coding Standards](#coding-standards)
- [Testing Guidelines](#testing-guidelines)
- [Pull Request Process](#pull-request-process)
- [Code Review Guidelines](#code-review-guidelines)

---

## Code of Conduct

- Be respectful and inclusive in all interactions
- Focus on constructive feedback
- Welcome contributors of all skill levels

---

## Getting Started

### Prerequisites

- .NET 10.0 SDK (see `global.json` for the exact version pinned)
- Git for version control
- IDE of choice (Visual Studio, VS Code, Rider)

### Setup

0. **Sign the [CLA](CLA.md) first, once it is final.** Forking and patching without it is a
   modification ISL-R does not permit, which is why contributions are closed while the ISL-CLA
   is a draft. The steps below describe the workflow that opens when it is in force.
1. Fork the repository
2. Clone your fork: `git clone https://github.com/YOUR_USERNAME/jauntyq.git`
3. Create a branch: `git checkout -b feature/your-feature-name`

---

## Development Workflow

### Branch Naming

- `feature/` - New features
- `fix/` - Bug fixes
- `docs/` - Documentation updates
- `refactor/` - Code refactoring
- `test/` - Test additions or modifications

### Commit Messages

Follow conventional commit format:

```
<type>(<scope>): <subject>

<body>

<footer>
```

**Types:**
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation
- `style`: Formatting, missing semicolons, etc.
- `refactor`: Code refactoring
- `test`: Adding tests
- `chore`: Maintenance tasks

**Example:**
```
feat(generator): Add support for @stream result sets

Added streaming IAsyncEnumerable output for queries tagged
with the @stream directive.

Closes #123
```

---

## Coding Standards

### C# Conventions

- Use `var` when the type is obvious
- Use explicit types when clarity is improved
- Prefer expression-bodied members for simple methods
- Use XML documentation comments for all public APIs

### Naming Conventions

- **Classes**: PascalCase (`SchemaSnapshot`, `QueryGenerator`)
- **Methods**: PascalCase (`ExecuteQuery`, `ParseSql`)
- **Properties**: PascalCase (`ConnectionString`, `Dialect`)
- **Parameters**: camelCase (`connection`, `sql`, `cancellationToken`)
- **Private fields**: camelCase with underscore prefix (`_cache`, `_mapper`)
- **Interfaces**: PascalCase with "I" prefix (`ISchemaProvider`, `IDialect`)

### File Organization

- One class per file (with exceptions for small related types)
- File name matches class name
- Organize files by namespace and functionality

### Code Style Examples

```csharp
// Good: Clear, concise, well-documented
/// <summary>
/// Parses a .sql file into a validated query definition.
/// </summary>
public static QueryDefinition Parse(string sql, SchemaSnapshot schema)
{
    return QueryParser.ParseCore(sql, schema, ParseMode.Strict);
}

// Avoid: Unclear naming, missing documentation
public static QueryDefinition DoParse(string s, SchemaSnapshot sc)
{
    return QueryParser.ParseCore(s, sc, ParseMode.Strict);
}
```

---

## Testing Guidelines

### Test Organization

- Place tests in `tests/Extrode.JauntyQ.<Component>.Tests/`, mirroring the `src/` project it covers
- Mirror source directory structure within each test project
- Use descriptive test method names: `MethodName_Scenario_ExpectedResult`

### Assertion Library

**JauntyQ uses xUnit's built-in Assert class** for all assertions.

**Examples:**

```csharp
// Equality checks
Assert.Equal(expected, actual);
Assert.NotEqual(unexpected, actual);

// Null checks
Assert.Null(value);
Assert.NotNull(value);

// Collection checks
Assert.Empty(collection);
Assert.NotEmpty(collection);
Assert.Single(collection);
Assert.Contains(item, collection);
Assert.DoesNotContain(item, collection);
Assert.All(collection, item => Assert.True(condition));

// Boolean checks
Assert.True(condition);
Assert.False(condition);

// Exception checks
var ex = Assert.Throws<ExceptionType>(() => action);
Assert.Contains("expected message", ex.Message);

// String checks
Assert.StartsWith(prefix, value);
Assert.EndsWith(suffix, value);
Assert.Contains(substring, value);
```

### Test Types

1. **Unit Tests**: Test individual methods/classes in isolation
2. **Integration Tests**: Test schema extraction and generated code against real databases
   (SQL Server, PostgreSQL, MySQL, SQLite via Testcontainers)
3. **Edge Case Tests**: Test boundary conditions and error scenarios

### Test Example

```csharp
[Fact]
public void Parse_UnknownTable_ThrowsSchemaValidationException()
{
    var schema = SchemaSnapshot.Empty;

    var ex = Assert.Throws<SchemaValidationException>(() =>
        QueryParser.Parse("SELECT * FROM Missing", schema));

    Assert.Contains("Missing", ex.Message);
}
```

### Running Tests

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test tests/Extrode.JauntyQ.SqlParser.Tests
```

---

## Pull Request Process

### Before Submitting

1. Your row is in [CONTRIBUTORS.md](CONTRIBUTORS.md), or this pull request adds it, and you
   personally authored every line
2. Ensure all tests pass: `dotnet test`
3. Build succeeds without warnings: `dotnet build`
4. Code follows project conventions
5. XML documentation added for public APIs
6. Update documentation if behavior changes
7. **`Extrode.JauntyQ.Runtime` stays zero-dependency.** Do not add a `PackageReference` to it —
   see [docs/07-roadmap/roadmap.md](docs/07-roadmap/roadmap.md) for why.

### PR Description Template

```markdown
## Description
Brief description of changes

## Type of Change
- [ ] Bug fix (non-breaking change that fixes an issue)
- [ ] New feature (non-breaking change that adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to change)
- [ ] Documentation update

## Testing
- [ ] Tests added/updated
- [ ] All tests pass locally
- [ ] Integration tests pass

## Checklist
- [ ] Code follows project conventions
- [ ] Code is documented
- [ ] No new warnings introduced
- [ ] No new dependency added to Extrode.JauntyQ.Runtime

## Contributor certification
I have read and agree to the Islamic Software License Contributor License
Agreement, Version 1.1 (CLA.md), as adopted by JauntyQ in CONTRIBUTING.md,
and my entry is in CONTRIBUTORS.md. I personally authored this contribution.
No part of it was generated by an AI or machine-learning system.
```

### Review Process

1. Submit PR with clear description
2. Wait for automated checks to pass
3. Address reviewer feedback
4. Squash commits if requested
5. PR will be merged by maintainer

---

## Code Review Guidelines

### For Authors

- Respond to feedback promptly
- Explain reasoning for complex changes
- Be open to suggestions

### For Reviewers

- Focus on logic, architecture, and correctness
- Be constructive and respectful
- Suggest improvements, not just criticisms
- Acknowledge good solutions

### Review Checklist

- [ ] Code compiles without errors
- [ ] Tests pass
- [ ] Logic is correct
- [ ] Edge cases handled
- [ ] Error handling appropriate
- [ ] Performance considered
- [ ] Security considered
- [ ] Documentation complete
- [ ] Follows coding standards

---

## Questions?

- Open an issue for questions or discussions
- Check existing issues before creating new ones
- Tag issues appropriately (bug, enhancement, question)

---

Thank you for contributing to JauntyQ!
