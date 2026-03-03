# README & Documentation Rewrite Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Rewrite the README and all 11 documentation files to accurately reflect the current API (post-v3 ergonomics) with a warm, welcoming tone that leads with code.

**Architecture:** Documentation-only changes across 12 markdown files. No code changes. Each task rewrites one or more files. Verification = docs build cleanly.

**Tech Stack:** Markdown, fsdocs frontmatter (YAML)

---

## Master API Corrections

These errors exist across the current docs and MUST be fixed:

1. **`like`/`repost` take `PostRef`, not separate `Uri`+`Cid`**: Current docs show `Bluesky.like agent post.Uri post.Cid`. Correct: `Bluesky.like agent postRef` where `postRef = { Uri = post.Uri; Cid = post.Cid }`
2. **`like` returns `LikeRef`, not `AtUri`**: Current social.md says returns `AtUri`. Correct: returns `LikeRef` (a record with `Uri: AtUri`)
3. **`ImageUpload.MimeType` is `ImageMime` DU, not `string`**: Current docs show `MimeType = "image/jpeg"`. Correct: `MimeType = Jpeg` (cases: `Png`, `Jpeg`, `Gif`, `Webp`, `Custom of string`)
4. **`uploadBlob` takes `ImageMime`, not `string`**: Current media.md shows `uploadBlob agent data "image/png"`. Correct: `uploadBlob agent data Png`
5. **`Bluesky.login` is the preferred auth**: Current quickstart/index use `AtpAgent.create` + `AtpAgent.login`. Should use `Bluesky.login` (combined, returns `Task<Result<AtpAgent, XrpcError>>`)
6. **`taskResult` CE should be showcased**: None of the current docs use it. It should be the primary way to show multi-step workflows
7. **Missing convenience functions**: `quotePost`, `getPostThreadView`, `getFollowers`, `getFollows`, `followByHandle`, `blockByHandle`, `unlikePost`, `unrepostPost`, `paginateTimeline`, `paginateFollowers`, `paginateNotifications` are undocumented
8. **Missing types**: `UndoResult` DU (`Undone | WasNotPresent`), `ThreadResult` type alias, `BlobRef` record, `ImageMime` DU, `IdentityError` DU
9. **`PostView` extensions undocumented**: `.Text`, `.Facets`, `.AsPost` should replace `Record.GetProperty("text").GetString()` pattern
10. **SRTP `getProfile`**: Accepts `Handle`, `Did`, or `string`. Should be shown in profiles guide
11. **Test count**: Current badge says "1,600+". Should be "1,636"
12. **`postWith` doesn't exist**: `posts.md` references `Bluesky.postWith`. Correct name: `Bluesky.postWithFacets`
13. **`AtpSession` has typed `Did`/`Handle`**: Current quickstart shows `session.Handle` and `session.Did` as plain strings (they print OK via `%s`/`%A` but they are typed)

---

### Task 1: Rewrite README.md

**Files:**
- Modify: `README.md`

**Step 1: Write the new README**

Structure:
1. Header (logo SVG, badges with corrected test count 1,636)
2. Warm one-line tagline + "Built from the ground up in F#" subtitle
3. Hero code example using `taskResult` CE (~15 lines showing login, post, like, reply, undo)
4. Design philosophy (4 bullets: type safety, protocol complexity hidden, Results not exceptions, generated from spec)
5. Getting Started (NuGet install + minimal example)
6. Feature showcase (small snippets: rich text, images, social, chat, identity, pagination, XRPC wrappers)
7. Architecture diagram (simplified)
8. Building & Testing (commands + test counts)
9. License

Key corrections in README:
- Use `Bluesky.login` not `AtpAgent.create` + `AtpAgent.login`
- Hero example shows `PostRef` flowing to `like`, `replyTo` auto-resolving roots, `undo` generic
- `like` takes `PostRef` not separate `Uri`+`Cid`
- Images use `ImageMime` DU (`Jpeg`) not string
- Show `.Text` extension on `PostView`
- Badge: 1,636 tests

**Step 2: Verify**

Visually review that all code examples match the actual API signatures from `Bluesky.fs`.

**Step 3: Commit**

```bash
git add README.md
git commit -m "Rewrite README with hero snippet, warm tone, and corrected API examples"
```

---

### Task 2: Rewrite docs/index.md

**Files:**
- Modify: `docs/index.md`

**Step 1: Write the new index page**

Structure:
1. Frontmatter (keep existing category/index structure)
2. Warm intro paragraph matching README tone
3. Features list (updated: include `taskResult` CE, `PostView` extensions, typed undos, convenience methods)
4. Installation (NuGet, same as current)
5. Quick example using `Bluesky.login` (not `AtpAgent.create`)
6. Next Steps links (same set of 10 guides)
7. Architecture table (updated test count to 1,636)

Key corrections:
- Use `Bluesky.login` not `AtpAgent.create` + `AtpAgent.login`
- Test count: "over 1,636 tests" (not 1,500)
- Update feature bullets to mention `taskResult`, convenience methods, typed undos

**Step 2: Commit**

```bash
git add docs/index.md
git commit -m "Update docs landing page with corrected API and test count"
```

---

### Task 3: Rewrite docs/quickstart.md

**Files:**
- Modify: `docs/quickstart.md`

**Step 1: Write the new quickstart**

Structure:
1. Frontmatter (same)
2. Prerequisites (.NET 10 + App Password)
3. Create project (same `dotnet new` commands)
4. Log In -- use `Bluesky.login` (3 args: baseUrl, identifier, password) instead of `AtpAgent.create` + `AtpAgent.login`. Show full Program.fs with `taskResult` CE
5. Make First Post -- `Bluesky.post agent "text"`, show `PostRef` return
6. Read Timeline -- use `Bluesky.getTimeline agent (Some 10L) None` convenience method + `PostView.Text` extension (not `Record.GetProperty("text")`)
7. Like a Post -- `Bluesky.like agent { Uri = post.Uri; Cid = post.Cid }` returns `LikeRef`
8. Reply -- `Bluesky.replyTo agent "text" postRef` (auto thread root)
9. Post with Images -- use `ImageMime.Jpeg` DU, not string
10. Complete Example -- full `taskResult` CE program tying it all together
11. What's Next links

Key corrections:
- `Bluesky.login` replaces `AtpAgent.create` + `AtpAgent.login`
- `taskResult` CE for the complete example
- `PostView.Text` replaces `Record.GetProperty("text").GetString()`
- `like` takes `PostRef`, not `Uri + Cid`
- `ImageUpload.MimeType` is `Jpeg` (DU), not `"image/jpeg"` (string)
- Remove `failwithf` from complete example (use `taskResult` error short-circuit instead)

**Step 2: Commit**

```bash
git add docs/quickstart.md
git commit -m "Rewrite quickstart with Bluesky.login, taskResult CE, and corrected API"
```

---

### Task 4: Rewrite docs/guides/posts.md

**Files:**
- Modify: `docs/guides/posts.md`

**Step 1: Write the new posts guide**

Sections:
1. Creating a Post -- `Bluesky.post agent "text"` (same, but emphasize `PostRef` return type)
2. Reading Posts -- use `PostView.Text` and `PostView.Facets` extensions (not `Record.GetProperty`)
3. Quote Posts -- NEW: `Bluesky.quotePost agent "text" quotedPostRef`
4. Replying -- `Bluesky.replyTo agent "text" parentRef` (auto root). Also `replyWithKnownRoot`
5. Threads -- show BOTH `Bluesky.getPostThreadView` (simple, returns `ThreadViewPost option`) AND raw `getPostThread` with `ThreadResult` pattern matching
6. Searching Posts -- same
7. Deleting a Post -- `Bluesky.deleteRecord agent postRef.Uri`
8. Posting with Pre-Resolved Facets -- `Bluesky.postWithFacets` (fix: was `postWith`)

**Step 2: Commit**

```bash
git add docs/guides/posts.md
git commit -m "Update posts guide with quotePost, getPostThreadView, PostView extensions"
```

---

### Task 5: Rewrite docs/guides/profiles.md

**Files:**
- Modify: `docs/guides/profiles.md`

**Step 1: Write the new profiles guide**

Sections:
1. Fetching a Profile -- show `Bluesky.getProfile agent "handle"` (convenience, SRTP) + mention it accepts `Handle`, `Did`, or `string`
2. Fetching Multiple Profiles -- `AppBskyActor.GetProfiles.query` (same)
3. Understanding Profile Types -- table (same)
4. Viewer State -- same, but use typed Did/Handle accessors
5. Searching -- same
6. Typed Identifiers -- same but updated

**Step 2: Commit**

```bash
git add docs/guides/profiles.md
git commit -m "Update profiles guide with SRTP getProfile convenience method"
```

---

### Task 6: Rewrite docs/guides/feeds.md

**Files:**
- Modify: `docs/guides/feeds.md`

**Step 1: Write the new feeds guide**

Sections:
1. Reading Your Timeline -- show `Bluesky.getTimeline` convenience method + raw `GetTimeline.query`
2. Author Feed -- same
3. Custom Feeds -- same
4. Feed Generator Metadata -- same
5. Understanding Feed Items -- use `PostView.Text` extension, fix `FeedViewPostReasonUnion` usage
6. Discovering Feeds -- same
7. Pagination -- show `Bluesky.paginateTimeline` pre-built paginator (primary), `Xrpc.paginate` (advanced)

**Step 2: Commit**

```bash
git add docs/guides/feeds.md
git commit -m "Update feeds guide with getTimeline, paginateTimeline, PostView.Text"
```

---

### Task 7: Rewrite docs/guides/social.md

**Files:**
- Modify: `docs/guides/social.md`

This guide needs the most corrections.

**Step 1: Write the new social guide**

Sections:
1. Liking a Post -- `Bluesky.like agent postRef` returns `LikeRef` (NOT `AtUri`). Fix: takes `PostRef` not separate args
2. Reposting -- `Bluesky.repost agent postRef` returns `RepostRef`. Same fix
3. Following -- `Bluesky.follow agent did` returns `FollowRef`. Also show `followByHandle agent "handle"` for string-based
4. Blocking -- `Bluesky.block agent did` returns `BlockRef`. Also show `blockByHandle agent "handle"`
5. Undoing Actions -- NEW: show all undo patterns:
   - `Bluesky.unlike agent likeRef` (ref-based, returns `unit`)
   - `Bluesky.undoLike agent likeRef` (typed, returns `UndoResult`)
   - `Bluesky.unlikePost agent postRef` (target-based, returns `UndoResult` which can be `WasNotPresent`)
   - `Bluesky.undo agent likeRef` (generic SRTP, works on any ref type)
   - `Bluesky.deleteRecord agent uri` (lowest level)
6. Checking Viewer State -- same but show typed fields
7. Followers and Follows -- show `Bluesky.getFollowers`/`Bluesky.getFollows` convenience methods + raw query

**Step 2: Commit**

```bash
git add docs/guides/social.md
git commit -m "Rewrite social guide with typed refs, UndoResult, followByHandle, getFollowers"
```

---

### Task 8: Rewrite docs/guides/chat.md

**Files:**
- Modify: `docs/guides/chat.md`

**Step 1: Write the new chat guide**

Sections:
1. Getting Started -- use `Bluesky.login` (not `AtpAgent.create` + `AtpAgent.login`). Explain that Chat module auto-applies proxy header (no need for `withChatProxy`)
2. Starting a Conversation -- `Chat.getConvoForMembers agent [ did ]`
3. Sending Messages -- `Chat.sendMessage agent convoId "text"`. Rich text via `ChatBskyConvo.SendMessage.call`
4. Reading Messages -- `Chat.getMessages agent convoId (Some 20L) None`
5. Reactions -- same
6. Managing Conversations -- same
7. Attachments note -- same

Key correction: Remove `AtpAgent.withChatProxy` from getting started. Chat module handles this automatically.

**Step 2: Commit**

```bash
git add docs/guides/chat.md
git commit -m "Update chat guide with Bluesky.login and auto proxy header note"
```

---

### Task 9: Rewrite docs/guides/rich-text.md

**Files:**
- Modify: `docs/guides/rich-text.md`

**Step 1: Write the new rich text guide**

Sections are mostly correct. Key fixes:
1. Fix `postWith` -> `postWithFacets`
2. Show `postWithFacets` correctly: `Bluesky.postWithFacets agent text facets`
3. Use `Bluesky.login` in any auth examples
4. Verify all function signatures match

**Step 2: Commit**

```bash
git add docs/guides/rich-text.md
git commit -m "Fix postWithFacets name and verify rich text API examples"
```

---

### Task 10: Rewrite docs/guides/media.md

**Files:**
- Modify: `docs/guides/media.md`

**Step 1: Write the new media guide**

Key corrections:
1. `ImageUpload.MimeType` is `ImageMime` DU (not `string`): `MimeType = Jpeg` not `MimeType = "image/jpeg"`
2. Document `ImageMime` DU cases: `Png | Jpeg | Gif | Webp | Custom of string`
3. `uploadBlob` takes `ImageMime` not string: `Bluesky.uploadBlob agent data Png`
4. Document `BlobRef` return type (has `Json`, `Ref`, `MimeType`, `Size` fields)
5. Add GIF to supported formats list

**Step 2: Commit**

```bash
git add docs/guides/media.md
git commit -m "Fix media guide with ImageMime DU, BlobRef type, corrected uploadBlob signature"
```

---

### Task 11: Rewrite docs/guides/identity.md

**Files:**
- Modify: `docs/guides/identity.md`

**Step 1: Write the new identity guide**

Key changes:
1. Show `IdentityError` DU: `XrpcError of XrpcError | DocumentParseError of string`
2. Show typed `AtprotoIdentity` fields (`Did: Did`, `Handle: Handle option`, `PdsEndpoint: Uri option`, `SigningKey: string option`)
3. Update examples to use `Bluesky.login`
4. Use `Did.value` and `Handle.value` for printing (they're typed, not strings)

**Step 2: Commit**

```bash
git add docs/guides/identity.md
git commit -m "Update identity guide with IdentityError DU and typed AtprotoIdentity fields"
```

---

### Task 12: Rewrite docs/guides/pagination.md

**Files:**
- Modify: `docs/guides/pagination.md`

**Step 1: Write the new pagination guide**

Restructure to lead with pre-built paginators:
1. Quick Start -- `Bluesky.paginateTimeline agent (Some 25L)` (the easy path)
2. Other Pre-Built Paginators -- `paginateFollowers`, `paginateNotifications`
3. Consuming Pages -- `MoveNextAsync` loop
4. Custom Pagination -- `Xrpc.paginate` for any endpoint (advanced)
5. How Cursors Work -- same
6. Single-Page Queries -- same

**Step 2: Commit**

```bash
git add docs/guides/pagination.md
git commit -m "Lead pagination guide with pre-built paginators, add paginateFollowers/Notifications"
```
