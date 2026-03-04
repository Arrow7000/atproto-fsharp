namespace FSharp.ATProto.OAuthServer

open System
open System.Collections.Concurrent
open System.Threading.Tasks

/// In-memory token store using ConcurrentDictionary. Suitable for development and testing.
type InMemoryTokenStore() =
    let tokens = ConcurrentDictionary<string, TokenData>()

    interface ITokenStore with
        member _.CreateToken(tokenId, data) =
            tokens.[tokenId] <- data
            task { return () }

        member _.ReadToken(tokenId) =
            match tokens.TryGetValue(tokenId) with
            | true, data -> Task.FromResult(Some data)
            | false, _ -> Task.FromResult(None)

        member _.DeleteToken(tokenId) =
            tokens.TryRemove(tokenId) |> ignore
            task { return () }

        member _.RotateToken(tokenId, newId, newRefreshToken, newData) =
            lock tokens (fun () ->
                tokens.TryRemove(tokenId) |> ignore
                tokens.[newId] <- { newData with RefreshToken = newRefreshToken })

            task { return () }

        member _.FindByRefreshToken(refreshToken) =
            tokens
            |> Seq.tryFind (fun kvp -> kvp.Value.RefreshToken = refreshToken)
            |> Option.map (fun kvp -> (kvp.Key, kvp.Value))
            |> Task.FromResult

/// In-memory request store using ConcurrentDictionary. Suitable for development and testing.
type InMemoryRequestStore() =
    let requests = ConcurrentDictionary<string, RequestData>()

    interface IRequestStore with
        member _.CreateRequest(requestId, data) =
            requests.[requestId] <- data
            task { return () }

        member _.ReadRequest(requestId) =
            match requests.TryGetValue(requestId) with
            | true, data -> Task.FromResult(Some data)
            | false, _ -> Task.FromResult(None)

        member _.ConsumeCode(code) =
            lock requests (fun () ->
                let found =
                    requests
                    |> Seq.tryFind (fun kvp -> kvp.Value.Code = Some code)

                match found with
                | Some kvp ->
                    requests.TryRemove(kvp.Key) |> ignore
                    Some kvp.Value
                | None -> None)
            |> Task.FromResult

        member _.DeleteRequest(requestId) =
            requests.TryRemove(requestId) |> ignore
            task { return () }

/// In-memory replay detection store using ConcurrentDictionary. Suitable for development and testing.
type InMemoryReplayStore() =
    let seen = ConcurrentDictionary<string, DateTimeOffset>()

    interface IReplayStore with
        member _.IsUnique(ns, key, expiresAt) =
            let fullKey = sprintf "%s:%s" ns key
            // Clean up expired entries periodically
            let now = DateTimeOffset.UtcNow

            for kvp in seen do
                if kvp.Value < now then
                    seen.TryRemove(kvp.Key) |> ignore

            let added = seen.TryAdd(fullKey, expiresAt)
            Task.FromResult(added)

/// In-memory account store using ConcurrentDictionary. Suitable for development and testing.
type InMemoryAccountStore() =
    let accounts = ConcurrentDictionary<string, AccountInfo * string>()

    /// Add an account for testing. Password is stored as-is (no hashing).
    member _.AddAccount(identifier: string, password: string, info: AccountInfo) =
        accounts.[identifier] <- (info, password)

    interface IAccountStore with
        member _.Authenticate(credentials) =
            match accounts.TryGetValue(credentials.Identifier) with
            | true, (info, password) ->
                if password = credentials.Password then
                    Task.FromResult(Ok info)
                else
                    Task.FromResult(Error "Invalid password")
            | false, _ -> Task.FromResult(Error "Account not found")

        member _.GetAccount(sub) =
            accounts.Values
            |> Seq.tryFind (fun (info, _) -> info.Sub = sub)
            |> Option.map fst
            |> Task.FromResult
