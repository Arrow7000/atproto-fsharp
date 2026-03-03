# API Consistency & Docs Restructuring Design

Date: 2026-03-03

## Goals

1. Make the convenience API consistent: every function that takes an actor, post, or convo identifier also accepts the corresponding entity type (ProfileSummary, Profile, TimelinePost, etc.)
2. Enforce type safety: no bare strings standing in for typed identifiers (Did, Handle, AtUri). Validation is the caller's responsibility.
3. Restructure docs into three sections with a new "Type Reference" level between tutorials and API reference.
4. Standardize on `taskResult {}` CE throughout docs.

---

## Part 1: API Consistency

### Design Principle

If a typed identifier exists for a concept (Did, Handle, AtUri), functions must accept that typed identifier or an entity containing it. Never a bare string. Convo IDs are the exception — they're opaque server-generated strings with no validation scheme, so `string` is the native type.

### New SRTP Witnesses

**ActorDidWitness** — for write operations that need a DID:

| Input Type | Output |
|---|---|
| ProfileSummary | .Did |
| Profile | .Did |
| Did | identity |

Applied to: `follow`, `block`, `muteUser`, `unmuteUser`

**PostUriWitness** — for operations that need an AtUri:

| Input Type | Output |
|---|---|
| TimelinePost | .Uri |
| PostRef | .Uri |
| AtUri | identity |

Applied to: `removeBookmark`, `getPostThread`, `getPostThreadView`, `getLikes`, `getRepostedBy`, `getQuotes`, `muteThread`, `unmuteThread`, `deleteRecord`

**ConvoWitness** — for chat operations that need a convo ID:

| Input Type | Output |
|---|---|
| ConvoSummary | .Id |
| string | identity (native type) |

Applied to: `sendMessage`, `getMessages`, `deleteMessage`, `markRead`, `muteConvo`, `unmuteConvo`, `acceptConvo`, `leaveConvo`, `addReaction`, `removeReaction`, `getConvo`

### Extended Witness

**ActorWitness** — for read operations (string removed):

| Input Type | Output |
|---|---|
| ProfileSummary | Did.value .Did |
| Profile | Did.value .Did |
| Handle | Handle.value |
| Did | Did.value |

Applied to: `getProfile` (already uses this), `getFollowers`, `getFollows`, `getAuthorFeed`, `getActorLikes`, `getSuggestedFollows`, `paginateFollowers`

### Existing Witness (unchanged)

**PostRefWitness** — already accepts PostRef | TimelinePost for write operations that need both Uri + Cid.

Applied to: `like`, `repost`, `replyTo`, `quotePost`, `unlikePost`, `unrepostPost`, `addBookmark`

**UndoWitness** — already accepts LikeRef | RepostRef | FollowRef | BlockRef | ListBlockRef.

Applied to: `undo`

### List Functions

SRTP doesn't compose with lists. These take the canonical key type:

- `getProfiles`: `Did list` (changed from `string list`)
- `getPosts`: `AtUri list` (unchanged)
- `getConvoForMembers`: `Did list` (unchanged)

### ByHandle Variants (unchanged)

Keep all `*ByHandle` functions as separate explicit operations. They do async handle resolution, which is a fundamentally different code path.

- `followByHandle`, `blockByHandle`, `muteUserByHandle`, `unmuteUserByHandle`

### Breaking Changes

- `getProfile agent "alice.bsky.social"` no longer compiles — use `Handle.tryCreate` first
- `getFollowers agent "alice.bsky.social"` no longer compiles — same
- `getProfiles agent ["alice"; "bob"]` no longer compiles — use `Did list`
- All other read functions that currently take `string` for actors — same pattern

These are intentional. Typical consumer flow:

```fsharp
// Most common: entities from API calls, already typed
let! profile = Bluesky.getProfile agent myDid
let! followers = Bluesky.getFollowers agent profile

// From external input: validate first
match Handle.tryCreate "alice.bsky.social" with
| Some handle -> let! followers = Bluesky.getFollowers agent handle
| None -> // handle error

// Handle resolution for writes: explicit ByHandle variant
let! follow = Bluesky.followByHandle agent "alice.bsky.social"
```

### Implementation Pattern

Each witness follows the existing pattern in Bluesky.fs:

```fsharp
type ActorDidWitness =
    | ActorDidWitness
    static member inline ToDid(ActorDidWitness, d: Did) = d
    static member inline ToDid(ActorDidWitness, p: ProfileSummary) = p.Did
    static member inline ToDid(ActorDidWitness, p: Profile) = p.Did
```

Each function using SRTP needs an `*Impl` function (non-inline, does the actual work) and an `inline` wrapper (resolves the witness). This is the existing pattern used by PostRefWitness.

---

## Part 2: Docs Restructuring

### Three Sections

**Getting Started** (tutorials, learn by doing):

| Index | Page | Notes |
|---|---|---|
| 0 | FSharp.ATProto (landing) | Keep |
| 1 | Quickstart | Update code for API changes |
| 2 | Build a Bot | Update code |
| 3 | Concepts | Keep |
| 4 | Error Handling | Move here, update |

Each Getting Started page gets a small note at the top: one-line explanation of `taskResult` linking to the Error Handling page. This way readers landing on any page aren't confronted with an unfamiliar CE.

**Type Reference** (catalog pages — new format):

| Index | Page | Notes |
|---|---|---|
| 5 | Posts | Rewrite from guide to catalog |
| 6 | Profiles | Rewrite from guide to catalog |
| 7 | Social Actions | Rewrite from guide to catalog |
| 8 | Feeds | Rewrite from guide to catalog |
| 9 | Chat | Rewrite from guide to catalog |
| 10 | Notifications | Rewrite from guide to catalog |

**Advanced Guides** (cross-cutting topics):

| Index | Page | Notes |
|---|---|---|
| 11 | Media | Minor updates |
| 12 | Rich Text | taskResult consistency |
| 13 | Identity | Keep |
| 14 | Moderation | Minor updates |
| 15 | Pagination | taskResult consistency |
| 16 | Raw XRPC | Keep |

### Type Catalog Page Template

Each Type Reference page follows a consistent structure:

1. **One-sentence intro** — what this concept is on Bluesky
2. **Domain types** — field tables for each relevant type
3. **Functions** — grouped by operation, each with signature + one-liner
4. **Short code snippet** per function group showing most common usage

### CE Consistency Rules

- `taskResult {}` everywhere
- Exception: Build a Bot uses `task {}` deliberately (bot needs to keep running on errors) — explained in that page
- Error Handling page shows `task {}` equivalent as a teaching comparison — the one place both appear side by side
- Where a function returns bare `Task`, wrap it for `taskResult` compatibility
- No `taskResult` intro note needed in Type Reference or Advanced Guides sections — only Getting Started

### fsdocs Category Structure

Use fsdocs frontmatter `category` and `categoryindex` to create three sidebar sections:

```yaml
# Getting Started pages
category: Getting Started
categoryindex: 1

# Type Reference pages
category: Type Reference
categoryindex: 2

# Advanced Guides pages
category: Advanced Guides
categoryindex: 3
```
