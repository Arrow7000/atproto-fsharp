namespace FSharp.ATProto.XrpcServer

open System
open System.Collections.Concurrent

/// In-memory sliding window rate limiter.
/// Tracks requests per key (typically client IP or DID) within a configurable window.
module RateLimiter =

    /// A rate limiter instance that can be shared across requests.
    /// Internal implementation uses a ConcurrentDictionary of timestamp lists.
    type RateLimiterState = internal {
        Config: RateLimitConfig
        Keys: ConcurrentDictionary<string, DateTimeOffset list ref>
    }

    /// Create a new rate limiter for the given config.
    let create (config : RateLimitConfig) : RateLimiterState = {
        Config = config
        Keys = ConcurrentDictionary<string, DateTimeOffset list ref> ()
    }

    /// Try to allow a request for the given key.
    /// Returns Ok with the number of remaining requests, or Error with the seconds until the window resets.
    let tryAllow (key : string) (now : DateTimeOffset) (state : RateLimiterState) : Result<int, float> =
        let windowStart = now - state.Config.Window

        let timestamps =
            state.Keys.GetOrAdd (key, fun _ -> ref [])

        // Prune timestamps outside the window and count
        lock timestamps (fun () ->
            let recent =
                timestamps.Value
                |> List.filter (fun ts -> ts > windowStart)

            if recent.Length >= state.Config.MaxRequests then
                // Find the earliest timestamp in the window -- that's when the oldest request expires
                let earliest = recent |> List.min
                let resetIn = (earliest + state.Config.Window - now).TotalSeconds
                Error (max 0.0 resetIn)
            else
                let updated = now :: recent
                timestamps.Value <- updated
                Ok (state.Config.MaxRequests - updated.Length))
