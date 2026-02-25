<!-- @format -->

# FSharp.ATProto Project Instructions

## Decision Authority

The user does not have strong opinions on implementation details. Use your best judgment for all technical decisions — architecture, internal APIs, data structures, algorithms, error handling patterns, test organization, etc. Only surface questions when you are genuinely stuck between options with no clear winner, or when a decision affects the **user-facing API shape of the library** (public types, module structure, function signatures that consumers will call).

If your judgment says option A is better than B or C, go with A.

## Implementation Strategy

If context is <35% full, go with the subagent driven approach in this session. Otherwise advise the user to clear the context and give him specific instructions for how to prompt you for the next phase of the work – make sure you give the prompt a friendly tone :)

## Design Principles

- **Functional-first**: Prefer the FP paradigm. Immutable data, pure functions, composition. Violate this only when there is a clear trade-off justification (e.g., performance, interop with mutable .NET APIs).
- **Robustness**: Every layer must be solid before the next builds on it ("wall of correctness").
- **Spec compliance via automation**: Validate against the AT Protocol spec through automated tests — interop test vectors, property-based tests, parsing all 324 real lexicon files. No manual testing.
- **Native F#**: No C# library wrappers. Only low-level .NET primitives.

## Build & Test

- .NET 10 SDK at `/usr/local/share/dotnet/` (system install)
- Test framework: Expecto 10.2.3 + FsCheck 2.16.6
- `[<Tests>]` attribute required on Expecto test values for discovery
- Run all tests: `dotnet test`
- Run specific project: `dotnet test tests/FSharp.ATProto.Syntax.Tests`

## Project Structure

```
src/
  FSharp.ATProto.Syntax/       # Identifiers (DID, Handle, NSID, etc.)
  FSharp.ATProto.DRISL/        # DRISL/CBOR encoding + CID
  FSharp.ATProto.Lexicon/      # Lexicon schema parser + validator
  FSharp.ATProto.CodeGen/      # CLI: Lexicon -> F# source (Phase 4)
  FSharp.ATProto.Core/         # XRPC client, session auth, rate limiting, pagination
  FSharp.ATProto.Bluesky/      # Generated types + rich text, identity, convenience methods
tests/
  FSharp.ATProto.Syntax.Tests/  # 726 tests
  FSharp.ATProto.DRISL.Tests/   # 112 tests
  FSharp.ATProto.Lexicon.Tests/ # 387 tests
  FSharp.ATProto.CodeGen.Tests/ # 169 tests
  FSharp.ATProto.Core.Tests/    # 30 tests
  FSharp.ATProto.Bluesky.Tests/ # 48 tests
extern/
  atproto/                     # Git submodule: lexicon schemas (324 files)
  atproto-interop-tests/       # Git submodule: test vectors
```

## Git

- Avoid merge commits. Merge branches into `main` with fast-forward strategy. Or if `main` is ahead of the feature branch, rebase the branch on `main` and then merge with fast-forward.
- Never add Claude as co-author on commit messages
