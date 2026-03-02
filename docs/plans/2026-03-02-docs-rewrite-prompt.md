# Docs Rewrite Session Prompt

Copy everything below the line and use it as your prompt in a fresh Claude Code session.

---

I'm on branch `convenience-layer-audit` working on the FSharp.ATProto library. The code is done (1,723 tests pass) but the docs need a significant rewrite. A brutally honest docs critique is at `docs/plans/2026-03-02-docs-critique.md` — please read it carefully. It has excellent feedback but note that I'm aware the NuGet package isn't published yet (I'll do that once docs are ready), so you can deprioritise that point.

## The core problem

The docs evolved incrementally across many sessions and are now inconsistent with the codebase. For example:
- Most guides use `task {}` with manual `match result with Ok/Error` because they predate the `taskResult` CE — the codebase itself uses `taskResult` and we should teach that as the primary pattern
- `Result.defaultWith failwith` appears everywhere in guides, contradicting our "Results, not exceptions" philosophy
- `agent.Session.Value.Handle` in the quickstart uses `.Value` which can throw
- The README, landing page, and quickstart repeat the same login-and-post example three times
- No explanation of AT Protocol concepts (DID, AT-URI, CID) anywhere
- No error handling guide documenting `XrpcError`, rate limiting, retry semantics
- The social.md guide is 690 lines covering three topics (social verbs, notifications, moderation)
- Pagination examples use mutable while-loops — the most un-F# code in a "functional-first" library

## What to do

### Phase 1: Audit codebase vs docs

Before changing anything, read through the actual source code to find discrepancies:
- `src/FSharp.ATProto.Bluesky/Bluesky.fs` — all convenience functions, domain types, paginators
- `src/FSharp.ATProto.Bluesky/Chat.fs` — chat functions
- `src/FSharp.ATProto.Core/TaskResult.fs` — the taskResult CE
- `src/FSharp.ATProto.Core/Xrpc.fs` — XrpcError type, rate limiting, auto-retry behaviour
- `src/FSharp.ATProto.Core/AtpAgent.fs` — session management, AtpSession type

Flag up:
- Functions that exist in code but aren't documented
- Functions documented with wrong signatures or return types
- Patterns in docs that have better alternatives in the codebase
- Domain types or features that the docs don't mention

### Phase 2: Restructure

The critic proposed this sitemap — use it as a starting point but apply your own judgment:

```
/                           Landing page (hero, 3 bullets, install, links)
/quickstart                 Zero to first post (5 min)
/tutorial/build-a-bot       End-to-end bot tutorial (20 min)
/concepts                   AT Protocol for humans (DID, Handle, AT-URI, CID, PDS)
/guides/
    posts                   Create, read, reply, quote, thread, search, delete
    social                  Like, repost, follow, block, undo (ONLY these core verbs)
    feeds                   Timeline, author feed, custom feeds, bookmarks
    profiles                Fetch, search, viewer state, upsert
    media                   Images, blobs, limits
    chat                    DMs, conversations, reactions
    notifications           Fetch, mark read, paginate
    moderation              Mute users/threads, mute/block lists, report
    rich-text               Facets, detection, resolution
    identity                Handle/DID resolution, verification
    error-handling          XrpcError, taskResult CE, retry behaviour, rate limits
    pagination              Cursors, paginators, IAsyncEnumerable patterns
    raw-xrpc                Dropping to generated wrappers, advanced usage
/api/                       Generated API reference (fsdocs)
```

Key structural changes:
- Split social.md into social (core verbs), notifications, and moderation
- Add concepts page, error-handling guide, raw-xrpc guide
- Add "Build a Bot" tutorial
- Deduplicate README vs landing page (README = elevator pitch + links, landing = orient + get started)

### Phase 3: Rewrite guides

For every guide:
- Use `taskResult {}` as the primary pattern, with a brief note that `task {}` + manual matching is also available
- Replace `Result.defaultWith failwith` with proper error handling or at minimum a footnote
- Fix any `.Value` usage on options
- Ensure all code examples use current domain types and function signatures
- Keep guides focused and self-contained (500-800 words each)
- Cross-link to the concepts page when AT Protocol terms appear

### Phase 4: New content

Write these new pages:
1. **Concepts page** — One paragraph per concept: Bluesky, AT Protocol, DID, Handle, AT-URI, CID, PDS, XRPC, Lexicon. No code. Just "here's what these words mean."
2. **Error handling guide** — Document `XrpcError` type and its fields, the `taskResult` CE (what it is, where it comes from, when to use it vs `task {}`), auto-retry on 401/429, rate limiting behaviour
3. **Build a Bot tutorial** — End-to-end: create a project, install packages, authenticate, monitor a hashtag with `searchPosts`, auto-like matching posts, handle errors gracefully. Complete working code.
4. **Moderation guide** — `muteUser`/`unmuteUser`, `muteThread`/`unmuteThread`, `muteModList`/`unmuteModList`, `blockModList`/`unblockModList`, `reportContent` with `ReportSubject` DU
5. **Notifications guide** — `getNotifications`, `getUnreadNotificationCount`, `markNotificationsSeen`, `paginateNotifications`, `Notification` and `NotificationKind` domain types

### Style guidelines

- The hero example in the README is good — keep it
- "Power Users: Raw XRPC" sections at the end of guides is a good pattern — keep it
- Don't over-explain. Respect the reader's intelligence. Show, don't tell.
- Every code example should be copy-pasteable and correct
- Use `taskResult {}` consistently as the primary pattern
- Domain types first, raw XRPC as escape hatch
- The pagination examples need to look more F#-like — consider adding a helper or at least acknowledging the IAsyncEnumerable awkwardness

### What NOT to do

- Don't worry about NuGet publishing — I'll handle that separately
- Don't change any source code — only docs
- Don't add emojis
- Don't create a changelog (there are no versions yet)
- Don't add cookbook/recipes beyond the single bot tutorial (we can add more later)

## Reference files

- Docs critique: `docs/plans/2026-03-02-docs-critique.md`
- Gap analysis vs TS SDK: `docs/plans/2026-03-02-gap-analysis.md`
- Current docs: `docs/` directory (index.md, quickstart.md, guides/*.md)
- README: `README.md`
