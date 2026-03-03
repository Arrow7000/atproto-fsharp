# Gap Analysis: AT Protocol TS SDK (@atproto/api) vs F# SDK (FSharp.ATProto)

## Executive Summary

The TS SDK (`@atproto/api`) and our F# SDK both have raw XRPC access to all 324 lexicon endpoints via
generated code. The question is: what **convenience abstractions** does the TS SDK provide beyond raw
XRPC access that we don't? This analysis identifies every gap, categorized by feature area.

**Key finding**: The TS SDK's biggest differentiating feature is its **moderation system** (~800 lines
of complex label interpretation logic). Most other gaps are thin convenience wrappers or preference
manipulation helpers that are primarily useful for full client applications rather than bots/automation.

---

## 1. Moderation System (MAJOR GAP)

### What the TS SDK provides

The TS SDK has a comprehensive, ~1200-line moderation subsystem in `packages/api/src/moderation/`:

**Core functions:**
- `moderatePost(subject, opts)` -> `ModerationDecision`
- `moderateProfile(subject, opts)` -> `ModerationDecision`
- `moderateNotification(subject, opts)` -> `ModerationDecision`
- `moderateFeedGenerator(subject, opts)` -> `ModerationDecision`
- `moderateUserList(subject, opts)` -> `ModerationDecision`

**ModerationDecision class:**
- `.ui(context)` where context is `'profileList' | 'contentList' | 'profileView' | 'contentView' | 'contentMedia' | 'avatar'`
- Returns `ModerationUI` with `{ filter, blur, alert, inform, noOverride }` booleans + cause arrays
- `.blocked`, `.muted`, `.blockCause`, `.muteCause`, `.labelCauses` getters
- `ModerationDecision.merge(...)` to combine decisions

**Label interpretation:**
- `interpretLabelValueDefinition()` / `interpretLabelValueDefinitions()`
- Built-in constants for known labels: `!hide`, `!warn`, `!no-unauthenticated`, `porn`, `sexual`, `nudity`, `graphic-media`
- Each label has defined behaviors across 6 UI contexts (profileList, contentList, profileView, contentView, contentMedia, avatar)

**Muted word matching:**
- `hasMutedWord(mutedWords, text, facets, outlineTags, languages, actor, actorTarget)` -> bool
- Progressive matching: tag matching, CJK substring, exact match, phrase match, word-level match
- Checks across post text, image alt text, quote post text, link card titles
- Expiration handling, following-exclusion

**How it works conceptually:**
```typescript
const decision = moderatePost(postView, {
  userDid: agent.did,
  prefs: moderationPrefs,
  labelDefs: labelDefinitions,
})
const ui = decision.ui('contentList')
if (ui.filter) { /* hide from list */ }
if (ui.blur)   { /* show behind click-through */ }
if (ui.alert)  { /* show warning badge */ }
if (ui.inform) { /* show info badge */ }
```

### Can our raw XRPC do this?
**No.** This is pure client-side logic - it interprets labels, muted words, blocks, and user preferences
to produce UI decisions. There's no XRPC endpoint for this. A client must implement this logic locally.

### Priority assessment
- **For bots/automation**: LOW. Bots typically don't need to render moderation UI. They might want to
  check if content is labeled or if a user is blocked, but they don't need the full 6-context UI system.
- **For client apps**: ESSENTIAL. Any app displaying content must implement moderation to be a proper
  Bluesky client.

### Complexity estimate
**HIGH** (~2-3 weeks). This is the single most complex feature. It involves:
- ~15 types (ModerationOpts, ModerationDecision, ModerationUI, ModerationCause variants, label types)
- 5 subject-specific decision functions (post, profile, notification, feed generator, user list)
- Label interpretation with 6 UI context behaviors
- Muted word matching with progressive multi-strategy algorithm
- Block/mute/hide/label cause aggregation and merging

### F# design sketch
```fsharp
type ModerationContext = ProfileList | ContentList | ProfileView | ContentView | ContentMedia | Avatar

type ModerationUI =
    { Filters : ModerationCause list
      Blurs : ModerationCause list
      Alerts : ModerationCause list
      Informs : ModerationCause list
      NoOverride : bool }

type ModerationDecision =
    { Did : Did; IsMe : bool; Causes : ModerationCause list }

module Moderation =
    val moderatePost : ModerationOpts -> PostView -> ModerationDecision
    val moderateProfile : ModerationOpts -> ProfileView -> ModerationDecision
    val ui : ModerationContext -> ModerationDecision -> ModerationUI
```

---

## 2. Preference Management (MEDIUM GAP)

### What the TS SDK provides

The Agent class has ~25 preference manipulation methods that read-modify-write the user's
`app.bsky.actor.putPreferences` record with concurrency protection:

**Content moderation prefs:**
- `setAdultContentEnabled(bool)` - toggles adult content visibility
- `setContentLabelPref(key, value, labelerDid?)` - sets label visibility (ignore/warn/hide)

**Feed management:**
- `overwriteSavedFeeds(feeds[])` - replaces all saved feeds
- `updateSavedFeeds(feeds[])` - updates specific feeds' pinned status
- `addSavedFeeds(feeds[])` - adds new feeds with TID generation
- `removeSavedFeeds(ids[])` - removes by ID
- `setFeedViewPrefs(feed, prefs)` - configures per-feed display (hide replies, reposts, etc.)

**Thread/post interaction:**
- `setThreadViewPrefs(prefs)` - thread sort order, display options
- `setPostInteractionSettings(settings)` - threadgate/postgate defaults
- `setInterestsPref(interests)` - interest tags

**Muted words (stored in preferences):**
- `addMutedWord(word)` / `addMutedWords(words[])` - with sanitization + TID generation
- `updateMutedWord(word)` - modifies existing
- `removeMutedWord(word)` / `removeMutedWords(words[])` - removes

**Hidden posts (stored in preferences):**
- `hidePost(uri)` / `unhidePost(uri)` - manages hidden post URIs in preferences

**Personal details:**
- `setPersonalDetails({ birthDate })` - stores birth date

**Labeler management:**
- `addLabeler(did)` / `removeLabeler(did)` - enables/disables labeler subscriptions
- `getLabelDefinitions(prefs)` - fetches and interprets label definitions

**App state (Bluesky-specific NUX/nudges):**
- `bskyAppQueueNudges(ids)` / `bskyAppDismissNudges(ids)` - nudge queue management
- `bskyAppSetActiveProgressGuide(guide)` - onboarding guide
- `bskyAppUpsertNux(nux)` / `bskyAppRemoveNuxs(ids)` - new user experience items

**Verification:**
- `setVerificationPrefs(settings)` - controls verification badge visibility

**Live events:**
- `updateLiveEventPreferences(action)` - manage hidden live event feeds

**The core pattern** - all preference methods use `updatePreferences()` which:
1. Calls `getPreferences()`
2. Applies the mutation callback
3. Calls `putPreferences()` with the updated array
4. Uses an async lock to prevent concurrent modifications

### What we have
We have `getPreferences` that returns the raw preferences. We do NOT have any preference mutation
helpers. Users must manually construct the preference array and call `putPreferences` via raw XRPC.

### Can our raw XRPC do this?
**Yes.** `app.bsky.actor.getPreferences` + `app.bsky.actor.putPreferences` are available. But the
convenience is in the read-modify-write pattern with proper merging logic.

### Priority assessment
- **For bots/automation**: LOW. Bots rarely need to manage saved feeds, muted words, or content preferences.
- **For client apps**: HIGH. Essential for settings screens.

### Complexity estimate
**MEDIUM** (~1 week). The individual methods are straightforward. The main complexity is:
- The `updatePreferences` locking mechanism (F# could use `SemaphoreSlim` or `MailboxProcessor`)
- Proper preference array merging (each pref type is a separate element in the array)
- TID generation for saved feeds and muted words
- V1/V2 feed preference migration

### Needs domain types?
Mostly thin wrappers over the generated types. Some convenience types for `BskyPreferences` (parsed
aggregate of all preference items) would be nice.

---

## 3. Profile Editing / upsertProfile (SMALL GAP)

### What the TS SDK provides
```typescript
await agent.upsertProfile(existing => ({
  ...existing,
  displayName: 'New Name',
  description: 'New bio',
}))
```

This method:
1. Fetches the current profile record via `com.atproto.repo.getRecord`
2. Passes it to the user's callback function
3. Validates the result against the profile schema
4. Writes it back via `com.atproto.repo.putRecord`
5. Retries on CAS (compare-and-swap) failures

### What we have
Nothing. Users must manually call `getRecord` + `putRecord` via raw XRPC.

### Can our raw XRPC do this?
**Yes.** All the underlying endpoints are available.

### Priority assessment
- **For bots/automation**: MEDIUM. Bots that update their own profile need this.
- **For client apps**: HIGH. Profile editing is a core feature.

### Complexity estimate
**LOW** (~2-4 hours). Simple read-modify-write with optional CAS retry.

### F# design sketch
```fsharp
let upsertProfile (agent: AtpAgent) (update: Profile option -> Profile) : Task<Result<unit, XrpcError>>
```

---

## 4. List Operations (SMALL GAP)

### What the TS SDK provides
- `muteModList(uri)` / `unmuteModList(uri)` - mute/unmute entire moderation lists
- `blockModList(uri)` / `unblockModList(uri)` - block/unblock via list

These are convenience wrappers around `app.bsky.graph.muteActorList`, `unmuteActorList`, and record
creation/deletion for `app.bsky.graph.listblock`.

### What we have
We have `muteUser`/`unmuteUser` but NOT list-level mute/block operations.

### Can our raw XRPC do this?
**Yes.** The underlying XRPC endpoints and record types are all generated.

### Priority assessment
- **For bots/automation**: LOW. Bots rarely manage moderation lists.
- **For client apps**: MEDIUM. List management is a secondary feature.

### Complexity estimate
**LOW** (~2-4 hours). Thin wrappers over existing endpoints.

---

## 5. RichText Class / Text Manipulation (SMALL GAP)

### What the TS SDK provides
The `RichText` class has:
- `insert(index, text)` - inserts text while adjusting all facet byte indices
- `delete(startIndex, endIndex)` - deletes text while adjusting facet byte indices
- `segments()` generator - yields `RichTextSegment` objects splitting text by facet boundaries
- `clone()` / `copyInto()` - duplication
- `detectFacetsWithoutResolution()` - detection without DID resolution
- `sanitizeRichText()` - cleans excessive whitespace/newlines
- `graphemeLength` / `length` properties

### What we have
- `RichText.detect` - detection only
- `RichText.resolve` - resolution
- `RichText.parse` - combined detect + resolve
- `RichText.graphemeLength` - grapheme counting
- `RichText.byteLength` - UTF-8 byte counting

### What we're missing
- **Text manipulation with facet adjustment** (insert/delete) - useful for text editors
- **Segmentation** - splitting text into rich text segments for rendering
- **Sanitization** - cleaning excessive whitespace

### Can our raw XRPC do this?
**N/A** - This is client-side text processing, not XRPC.

### Priority assessment
- **For bots/automation**: LOW. Bots create text, rarely need to edit or segment it.
- **For client apps**: MEDIUM-HIGH. Essential for rich text editors and rendering.

### Complexity estimate
- Insert/delete with facet adjustment: **MEDIUM** (~1 day). Tricky byte index arithmetic.
- Segmentation: **LOW** (~2-4 hours). Walk through facets and yield segments.
- Sanitization: **LOW** (~1 hour).

---

## 6. Typeahead Search (TINY GAP)

### What the TS SDK provides
- `searchActorsTypeahead(params)` - lightweight search for autocomplete UX

### What we have
- `searchActors` (full search) but not typeahead-specific

### Can our raw XRPC do this?
**Yes.** `app.bsky.actor.searchActorsTypeahead` is available in generated code.

### Priority assessment
- **For bots/automation**: LOW.
- **For client apps**: MEDIUM.

### Complexity estimate
**TRIVIAL** (~30 min). One function wrapping the generated XRPC call.

---

## 7. Suggestions (TINY GAP)

### What the TS SDK provides
- `getSuggestions(params)` - general account suggestions (different from our `getSuggestedFollows`)

### What we have
- `getSuggestedFollows(actor)` - suggestions based on a specific actor

### Difference
Our `getSuggestedFollows` wraps `app.bsky.graph.getSuggestedFollowsByActor` (suggestions based on a
specific user). The TS SDK's `getSuggestions` wraps `app.bsky.actor.getSuggestions` (general
suggestions for the authenticated user).

### Can our raw XRPC do this?
**Yes.** Both endpoints are available.

### Priority assessment
- **For bots/automation**: LOW.
- **For client apps**: LOW.

### Complexity estimate
**TRIVIAL** (~30 min).

---

## 8. OAuth Support (ARCHITECTURAL GAP)

### What the TS SDK provides
The TS SDK has a clean `SessionManager` abstraction that decouples the Agent from auth method:
- `CredentialSession` - username/password auth (what we support)
- `@atproto/oauth-client` / `oauth-client-browser` / `oauth-client-node` - separate packages that
  produce OAuth sessions compatible with the Agent

The Agent class constructor accepts any `SessionManager`, so OAuth is plug-and-play.

### What we have
Only password-based auth (`com.atproto.server.createSession`).

### Can our raw XRPC do this?
**Partially.** OAuth is a complex multi-step flow (PKCE, DPoP, etc.) that requires:
- Client metadata discovery
- Authorization server discovery (via PDS -> DID doc -> auth server)
- Authorization code flow with PKCE
- DPoP-bound token management
- Token refresh with rotation

### Priority assessment
- **For bots/automation**: LOW-MEDIUM. App passwords work fine for bots. OAuth matters for apps acting
  on behalf of users who don't want to share passwords.
- **For client apps**: HIGH long-term (OAuth is the recommended auth flow going forward).

### Complexity estimate
**VERY HIGH** (~2-4 weeks). OAuth for AT Protocol is unusually complex due to DPoP binding, client
metadata, and the decentralized auth server discovery flow. This is a separate project.

### Recommendation
This should be a separate `FSharp.ATProto.OAuth` package when needed, not part of the core SDK.

---

## 9. Age Assurance (TINY GAP)

### What the TS SDK provides
- `getAgeAssuranceRegionConfig(config, geolocation)` - finds matching region rules
- `computeAgeAssuranceRegionAccess(region, data)` - evaluates age-based access rules

This is for apps that need to restrict content based on user age and region.

### Can our raw XRPC do this?
**N/A** - Pure client-side logic.

### Priority assessment
- **For bots/automation**: NONE.
- **For client apps**: LOW (only apps in regulated regions need this).

### Complexity estimate
**LOW** (~4 hours).

---

## 10. Mock/Test Utilities (NICE-TO-HAVE GAP)

### What the TS SDK provides
`mocker.ts` exports factory functions for testing:
- `mock.post()`, `mock.postView()`, `mock.embedRecordView()`
- `mock.profileViewBasic()`, `mock.actorViewerState()`
- `mock.listViewBasic()`
- `mock.replyNotification()`, `mock.followNotification()`
- `mock.label()`

### What we have
Nothing equivalent. Tests use raw test data.

### Priority assessment
- **For SDK consumers**: MEDIUM. Makes testing consumer code easier.
- **For us internally**: We already have thorough tests without this.

### Complexity estimate
**LOW** (~1 day).

---

## 11. Predicate / Validation Functions (TINY GAP)

### What the TS SDK provides
Validation predicates: `isValidProfile`, `isValidAdultContentPref`, `isValidContentLabelPref`, etc.
(~17 validators).

### What we have
Nothing equivalent, but F# static typing provides most of this at compile time.

### Priority assessment
- **For all use cases**: VERY LOW. F# discriminated unions and strong typing make runtime validation
  less necessary than in TypeScript.

### Complexity estimate
**LOW** (~4 hours).

---

## 12. Account Creation / Deletion (TINY GAP)

### What the TS SDK provides
- `createAccount(data)` - creates account and establishes session
- `logout()` - terminates session

### What we have
- `login` - creates session. No `createAccount` or explicit `logout`.

### Can our raw XRPC do this?
**Yes.** `com.atproto.server.createAccount` and `com.atproto.server.deleteSession` are available.

### Priority assessment
- **For bots/automation**: LOW (accounts are created manually).
- **For client apps**: MEDIUM.

### Complexity estimate
**TRIVIAL** (~1 hour).

---

## 13. Session Persistence / Resume (SMALL GAP)

### What the TS SDK provides
- `resumeSession(session)` - resumes a previously saved session without re-authenticating
- Session event callbacks (`create`, `update`, `expired`, `network-error`)
- The `CredentialSession` class has a `persistSession` callback for automatic session storage

### What we have
- `AtpAgent.Session` is a mutable field that can be read/written, but there's no formal
  `resumeSession` method or event system.

### Priority assessment
- **For bots/automation**: LOW-MEDIUM. Long-running bots benefit from session persistence.
- **For client apps**: HIGH.

### Complexity estimate
**LOW** (~4 hours). Add `resumeSession` method and optional session change callback.

---

## 14. Labeler Configuration on Agent (SMALL GAP)

### What the TS SDK provides
- `configureLabelers(dids[])` - sets active labeler DIDs on the agent
- These DIDs are sent as `atproto-accept-labelers` header on requests
- `getLabelers(params)` - fetches labeler service definitions
- `getLabelDefinitions(prefs)` - fetches and interprets label value definitions

### What we have
- `ExtraHeaders` can manually add labeler headers, but no convenience method.

### Priority assessment
- **For bots/automation**: LOW.
- **For client apps**: MEDIUM (needed for moderation system).

### Complexity estimate
**LOW** (~2-4 hours).

---

## 15. "via" Attribution on Likes/Reposts/Follows (TINY GAP)

### What the TS SDK provides
```typescript
agent.like(uri, cid, { via: { uri: quoteUri, cid: quoteCid } })
```
The `via` parameter on `like()`, `repost()`, and `follow()` adds an attribution record indicating
the content was engaged with via a specific embed/quote.

### What we have
No `via` parameter on our engagement functions.

### Can our raw XRPC do this?
**Yes.** The `via` field is part of the like/repost/follow record schemas.

### Priority assessment
- **For bots/automation**: LOW.
- **For client apps**: LOW-MEDIUM.

### Complexity estimate
**TRIVIAL** (~1 hour). Add optional `via` parameter.

---

## 16. Clone / Proxy Pattern (NON-GAP)

### What the TS SDK provides
- `agent.clone()` - creates independent copy
- `agent.withProxy(serviceType, did)` - returns agent configured for proxy

### What we have
- `AtpAgent.withChatProxy` - creates agent copy with chat proxy header
- F# record `{ agent with ... }` syntax provides cloning naturally

### Assessment
**Not a gap.** Our approach is idiomatic F#.

---

## Summary Table

| Feature | TS SDK | F# SDK | Gap Size | Bot Priority | Client Priority | Effort |
|---------|--------|--------|----------|-------------|-----------------|--------|
| **Moderation system** | Full (labels, muted words, blocks, 6 UI contexts) | None | MAJOR | Low | Essential | 2-3 weeks |
| **Preference mutation** | ~25 methods (feeds, words, labels, prefs) | Read only | MEDIUM | Low | High | 1 week |
| **upsertProfile** | CAS-retry read-modify-write | None | SMALL | Medium | High | 2-4 hours |
| **List mute/block** | muteModList, blockModList, etc. | None | SMALL | Low | Medium | 2-4 hours |
| **RichText manipulation** | insert/delete/segments/sanitize | detect/resolve/parse | SMALL | Low | Medium-High | 1-2 days |
| **OAuth** | SessionManager abstraction + separate packages | Password only | ARCH | Low-Medium | High (long-term) | 2-4 weeks |
| **Typeahead search** | searchActorsTypeahead | None | TINY | Low | Medium | 30 min |
| **General suggestions** | getSuggestions | Only getSuggestedFollows | TINY | Low | Low | 30 min |
| **Age assurance** | Region-based age rules | None | TINY | None | Low | 4 hours |
| **Mock/test utilities** | Factory functions for test data | None | NICE | N/A | Medium | 1 day |
| **Validation predicates** | ~17 validators | F# types cover this | TINY | Very Low | Very Low | 4 hours |
| **Account creation** | createAccount, logout | None | TINY | Low | Medium | 1 hour |
| **Session persistence** | resumeSession, events | Manual field access | SMALL | Low-Medium | High | 4 hours |
| **Labeler config** | configureLabelers, getLabelDefs | Manual headers | SMALL | Low | Medium | 2-4 hours |
| **Via attribution** | Optional via on like/repost/follow | None | TINY | Low | Low-Medium | 1 hour |

---

## Recommended Priorities (if we were to close gaps)

### Phase A: Quick wins for broader utility (1-2 days)
1. `upsertProfile` - high value, low effort
2. `searchActorsTypeahead` convenience wrapper
3. `getSuggestions` convenience wrapper
4. `createAccount` / `logout` convenience wrappers
5. `resumeSession` method
6. List mute/block operations
7. `via` parameter on engagement functions

### Phase B: Client-app essentials (1-2 weeks)
1. Preference mutation helpers (the full read-modify-write pattern)
2. RichText segmentation (for rendering)
3. RichText insert/delete (for text editors)
4. Labeler configuration convenience methods
5. Session persistence/event callbacks

### Phase C: Full client parity (2-4 weeks)
1. Moderation system (the big one)
2. Muted word matching engine

### Phase D: Future / separate project
1. OAuth support (separate package)
2. Age assurance (if needed)

---

## What We DON'T Need

Several TS SDK features are not relevant gaps:

- **Deprecated methods**: `setSavedFeeds`, `addSavedFeed`, `removeSavedFeed`, `addPinnedFeed`,
  `removePinnedFeed`, `upsertMutedWords` are all deprecated in favor of newer APIs.
- **Bluesky-app-specific NUX/nudge methods**: `bskyAppQueueNudges`, `bskyAppDismissNudges`,
  `bskyAppSetActiveProgressGuide`, `bskyAppUpsertNux`, `bskyAppRemoveNuxs` are specific to the
  official Bluesky app client. Other apps don't need these.
- **Live event preferences**: Very Bluesky-client-specific.
- **Verification preferences**: Display-specific, client-only.
- **Predicate validators**: F# static typing provides this at compile time.
- **Mocker**: Nice-to-have for consumers but not a gap in functionality.

---

## Conclusion

For a **bot/automation SDK** (our primary use case based on CLAUDE.md's design goals), our SDK is
already quite complete. The main actionable gaps are:

1. **upsertProfile** - bots that manage their own profile need this
2. **Session persistence** - long-running bots benefit from this
3. **List operations** - for moderation bots

For a **full client app SDK**, the moderation system is the single biggest gap. Without it, an app
cannot properly display content with appropriate content warnings, filtering, and labeling. The
preference management system is the second biggest gap for client apps.

The TS SDK has ~2x our convenience surface area (roughly 80 convenience methods vs our ~45), but
the vast majority of that delta is preference manipulation and moderation - areas that are primarily
relevant to full client applications.
