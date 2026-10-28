# API unit and endpoint tests

Run from the repository root:

```powershell
dotnet test Tests/Inkstone.Api.Tests/Inkstone.Api.Tests.csproj -p:SkipRustBuild=true
```

The build uses .NET 10. The tests do not launch Rust, Vue, a network server, or an external database. `SkipRustBuild=true` skips the normal backend converter build; production builds keep their existing behavior.

The suite contains:

- Pure request/operation validation tests for every automation operation, required fields, bounded UTF-8 text and decoded images, target/reference shapes, numeric limits, duplicate JSON keys, unknown properties, source revisions, bulk records, token scopes and job contracts.
- HTTP endpoint tests mapping the production automation handlers into ASP.NET TestServer with a separate SQLite memory database for each test and a controlled `IDocumentProcessor`. These verify authentication/ownership/version checks, structured validation responses, no engine calls or writes on rejected input, atomic failures, revision history, preview privacy, retry receipts, job snapshots/cancellation/quotas and token hashing/revocation/expiry/lockout.
- Authorization regressions for uppercase and trailing-slash route variants, which must enforce the same token scopes as their canonical routes.

The test-only identity handler supplies synthetic cookie identities. The actual bearer-token middleware is used. The full production cookie and CSRF pipeline, real DOCX editing/rendering and converter process limits remain covered by `python Tests/document_automation_smoke.py` and the existing Python API suites. Unit tests isolate HTTP/storage behavior; they do not claim to validate Word rendering fidelity.

To retain a TRX result inside the ignored build directory:

```powershell
dotnet test Tests/Inkstone.Api.Tests/Inkstone.Api.Tests.csproj -p:SkipRustBuild=true --logger trx --results-directory Application/target/endpoint-tests
```
