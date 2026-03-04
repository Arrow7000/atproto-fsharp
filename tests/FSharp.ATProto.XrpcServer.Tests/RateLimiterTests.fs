module FSharp.ATProto.XrpcServer.Tests.RateLimiterTests

open System
open Expecto
open FSharp.ATProto.XrpcServer

[<Tests>]
let rateLimiterTests =
    testList
        "RateLimiter"
        [
          test "allows requests within limit" {
              let config = { MaxRequests = 3; Window = TimeSpan.FromSeconds 60.0 }
              let limiter = RateLimiter.create config
              let now = DateTimeOffset.UtcNow

              let r1 = RateLimiter.tryAllow "client1" now limiter
              Expect.isOk r1 "First request should be allowed"

              let r2 = RateLimiter.tryAllow "client1" now limiter
              Expect.isOk r2 "Second request should be allowed"

              let r3 = RateLimiter.tryAllow "client1" now limiter
              Expect.isOk r3 "Third request should be allowed"
          }

          test "blocks requests exceeding limit" {
              let config = { MaxRequests = 2; Window = TimeSpan.FromSeconds 60.0 }
              let limiter = RateLimiter.create config
              let now = DateTimeOffset.UtcNow

              let _ = RateLimiter.tryAllow "client1" now limiter
              let _ = RateLimiter.tryAllow "client1" now limiter
              let r3 = RateLimiter.tryAllow "client1" now limiter

              Expect.isError r3 "Fourth request should be blocked"
          }

          test "returns remaining count" {
              let config = { MaxRequests = 5; Window = TimeSpan.FromSeconds 60.0 }
              let limiter = RateLimiter.create config
              let now = DateTimeOffset.UtcNow

              match RateLimiter.tryAllow "client1" now limiter with
              | Ok remaining -> Expect.equal remaining 4 "Should have 4 remaining after first request"
              | Error _ -> failtest "Should not be rate limited"

              match RateLimiter.tryAllow "client1" now limiter with
              | Ok remaining -> Expect.equal remaining 3 "Should have 3 remaining after second request"
              | Error _ -> failtest "Should not be rate limited"
          }

          test "returns retry-after seconds when blocked" {
              let config = { MaxRequests = 1; Window = TimeSpan.FromSeconds 30.0 }
              let limiter = RateLimiter.create config
              let now = DateTimeOffset.UtcNow

              let _ = RateLimiter.tryAllow "client1" now limiter

              match RateLimiter.tryAllow "client1" (now.AddSeconds 5.0) limiter with
              | Error retryAfter ->
                  // The earliest request was at 'now', window is 30s, so retry after ~25s
                  Expect.isTrue (retryAfter > 20.0 && retryAfter <= 30.0) (sprintf "Retry-after should be around 25s, got %.1f" retryAfter)
              | Ok _ -> failtest "Should be rate limited"
          }

          test "allows requests after window expires" {
              let config = { MaxRequests = 1; Window = TimeSpan.FromSeconds 10.0 }
              let limiter = RateLimiter.create config
              let now = DateTimeOffset.UtcNow

              let _ = RateLimiter.tryAllow "client1" now limiter

              // Request within the window -- should be blocked
              let blocked = RateLimiter.tryAllow "client1" (now.AddSeconds 5.0) limiter
              Expect.isError blocked "Should be blocked within window"

              // Request after the window -- should be allowed
              let allowed = RateLimiter.tryAllow "client1" (now.AddSeconds 11.0) limiter
              Expect.isOk allowed "Should be allowed after window expires"
          }

          test "tracks clients independently" {
              let config = { MaxRequests = 1; Window = TimeSpan.FromSeconds 60.0 }
              let limiter = RateLimiter.create config
              let now = DateTimeOffset.UtcNow

              let _ = RateLimiter.tryAllow "client1" now limiter

              // client1 is now blocked
              let blocked = RateLimiter.tryAllow "client1" now limiter
              Expect.isError blocked "client1 should be blocked"

              // client2 should still be allowed
              let allowed = RateLimiter.tryAllow "client2" now limiter
              Expect.isOk allowed "client2 should be allowed"
          }

          test "sliding window prunes old timestamps" {
              let config = { MaxRequests = 2; Window = TimeSpan.FromSeconds 10.0 }
              let limiter = RateLimiter.create config
              let now = DateTimeOffset.UtcNow

              // Fill up the limit
              let _ = RateLimiter.tryAllow "client1" now limiter
              let _ = RateLimiter.tryAllow "client1" (now.AddSeconds 1.0) limiter

              // Blocked at t=2
              let blocked = RateLimiter.tryAllow "client1" (now.AddSeconds 2.0) limiter
              Expect.isError blocked "Should be blocked"

              // At t=11, the first request (t=0) has expired, but the second (t=1) hasn't
              // So we have 1 request in the window, limit is 2 -- should allow
              let allowed = RateLimiter.tryAllow "client1" (now.AddSeconds 11.0) limiter
              Expect.isOk allowed "Should be allowed after oldest request expires"
          }

          test "concurrent access is safe" {
              let config = { MaxRequests = 100; Window = TimeSpan.FromSeconds 60.0 }
              let limiter = RateLimiter.create config
              let now = DateTimeOffset.UtcNow

              // Simulate concurrent requests
              let results =
                  [| 1..100 |]
                  |> Array.Parallel.map (fun i ->
                      RateLimiter.tryAllow "client1" (now.AddMilliseconds (float i)) limiter)

              let successes = results |> Array.filter Result.isOk |> Array.length
              Expect.equal successes 100 "All 100 requests should succeed"

              // Next request should be blocked
              let blocked = RateLimiter.tryAllow "client1" (now.AddMilliseconds 101.0) limiter
              Expect.isError blocked "101st request should be blocked"
          }
        ]
