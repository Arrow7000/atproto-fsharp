namespace FSharp.ATProto.FeedGenerator

open FSharp.ATProto.Syntax

/// A single item in a feed skeleton response.
type SkeletonItem = {
    Post: AtUri
    Reason: SkeletonReason option
}

/// Reason for including a post in the skeleton (e.g., repost).
and SkeletonReason =
    | RepostBy of did: Did * indexedAt: string

/// A feed skeleton response.
type SkeletonFeed = {
    Feed: SkeletonItem list
    Cursor: string option
}

/// Query parameters from a getFeedSkeleton request.
type FeedQuery = {
    Feed: AtUri
    Limit: int
    Cursor: string option
}

/// Description of a single feed offered by this generator.
type FeedDescription = {
    Uri: AtUri
    DisplayName: string
    Description: string option
    Avatar: string option
}

/// Response for describeFeedGenerator.
type GeneratorDescription = {
    Did: Did
    Feeds: FeedDescription list
}
