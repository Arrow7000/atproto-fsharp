# FSharp.ATProto Documentation Critique

## The Verdict Up Front

These docs are **competent but not compelling**. They read like internal reference material -- thorough, accurate, well-structured for someone who already decided to use the library. But they fail at the *first* job of documentation: convincing a stranger to care.

If I landed here from a search for "F# Bluesky library," I'd spend about 90 seconds scanning the README, think "okay, that looks solid," then go check if there's a C# library with a bigger community. Nothing here grabs me by the collar and says "this will make your life dramatically easier." The docs inform. They don't *sell*.

The good news: the underlying library is clearly well-built. The API design is genuinely thoughtful. The docs just need to catch up to the quality of the code.

---

## First Impressions: README and Landing Page

### README

**The hero example is good but wasted.** The `taskResult` CE example at the top is genuinely compelling F# code. It shows login, post, like, reply, and undo in 8 lines. That's excellent. But then the README *immediately repeats a simpler version of the same thing* in the "Getting Started" section. That's 3 lines of nearly-identical code right below the hero. Pick one. The hero already IS the getting started.

**The "Design" bullet points are abstract.** "If it compiles, it's correct" -- great tagline. But it's just asserted, not shown. The hero example has a comment saying "the compiler prevents mix-ups" but doesn't show what a mix-up would look like. Show me the wrong code that doesn't compile. That's the payoff moment.

**The architecture diagram is premature.** Six-layer ASCII art means nothing to someone evaluating the library. They don't care about DRISL or CBOR. That belongs in a contributor guide, not the README.

**The test count table is noise.** Nobody evaluating a library looks at "758 tests in Syntax, 112 in DRISL" and thinks anything useful. It's filler that pushes the things they care about further down the page.

**The AI Transparency section is brave and well-written.** I mean that sincerely. It's honest, it explains how correctness is maintained, and it doesn't apologize. This is the single best section of the README. But it's at the bottom where almost nobody will read it. Consider moving it to a separate page and linking to it.

**Missing from the README:**
- What Bluesky/AT Protocol IS (one sentence for the uninitiated)
- A link to live docs site
- NuGet badge (or a note saying it's not published yet, if that's the case)
- A "Why this library?" section comparing to alternatives

### Landing Page (docs/index.md)

The landing page is a less good version of the README. It repeats the feature list in a slightly different format, repeats the architecture table, and adds an installation section. The "Quick Example" is the *third* time I've seen login-and-post code.

**Core problem:** The README and landing page are ~70% overlapping content. They should serve different purposes:
- README = elevator pitch, social proof, links to docs
- Landing page = "I'm here, orient me, get me started fast"

Right now they're both trying to be everything and succeeding at neither.

---

## The Quickstart

This is **actually pretty good**. It's the strongest page in the docs. Five minutes is a reasonable promise, the prerequisites are clear, the code builds up incrementally, and the complete example at the end ties it together.

**Problems:**

1. **The package doesn't exist on NuGet.** `dotnet add package FSharp.ATProto.Bluesky` will fail. There is no version 0.1.0 published anywhere. The docs present this as a published library, but it appears to be source-only. This is the single most embarrassing gotcha -- someone follows the quickstart and hits a wall on step 1. Either publish the package or document the real installation process (cloning the repo, building from source, project references).

2. **`.Result` is a code smell in async F#.** The quickstart uses `result.Result` to synchronously block on the task. In real F# code you'd use `Async.RunSynchronously` or run it from an async context. The fact that the entry point pattern forces `.Result` is a friction point worth acknowledging.

3. **`agent.Session.Value.Handle`** -- that `.Value` is an option dereference that can throw. On the very first page where you're teaching people the library is "no exceptions, ever," you show them Option.Value. Use pattern matching.

4. **The "Like a Post" section constructs PostRef manually.** This feels like it should have a helper. You show `let postRef = { PostRef.Uri = firstPost.Uri; Cid = firstPost.Cid }` -- is there a `PostRef.ofTimelinePost` or similar? If not, there should be. If there is, the docs should show it.

5. **No mention of App Password creation.** The prerequisites say "a Bluesky account with an App Password" and link to the settings page. A screenshot or step-by-step ("go to Settings > App Passwords > Create...") would save first-timers a trip.

---

## Guide Quality: Page by Page

### Posts (posts.md) -- SOLID

The best guide of the bunch. Good progression from simple to complex, covers the major operations (create, read, quote, reply, thread, search, delete), and the "Power users" callouts for raw XRPC access are a nice pattern.

**Issues:**
- `Result.defaultWith failwith` appears in several examples for parsing AT-URIs. This is exactly the pattern you told users NOT to use ("Results, not exceptions"). Show proper error handling or at least acknowledge the shortcut.
- The `task { }` examples use verbose `match result with | Ok ... | Error ...` every single time. This is correct but repetitive. By the 6th example, the reader gets it. Consider showing the `taskResult` CE earlier in guides, since it eliminates 80% of the boilerplate.
- `post.Author.DisplayName` is used in examples but `DisplayName` is likely a `string` that could be empty. Is there a fallback pattern you recommend?

### Social Actions (social.md) -- TOO LONG

This is 690 lines. It covers liking, reposting, following, blocking, five layers of undo, viewer state, followers/follows, likes/reposts, suggested follows, muting, reporting, notifications (with three subsections), and paginating notifications.

**This page is doing the work of three pages.** It should be split:
- Social Actions (like/repost/follow/block/undo) -- the core verbs
- Notifications (fetching, marking read, paginating)
- Moderation (muting, blocking, reporting)

As-is, someone looking for "how to mute a user" has to scroll through 400+ lines of like/repost/follow documentation.

**The undo section is over-documented.** Five different undo mechanisms explained in detail, plus a comparison table. This is thorough but overwhelming. Lead with the one most people should use, mention the others exist, and move on. The table is a good summary but it shouldn't require reading 100 lines of explanation to reach it.

### Feeds (feeds.md) -- GOOD

Clean, well-organized. The domain type explanations (`FeedItem`, `TimelinePost`, `FeedReason`) are genuinely helpful.

**Issues:**
- Custom feeds have no convenience wrapper -- you drop down to raw XRPC. Fair enough, but worth a sentence saying "convenience wrappers for custom feeds may be added in a future release" or similar.
- The `Xrpc.paginate` example at the bottom is complex but good. The issue is that it's buried in a guide about feeds. This should primarily live in the Pagination guide with a cross-reference.

### Chat (chat.md) -- FINE

Does what it needs to do. The progression from starting a conversation to sending messages to managing conversations is logical.

**Issues:**
- `Did.parse "did:plc:xyz123" |> Result.defaultWith failwith` -- there's that pattern again. In a "Getting Started" section, you're teaching people to `failwith` on parse errors.
- The "A Note on Attachments" at the end is oddly phrased. If image attachments aren't supported, just say "Image attachments in DMs are not yet supported by the Bluesky API." Don't explain the internal union type.
- No example showing how to get a DID for a user you want to message. The guide starts with "get or create a conversation with DIDs" but doesn't show how to get a DID from a handle, which is the typical starting point.

### Profiles (profiles.md) -- GOOD

The SRTP explanation is handled well -- "it accepts Handle, Did, or string" is the right level of detail. The domain type table is helpful.

**Issue:** The "Working with Typed Identifiers" section at the bottom repeats what's in the Identity guide. Pick one home for this content.

### Rich Text (rich-text.md) -- GOOD

Clear explanation of a genuinely tricky topic (UTF-8 byte offsets). The three-tier API (auto/detect+resolve/manual) is well-presented.

**Issue:** `RichText.graphemeLength` and `RichText.byteLength` appear without context. Why would I need these? Add a sentence: "Check grapheme length before posting to avoid the server rejecting your post for exceeding the 300-grapheme limit."

### Pagination (pagination.md) -- ADEQUATE BUT PAINFUL

The content is accurate but the consumption pattern is ugly. Every pagination example is 12+ lines of mutable state, while loops, and manual enumerator management:

```fsharp
let enumerator = pages.GetAsyncEnumerator()
let mutable hasMore = true
while hasMore do
    let! moved = enumerator.MoveNextAsync()
    ...
```

This is the most un-F#-like code in the entire documentation. For a library that prides itself on functional style, showing mutable loops with manual enumerator management is a bad look.

**Suggestion:** Either provide a helper function (`Paginator.forEachPage`, `Paginator.collectAll`, or integrate with `taskSeq` or `AsyncSeq`) that makes consumption idiomatic, or at least acknowledge the awkwardness and explain why `IAsyncEnumerable` was chosen over a more F#-native approach.

### Identity (identity.md) -- EXCELLENT

The best guide after Posts. Clean, the comparison table at the bottom is chef's-kiss, and the bidirectional verification explanation is clear without being condescending.

No significant issues.

### Media (media.md) -- TOO SHORT

This is the thinnest guide (153 lines) and it only covers images. The title is "Media" but there's no mention of:
- Video (even to say "not supported")
- External link cards / link previews
- OG image embeds
- GIF behavior (is it animated? or a still frame?)
- Image size limits and recommended dimensions

The 1MB limit is mentioned at the bottom in passing. This should be a prominent callout box, not a footnote.

---

## Cross-Cutting Problems

### 1. The `task {}` vs `taskResult {}` confusion

The quickstart uses `taskResult {}`. Most guides use `task {}` with manual `match result with`. Some guides use `taskResult {}` in certain examples. There's no consistent recommendation for which to use.

**The docs never explain `taskResult` as a concept.** It appears in the hero example with a one-line comment, then the quickstart uses it, then individual guides switch to `task {}` without explanation. A newcomer will be confused about:
- Where `taskResult` comes from (is it built into F#? A library type?)
- When to use `taskResult` vs `task` + match
- How error types flow through the CE

This needs its own section, ideally in the quickstart or a dedicated "Error Handling" guide.

### 2. No error handling guide

Every function returns `Result<'T, XrpcError>`. But nowhere in the docs is `XrpcError` fully explained. What are its cases? How do you handle rate limiting? What does a 401 look like? What about network timeouts? The library has auto-retry on 401 and 429 -- that's a feature worth documenting prominently.

### 3. `Result.defaultWith failwith` is everywhere

This pattern appears in at least 6 different guides for parsing AT-URIs and DIDs. It directly contradicts the "Results, not exceptions" philosophy. The docs should either:
- Provide a helper that makes this less ugly (e.g., `AtUri.parseUnsafe`)
- Show proper error handling in examples
- Add a footnote explaining "we use this shortcut in examples for brevity; in production code, handle the error case"

### 4. No explanation of AT Protocol concepts

The docs assume you know what a DID is, what an AT-URI looks like, what a CID represents, what XRPC means, and how the protocol works. A newcomer who just wants to "post to Bluesky from F#" will encounter these terms on every page with zero explanation.

You need a "Concepts" page that explains in 2-3 sentences each:
- What Bluesky is and how it relates to AT Protocol
- DID (think of it as a permanent user ID)
- Handle (the human-readable username)
- AT-URI (an address for any piece of content)
- CID (a fingerprint of specific content version)
- PDS (the server that hosts your data)
- XRPC (the protocol's RPC mechanism -- you mostly don't need to know this)
- Lexicon (the schema system -- you definitely don't need to know this)

### 5. No runnable examples

None of the code examples are tested or verified to compile. They're written in markdown code blocks, not F# script files. This means:
- Typos can creep in
- API changes can make examples stale
- Users can't `dotnet fsi my-example.fsx` to try things

The gold standard is Rust's `mdbook` approach where examples are extracted and compiled as part of CI. Short of that, at minimum provide a `/samples/` directory with working .fsx scripts.

### 6. No API reference

The sidebar has an "API Reference > All Namespaces" link. I assume fsdocs generates this from XML doc comments. But:
- It's never mentioned or linked from any guide
- There's no "here's how to read the API reference" section
- The domain types (`PostRef`, `Profile`, `TimelinePost`, etc.) are defined inline in guides but never in one consolidated place

### 7. The package installation is fiction

`dotnet add package FSharp.ATProto.Bluesky` with `Version="0.1.0"` appears on the landing page and in the quickstart. As far as I can tell, this package is not published on NuGet. This is the most critical issue. Either:
- Publish the package
- Remove the installation instructions and replace with source-build instructions
- Add a prominent "Pre-release" banner

---

## What's Missing

### Critical gaps
1. **Error handling guide** -- What is `XrpcError`? What are its cases? How do I handle rate limiting, auth expiry, network errors?
2. **Concepts/glossary page** -- DID, Handle, AT-URI, CID, PDS, XRPC, Lexicon explained for newcomers
3. **Installation that actually works** -- either publish to NuGet or document the real process
4. **A "Build a Bot" tutorial** -- end-to-end: monitor a hashtag, auto-like posts, respond to mentions. This is the #1 use case for Bluesky libraries.
5. **`taskResult` CE documentation** -- what it is, where it comes from, when to use it
6. **Configuration guide** -- custom PDS endpoints, timeouts, retry behavior, HTTP client configuration

### Nice to have
7. **Troubleshooting / FAQ** -- common errors and their fixes
8. **Rate limiting docs** -- what are the limits, how does the library handle them
9. **Session management** -- how long sessions last, when/how they refresh
10. **Thread safety** -- is `AtpAgent` safe to share across tasks?
11. **Changelog** -- what changed between versions
12. **Working sample projects** -- a bot, a CLI tool, a web app
13. **Video support status** -- even if just "not yet supported"

---

## Design and Navigation

### What works
- The sidebar grouping (Overview > Guides > API Reference) is logical
- Guides are reasonably sized (except Social Actions)
- The "Power users" pattern for raw XRPC access is a good design decision
- Cross-links between guides are present and helpful
- The CSS theme is clean and the Bluesky-blue branding is appropriate

### What doesn't work
- **The landing page and README are redundant.** Someone who reads the README and clicks through to docs sees 70% of the same content again.
- **No search.** fsdocs includes it, but it's hard to find. If someone wants "how do I mute a user," they need to guess it's in "Social Actions."
- **The guide order is odd.** Posts, Profiles, Feeds, Social Actions, Chat, Rich Text, Media, Identity, Pagination. A more natural learning progression would be: Posts > Social Actions > Feeds > Profiles > Media > Chat > Rich Text > Identity > Pagination (save the plumbing for last).
- **No breadcrumbs or "you are here" indicator** in the mobile menu.
- **No versioning.** When the API changes, old docs should still be accessible.

---

## The Hard Questions

### Is this library ready to advertise publicly?

No. The blocking issue is that you can't install it. Beyond that, the docs assume prior AT Protocol knowledge, the quickstart has code patterns that contradict the library's philosophy, and there's no end-to-end tutorial. The API itself looks excellent; the docs aren't ready for it.

### What would embarrass the author?

1. A prominent F# developer runs `dotnet add package FSharp.ATProto.Bluesky` and gets "Package not found."
2. Someone copies the quickstart verbatim and gets a `NullReferenceException` from `agent.Session.Value.Handle`.
3. A reviewer notices that a library promoting "no exceptions" uses `Result.defaultWith failwith` in every guide.
4. The pagination examples are mutable-while-loop imperative code in a "functional-first" library.

### What would make someone choose a C# wrapper instead?

- The C# library is on NuGet and this isn't
- The C# library has a "Build a Bot in 10 Minutes" tutorial
- The C# library has Stack Overflow answers
- The C# library has more GitHub stars and a Discord community

### What would make this a "wow" experience?

- A `dotnet new` template: `dotnet new bskybot` scaffolds a working bot
- Interactive examples in the browser (Fable + Bolero, or at minimum F# Interactive snippets)
- A "Cookbook" section with 15+ recipes (post an image, monitor a hashtag, auto-follow-back, export your posts, etc.)
- Generated API docs with inline examples
- A comparison table with other AT Protocol libraries showing feature parity

---

## If I Were Redesigning This Docs Site From Scratch

### Proposed Sitemap

```
/                           Landing page (hero example, 3 bullets, install, links)
/quickstart                 Zero to first post (5 min)
/tutorial/build-a-bot       End-to-end bot tutorial (20 min)
/concepts                   AT Protocol for humans (DID, Handle, AT-URI, CID, PDS)
/guides/
    posts                   Create, read, reply, quote, thread, search, delete
    social                  Like, repost, follow, block, undo (ONLY these)
    feeds                   Timeline, author feed, custom feeds, bookmarks
    profiles                Fetch, search, viewer state
    media                   Images, blobs, limits
    chat                    DMs, conversations, reactions
    notifications           Fetch, mark read, paginate
    moderation              Mute, block, report
    rich-text               Facets, detection, resolution
    identity                Handle/DID resolution, verification
    error-handling          XrpcError, taskResult CE, retry behavior, rate limits
    pagination              Cursors, paginators, IAsyncEnumerable patterns
    raw-xrpc                Dropping to generated wrappers, advanced usage
/cookbook/
    hashtag-monitor         Watch a hashtag and auto-like
    follow-back-bot         Auto-follow-back new followers
    thread-poster           Post a thread from a text file
    image-poster            Post images from a folder
    dm-responder            Auto-respond to DMs
/api/                       Generated API reference (fsdocs)
/changelog                  Version history
/contributing               For contributors, architecture, building from source
```

### Content Strategy

1. **Landing page**: 30 seconds to understand + install. Hero example (the one you have is good), three value props, install command, "Read the quickstart" button. That's it. No architecture diagram, no test counts, no feature list.

2. **Quickstart**: Keep what you have but fix the installation story, remove `.Value` usage, and end with "Next: Build a Bot Tutorial."

3. **Tutorial**: A complete, copy-paste-and-run tutorial. "Build a Bluesky bot that monitors #fsharp and likes every post." This is the page that converts tire-kickers into users.

4. **Concepts page**: One paragraph per concept. No code. Just "here's what these words mean." Link to it from every guide that uses AT Protocol terminology.

5. **Guides**: Focused, single-topic, self-contained. Each guide should be readable in 5-10 minutes. The current guides are mostly there; split Social Actions into three and add Error Handling.

6. **Cookbook**: Short, complete, copy-pasteable recipes. Each one is a single file that does one useful thing. This is where your library goes from "I understand it" to "I use it daily."

7. **README**: Trim to: hero example, three design bullets, install command, link to docs, link to NuGet, badges. Everything else goes to the docs site.

### Per-Page Word Budget

| Page | Target | Current |
|------|--------|---------|
| Landing page | 300 words | ~600 words |
| Quickstart | 800 words | ~1200 words (good) |
| Tutorial | 1500 words | does not exist |
| Concepts | 500 words | does not exist |
| Each guide | 500-800 words | 400-2500 words (wildly uneven) |
| Each cookbook recipe | 200-400 words | does not exist |

### The One Change That Would Have the Most Impact

**Publish the NuGet package.** Everything else is refinement. But until `dotnet add package` works, nothing else matters. The docs can be Stripe-quality and it won't matter if the first command fails.

### The Second Change

**Write the "Build a Bot" tutorial.** A newcomer with a working tutorial will forgive incomplete reference docs. A newcomer with perfect reference docs but no tutorial will leave.

---

## Summary: What's Good, What's Bad, What's Ugly

**Good:**
- Hero example is genuinely compelling F# code
- API design is thoughtful (typed refs, SRTP polymorphism, domain types)
- Guide structure is logical (mostly)
- "Power users" escape hatch to raw XRPC is a great pattern
- Identity guide is excellent
- The CSS theme is clean
- AI Transparency section is honest and well-argued

**Bad:**
- README and landing page are 70% redundant
- No concepts/glossary for AT Protocol newcomers
- No error handling documentation
- No end-to-end tutorial
- `taskResult` CE is used but never explained
- Social Actions page is three pages crammed into one
- Pagination examples are imperative mutable code
- Guide order doesn't match learning progression
- Rich Text guide doesn't explain why you'd need `graphemeLength`

**Ugly:**
- Package installation instructions are for a package that doesn't exist
- `Result.defaultWith failwith` everywhere contradicts "no exceptions" philosophy
- `agent.Session.Value.Handle` in quickstart can throw
- The same login-and-post example appears three times across README/landing/quickstart
