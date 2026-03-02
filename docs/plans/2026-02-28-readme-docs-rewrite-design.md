# README & Documentation Rewrite Design

**Date**: 2026-02-28
**Goal**: Rewrite the README and all documentation guides to accurately reflect the current library (post-v3 ergonomics) and grab F# developers immediately.

## Audience & Tone

- **Primary audience**: F# developers looking for a Bluesky/AT Proto library
- **Tone**: Warm and welcoming
- **Strategy**: Lead with code -- let the API speak for itself

## README Structure

### 1. Header + Hero Snippet
- Logo SVG + badges (CI, .NET 10, test count, MIT)
- One-line warm tagline (not the current wall of text)
- Immediately: a ~15-line `taskResult` example demonstrating:
  - `Bluesky.login` (one-step auth)
  - `Bluesky.post` (auto rich-text detection)
  - `Bluesky.like` returning `LikeRef` (distinct domain types)
  - `Bluesky.replyTo` (auto thread-root resolution)
  - `Bluesky.undo` (generic SRTP undo)
  - Return type annotation showing `Task<Result<_, XrpcError>>`

### 2. Design Philosophy (brief)
4 bullets, one sentence each:
- **If it compiles, it's correct** -- distinct types for every domain concept
- **The library handles protocol complexity** -- thread roots, rich text, chat proxies
- **Results, not exceptions** -- every function returns `Result`
- **Generated from the spec** -- 324 Lexicon schemas, 237 XRPC wrappers

### 3. Getting Started
- NuGet install command
- Simple login + post example
- Link to quickstart guide

### 4. Feature Showcase
Small focused snippets (~3-5 lines each) for:
- Rich text (auto-detection + manual)
- Images (`postWithImages`)
- Social graph (follow/block, undo operations)
- Chat / DMs (auto proxy, `Chat.sendMessage`)
- Identity resolution (`resolveIdentity`)
- Pagination (`paginateTimeline` + `IAsyncEnumerable`)
- Full XRPC access (generated wrappers)

### 5. Architecture (brief)
- Simplified layer diagram
- One sentence per layer
- Package names for NuGet

### 6. Build & Test
- Build/test commands
- Test counts per project

### 7. License
- MIT badge/link

## Documentation Guides Update

All 11 doc files (index + quickstart + 9 guides) need updating to reflect the v3 API:

### Changes across all guides:
- Use `taskResult` CE where it makes examples cleaner
- Use `Bluesky.login` instead of `AtpAgent.create` + `AtpAgent.login` where appropriate
- Show `UndoResult` pattern for undo operations
- Use typed undo functions (`undoLike`, `unrepostPost`, etc.)
- Show `getPostThreadView` alongside raw thread access
- Show pre-built paginators (`paginateTimeline`, `paginateFollowers`)
- Ensure all examples compile against current API
- Use warm, welcoming tone throughout

### Per-guide notes:
1. **index.md**: Update feature list, match README tone, update test count
2. **quickstart.md**: Use `Bluesky.login`, `taskResult` CE, show `getProfile` with SRTP
3. **posts.md**: Add `quotePost`, `getPostThreadView`, `ThreadResult` alias
4. **profiles.md**: Show SRTP `getProfile` accepting Handle/Did/string
5. **feeds.md**: Use pre-built `paginateTimeline`, show `PostView.Text`/`.Facets`
6. **social.md**: Show `UndoResult` DU, typed undos, `unlikePost`/`unrepostPost`, `followByHandle`/`blockByHandle`, `getFollowers`/`getFollows` convenience functions
7. **chat.md**: Ensure examples use current Chat module signatures
8. **rich-text.md**: Verify examples match current API
9. **media.md**: Add `BlobRef` type, `ImageMime` DU
10. **identity.md**: Add `IdentityError` type, verify examples
11. **pagination.md**: Add pre-built paginators, show `IAsyncEnumerable` consumption patterns
