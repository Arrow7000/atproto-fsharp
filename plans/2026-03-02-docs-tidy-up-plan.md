# Docs Tidy-Up Plan (Post Convenience Layer Audit)

The convenience layer audit added 27 new functions and 13 domain types. Every docs guide except `identity.md` and `media.md` references outdated return types or missing convenience functions. The working tree already has partial rewrites of `README.md` and several guides, but those rewrites predate the domain type changes and are themselves outdated.

## Strategy

**Core principle:** Docs should show the convenience layer first, with raw XRPC as a "Power Users" escape hatch. Every code example accessing `.Feed`, `.Followers`, `.Notifications`, `.Convos`, `.Messages`, or `Option.defaultValue` on profile fields is wrong -- the domain types have `.Items`, non-optional fields, and simpler structures.

## Task 1: Update README.md

Current issues:
- Chat example uses `convo.Convo.Id` (should be `convo.Id` -- returns `ConvoSummary` now)
- Pagination comment says `Result<GetTimeline.Output, ...>` (should be `Result<Page<FeedItem>, ...>`)
- "Full XRPC access" example uses `GetAuthorFeed.query` which now has a convenience wrapper
- Test count is stale (should be 1,696)

Changes needed:
- Fix chat example to use `ConvoSummary` fields directly
- Fix pagination type annotation
- Replace raw XRPC example with a different endpoint (one that doesn't have a convenience wrapper)
- Add a "What's included" bullet for domain types + search/bookmarks/mute
- Update test count

## Task 2: Update docs/index.md

Current issues:
- Feature list doesn't mention domain types, search, bookmarks, or mute/report
- Chat bullet undersells the API (now has accept/leave/reactions/getConvo)
- Mentions a non-existent `paginateFollows` paginator

Changes needed:
- Add domain types to feature list ("`Profile`, `TimelinePost`, `FeedItem`, `Page<'T>`, etc.")
- Add search, bookmarks, mute/report to feature list
- Update Chat description to mention reactions + accept/leave
- Fix paginator list (timeline, followers, notifications)

## Task 3: Update docs/quickstart.md

Current issues:
- `getTimeline` example accesses `timeline.Feed` (should be `timeline.Items`)
- PostRef construction from `FeedItem` needs path update
- Extension property explanation is misleading (domain types have fields directly)

Changes needed:
- Replace `timeline.Feed` with `timeline.Items`
- Update PostRef construction to go through `FeedItem.Post`
- Remove/simplify extension property discussion (mention they exist on raw `PostView` but domain types don't need them)

## Task 4: Update docs/guides/posts.md

Current issues:
- "Reading Posts" uses raw `AppBskyFeed.GetPosts.query` → now has `Bluesky.getPosts`
- Thread section uses raw `ThreadViewPostParentUnion` patterns → now has `ThreadNode`/`ThreadPost`
- "Search Posts" uses raw `SearchPosts.query` → now has `Bluesky.searchPosts`
- PostView extension table is confusing alongside domain types

Changes needed:
- Replace raw `GetPosts.query` with `Bluesky.getPosts` (returns `TimelinePost list`)
- Add `Bluesky.getQuotes` section
- Update thread examples to use `ThreadNode.Post`, `ThreadPost.Replies`, etc.
- Update search to use `Bluesky.searchPosts` (returns `Page<TimelinePost>`)
- Add note that `PostView` extensions exist for the raw layer, but domain types expose fields directly

## Task 5: Update docs/guides/social.md

Current issues:
- `getFollows`/`getFollowers` examples access `output.Follows`, `output.Followers`, `output.Subject` → should be `page.Items`, `page.Cursor`
- "Who Liked" uses raw `GetLikes.query` → now has `Bluesky.getLikes`
- "Who Reposted" uses raw `GetRepostedBy.query` → now has `Bluesky.getRepostedBy`
- Missing: muteUser/unmuteUser, muteThread/unmuteThread, reportContent

Changes needed:
- Fix getFollows/getFollowers examples to use `Page<ProfileSummary>`
- Replace raw getLikes/getRepostedBy with convenience functions
- Add "Muting" section: muteUser/unmuteUser, muteThread/unmuteThread
- Add "Reporting Content" section: reportContent with ReportSubject DU
- Add `getSuggestedFollows` mention

## Task 6: Update docs/guides/feeds.md

Current issues:
- `getTimeline` accesses `output.Feed` → should be `page.Items`
- "Author Feed" uses raw `GetAuthorFeed.query` → now has `Bluesky.getAuthorFeed`
- "Understanding Feed Items" references raw union types → should use domain types
- Pagination examples reference raw output types

Changes needed:
- Fix getTimeline to use `Page<FeedItem>` (`.Items`, not `.Feed`)
- Replace raw GetAuthorFeed with `Bluesky.getAuthorFeed`
- Add `Bluesky.getActorLikes` section
- Rewrite "Understanding Feed Items" for `FeedItem`/`TimelinePost`/`FeedReason` domain types
- Fix pagination examples to use domain return types
- Add bookmarks section: `addBookmark`, `removeBookmark`, `getBookmarks`

## Task 7: Update docs/guides/chat.md

Current issues:
- `getConvoForMembers` example uses `result.Convo` → returns `ConvoSummary` directly
- `listConvos` accesses `cs.Convos` → should be `cs.Items`
- `getMessages` accesses `ms.Messages` with raw union matching → should be `ms.Items` with `ChatMessage` DU
- `sendMessage` accesses `msg.Text`/`msg.Id` → returns `ChatMessage` DU
- Reactions section uses raw XRPC → now has `Chat.addReaction`/`removeReaction`
- Missing: acceptConvo, leaveConvo, getConvo

Changes needed:
- Fix all return type access patterns for domain types
- Replace raw reaction calls with convenience functions
- Add "Managing Conversations" section: acceptConvo, leaveConvo, getConvo
- Fix `sendMessage` example to pattern-match `ChatMessage.Message`

## Task 8: Update docs/guides/pagination.md

Current issues:
- All three paginator return types are documented as raw generated types
- Code examples access `.Feed`, `.Followers`, `.Notifications` → should be `.Items`

Changes needed:
- Update all type annotations to `Page<FeedItem>`, `Page<ProfileSummary>`, `Page<Notification>`
- Replace `.Feed`/`.Followers`/`.Notifications` with `.Items`
- Update single-page query example to mention convenience functions

## Task 9: Update docs/guides/profiles.md

Current issues:
- `getProfile` example uses `Option.defaultValue` on fields that are no longer optional
- Says both approaches return `GetProfile.Output` → convenience returns `Profile` domain type
- "Multiple Profiles" uses raw `GetProfiles.query` → now has `Bluesky.getProfiles`
- "Searching" uses raw `SearchActors.query` → now has `Bluesky.searchActors`
- Viewer state section accesses raw `profile.Viewer` → `Profile` has `IsFollowing`, `IsFollowedBy`, etc.

Changes needed:
- Rewrite getProfile examples for `Profile` domain type (non-optional fields)
- Replace raw GetProfiles with `Bluesky.getProfiles`
- Replace raw SearchActors with `Bluesky.searchActors`
- Rewrite viewer state for boolean fields
- Add `getSuggestedFollows` section
- Add note about `ProfileSummary` vs `Profile` (summary is used in feeds/notifications, full profile from getProfile)

## Task 10: Update docs/guides/rich-text.md

Current issues:
- Chat rich text section says you need to manually call `RichText.parse` for DMs → `Chat.sendMessage` now auto-detects

Changes needed:
- Update chat section to note that `Chat.sendMessage` handles rich text automatically
- Keep the manual approach as a "Custom Facets" example

## Task 11: Update docs/guides/notifications section

Currently notifications are mentioned in pagination but have no dedicated content.

Changes needed:
- Add notifications to social.md or create a section in an existing guide
- Document `getNotifications`, `getUnreadNotificationCount`, `markNotificationsSeen`
- Show the `Notification` and `NotificationKind` domain types

## Guides that need NO changes

- `docs/guides/identity.md` -- correct and current
- `docs/guides/media.md` -- correct and current

## Execution order

Tasks 1-3 first (README, index, quickstart -- the entry points).
Then tasks 4-10 in any order (individual guides).
Task 11 last (new content).
