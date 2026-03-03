# Literate F# Script Migration Plan

**Goal:** Convert doc pages from `.md` to `.fsx` literate scripts so code snippets are compiler-checked by fsdocs. A type error in a doc snippet = a build error.

**Branch:** Create from `main`

---

## Background

fsdocs supports [literate F# scripts](https://fsprojects.github.io/FSharp.Formatting/literate.html). You write `.fsx` files where:
- Prose lives in `(**` ... `*)` blocks (rendered as markdown)
- Code outside those blocks is compiled and optionally shown
- `(*** hide ***)` hides the next code block from output
- `(*** include-output ***)` shows evaluation output
- Frontmatter goes in `(** --- title: ... --- *)`

## Scope

Convert the 6 **Type Reference** pages (these have the most code snippets):
- `docs/guides/posts.md` → `docs/guides/posts.fsx`
- `docs/guides/profiles.md` → `docs/guides/profiles.fsx`
- `docs/guides/social.md` → `docs/guides/social.fsx`
- `docs/guides/feeds.md` → `docs/guides/feeds.fsx`
- `docs/guides/chat.md` → `docs/guides/chat.fsx`
- `docs/guides/notifications.md` → `docs/guides/notifications.fsx`

Leave the Getting Started and Advanced Guides pages as `.md` for now — they can be migrated later if this works well.

## Key Challenge: API Calls Need Auth

The code snippets call `Bluesky.post`, `Bluesky.like`, etc. which need a live `AtpAgent`. Since fsdocs *compiles* but doesn't *execute* `.fsx` files by default, the code just needs to type-check — it doesn't need to actually run.

However, F# scripts require expressions to be valid. You can't just write `Bluesky.post agent "hello"` without `agent` being defined. Solution:

1. Each `.fsx` file starts with a hidden preamble that references the project DLLs and creates a dummy agent:

```fsharp
(*** hide ***)
#r "../../src/FSharp.ATProto.Syntax/bin/Release/net10.0/FSharp.ATProto.Syntax.dll"
#r "../../src/FSharp.ATProto.Core/bin/Release/net10.0/FSharp.ATProto.Core.dll"
#r "../../src/FSharp.ATProto.Bluesky/bin/Release/net10.0/FSharp.ATProto.Bluesky.dll"
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax

// Dummy agent for type-checking only — never executed
let agent = Unchecked.defaultof<AtpAgent>
```

2. Code snippets use `ignore` or `|> ignore` to avoid "expression should have type unit" warnings:

```fsharp
taskResult {
    let! post = Bluesky.post agent "Hello from F#!"
    return post
}
|> ignore
```

3. For snippets that show patterns (like match expressions), wrap in a function:

```fsharp
let example () = taskResult {
    let! thread = Bluesky.getPostThreadView agent someUri None None
    match thread with
    | Some tp -> printfn "Post: %s" tp.Post.Text
    | None -> printfn "Not found"
}
```

## Task List

### Task 1: Convert `posts.fsx` as proof of concept

1. Create `docs/guides/posts.fsx` with:
   - Frontmatter in `(** --- ... --- *)` block (same category/index)
   - Hidden preamble with `#r` references
   - All prose in `(**` ... `*)` blocks
   - All code snippets as real F# code
   - Tables stay in the `(**` blocks (they're just markdown)
2. Delete `docs/guides/posts.md`
3. Run `dotnet fsdocs build` and verify:
   - The page renders identically
   - No compile errors
   - Sidebar still works
4. Commit

### Task 2: Convert remaining 5 pages

Same pattern as Task 1 for each page. Can be done in parallel since they're independent files.

### Task 3: Update docs workflow

The `dotnet fsdocs build` command should already pick up `.fsx` files. Verify the GitHub Actions workflow still works. May need to ensure the Release build runs first (for the `#r` DLL references).

### Task 4: Add a note to CLAUDE.md

Add a note that Type Reference docs are literate `.fsx` files and code changes that break doc snippets will cause build failures.

## Verification

- `dotnet fsdocs build` succeeds with no errors
- All 6 pages render correctly (same content as before)
- Sidebar categories still work
- Intentionally breaking a function signature causes a doc build failure (the whole point!)

## Risks

- **`#r` paths are fragile** — they reference `bin/Release/net10.0/` paths. If the build output changes, the scripts break. Mitigation: the fsdocs build step already runs `dotnet build --configuration Release` first.
- **fsdocs script evaluation** — by default fsdocs may try to *evaluate* scripts, not just compile them. If so, add `--strict` or check fsdocs flags to disable evaluation. The `Unchecked.defaultof<AtpAgent>` approach means evaluation would crash. May need `(*** do-not-eval-file ***)` directive.
- **Generated types** — the Bluesky DLL includes generated types from lexicon schemas. The `#r` approach should pull these in, but verify that types like `AppBskyFeed.Post` resolve correctly in the script.
