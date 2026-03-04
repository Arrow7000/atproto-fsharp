namespace FSharp.ATProto.FeedGenerator

open System.Threading.Tasks

/// Interface for implementing a custom feed algorithm.
type IFeedAlgorithm =
    /// Returns the feed skeleton for the given query.
    abstract member GetFeedSkeleton : query: FeedQuery -> Task<SkeletonFeed>

/// Helper module for creating feed algorithms from functions.
module FeedAlgorithm =

    /// Create a feed algorithm from an async function.
    let fromFunction (f : FeedQuery -> Task<SkeletonFeed>) : IFeedAlgorithm =
        { new IFeedAlgorithm with
            member _.GetFeedSkeleton query = f query
        }

    /// Create a feed algorithm from a synchronous function.
    let fromSync (f : FeedQuery -> SkeletonFeed) : IFeedAlgorithm =
        { new IFeedAlgorithm with
            member _.GetFeedSkeleton query = Task.FromResult (f query)
        }
