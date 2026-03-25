namespace FSharp.ATProto.Pds

open System
open System.Collections.Concurrent
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open FSharp.ATProto.Syntax
open FSharp.ATProto.Crypto

module internal Json =

    let options =
        let opts = JsonSerializerOptions (PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        opts.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
        opts

    let error (status : int) (error : string) (message : string) : IResult =
        Results.Json (
            dict [ "error", box error; "message", box message ],
            options,
            statusCode = status
        )

    let tryReadJsonBody (ctx : HttpContext) : Task<Result<JsonElement, IResult>> =
        task {
            try
                let! doc = JsonDocument.ParseAsync ctx.Request.Body
                return Ok doc.RootElement
            with ex ->
                return Error (error 400 "InvalidRequest" (sprintf "Invalid JSON body: %s" ex.Message))
        }

    let getString (prop : string) (el : JsonElement) : string option =
        match el.TryGetProperty prop with
        | true, v when v.ValueKind = JsonValueKind.String -> Some (v.GetString ())
        | _ -> None

    let getStringOrEmpty (prop : string) (el : JsonElement) : string =
        getString prop el |> Option.defaultValue ""

    let getElement (prop : string) (el : JsonElement) : JsonElement option =
        match el.TryGetProperty prop with
        | true, v -> Some v
        | _ -> None

module internal DidGeneration =

    let generate () : Did =
        let bytes = RandomNumberGenerator.GetBytes 24
        let encoded = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        let plcId = encoded.ToLowerInvariant().Substring (0, 24)
        Did.parse (sprintf "did:plc:%s" plcId) |> Result.defaultWith failwith

module internal Endpoints =

    let describeServer (state : PdsState) (_ctx : HttpContext) : Task<IResult> =
        task {
            let response =
                dict [
                    "did", box (Did.value state.ServiceDid)
                    "availableUserDomains", box [| state.Config.Hostname |]
                    "inviteCodeRequired", box state.Config.InviteRequired
                ]

            return Results.Json (response, Json.options)
        }

    let private validateInvite (state : PdsState) (code : string) : IResult option =
        if not state.Config.InviteRequired then
            None
        else
            match state.Config.InviteCode with
            | Some expected when code = expected -> None
            | Some _ -> Some (Json.error 400 "InvalidInviteCode" "Invalid invite code")
            | None -> Some (Json.error 400 "InvalidInviteCode" "Invite codes not configured")

    let createAccount (state : PdsState) (ctx : HttpContext) : Task<IResult> =
        task {
            let! bodyResult = Json.tryReadJsonBody ctx

            match bodyResult with
            | Error err -> return err
            | Ok body ->
                let handleStr = Json.getStringOrEmpty "handle" body
                let password = Json.getStringOrEmpty "password" body
                let inviteCode = Json.getStringOrEmpty "inviteCode" body

                match validateInvite state inviteCode with
                | Some err -> return err
                | None ->

                match Handle.parse handleStr with
                | Error msg ->
                    return Json.error 400 "InvalidHandle" (sprintf "Invalid handle: %s" msg)
                | Ok handle ->
                    let normalizedHandle = Handle.value handle

                    if state.Accounts.ContainsKey normalizedHandle then
                        return Json.error 400 "HandleNotAvailable" "Handle already taken"
                    else
                        let did = DidGeneration.generate ()
                        let passwordHash, salt = Passwords.hash password
                        let signingKey = Keys.generate Algorithm.P256

                        let account =
                            { Did = did
                              Handle = handle
                              PasswordHash = passwordHash
                              PasswordSalt = salt
                              SigningKey = signingKey
                              CreatedAt = DateTimeOffset.UtcNow }

                        state.Accounts.TryAdd (normalizedHandle, account) |> ignore
                        state.AccountsByDid.TryAdd (Did.value did, account) |> ignore

                        let accessToken = Tokens.generate ()
                        let refreshToken = Tokens.generate ()
                        let now = DateTimeOffset.UtcNow

                        let session =
                            { AccessToken = accessToken
                              RefreshToken = refreshToken
                              Did = did
                              Handle = handle
                              AccessExpiresAt = now + state.Config.AccessTokenLifetime
                              RefreshExpiresAt = now + state.Config.RefreshTokenLifetime }

                        state.Sessions.TryAdd (accessToken, session) |> ignore
                        state.RefreshIndex.TryAdd (refreshToken, accessToken) |> ignore

                        let response =
                            dict [
                                "did", box (Did.value did)
                                "handle", box normalizedHandle
                                "accessJwt", box accessToken
                                "refreshJwt", box refreshToken
                            ]

                        return Results.Json (response, Json.options)
        }

    let createSession (state : PdsState) (ctx : HttpContext) : Task<IResult> =
        task {
            let! bodyResult = Json.tryReadJsonBody ctx

            match bodyResult with
            | Error err -> return err
            | Ok body ->
                let identifier = Json.getStringOrEmpty "identifier" body
                let password = Json.getStringOrEmpty "password" body

                let account =
                    match state.Accounts.TryGetValue identifier with
                    | true, acct -> Some acct
                    | false, _ ->
                        match state.AccountsByDid.TryGetValue identifier with
                        | true, acct -> Some acct
                        | false, _ -> None

                match account with
                | None ->
                    return Json.error 401 "AuthenticationRequired" "Invalid identifier or password"
                | Some acct ->
                    if not (Passwords.verify password acct.PasswordHash acct.PasswordSalt) then
                        return Json.error 401 "AuthenticationRequired" "Invalid identifier or password"
                    else
                        let accessToken = Tokens.generate ()
                        let refreshToken = Tokens.generate ()
                        let now = DateTimeOffset.UtcNow

                        let session =
                            { AccessToken = accessToken
                              RefreshToken = refreshToken
                              Did = acct.Did
                              Handle = acct.Handle
                              AccessExpiresAt = now + state.Config.AccessTokenLifetime
                              RefreshExpiresAt = now + state.Config.RefreshTokenLifetime }

                        state.Sessions.TryAdd (accessToken, session) |> ignore
                        state.RefreshIndex.TryAdd (refreshToken, accessToken) |> ignore

                        let response =
                            dict [
                                "did", box (Did.value acct.Did)
                                "handle", box (Handle.value acct.Handle)
                                "accessJwt", box accessToken
                                "refreshJwt", box refreshToken
                            ]

                        return Results.Json (response, Json.options)
        }

    let private extractBearer (ctx : HttpContext) : string option =
        match ctx.Request.Headers.Authorization.ToString () with
        | auth when auth.StartsWith ("Bearer ", StringComparison.OrdinalIgnoreCase) ->
            Some (auth.Substring 7)
        | _ -> None

    let private authenticate (state : PdsState) (ctx : HttpContext) : SessionInfo option =
        match extractBearer ctx with
        | None -> None
        | Some token ->
            match state.Sessions.TryGetValue token with
            | true, session when session.AccessExpiresAt > DateTimeOffset.UtcNow -> Some session
            | true, _ ->
                None
            | false, _ -> None

    let private requireAuth (state : PdsState) (ctx : HttpContext) : Result<SessionInfo, IResult> =
        match extractBearer ctx with
        | None ->
            Error (Json.error 401 "AuthenticationRequired" "Authentication required")
        | Some token ->
            match state.Sessions.TryGetValue token with
            | true, session when session.AccessExpiresAt > DateTimeOffset.UtcNow ->
                Ok session
            | true, _ ->
                Error (Json.error 400 "ExpiredToken" "Token has expired")
            | false, _ ->
                Error (Json.error 401 "AuthenticationRequired" "Invalid token")

    let refreshSession (state : PdsState) (ctx : HttpContext) : Task<IResult> =
        task {
            match extractBearer ctx with
            | None ->
                return Json.error 401 "AuthenticationRequired" "Authentication required"
            | Some refreshToken ->
                match state.RefreshIndex.TryGetValue refreshToken with
                | false, _ ->
                    return Json.error 401 "AuthenticationRequired" "Invalid refresh token"
                | true, oldAccessToken ->
                    match state.Sessions.TryGetValue oldAccessToken with
                    | false, _ ->
                        return Json.error 401 "AuthenticationRequired" "Session not found"
                    | true, oldSession ->
                        if oldSession.RefreshExpiresAt <= DateTimeOffset.UtcNow then
                            return Json.error 400 "ExpiredToken" "Refresh token has expired"
                        else
                            state.Sessions.TryRemove oldAccessToken |> ignore
                            state.RefreshIndex.TryRemove refreshToken |> ignore

                            let newAccess = Tokens.generate ()
                            let newRefresh = Tokens.generate ()
                            let now = DateTimeOffset.UtcNow

                            let newSession =
                                { AccessToken = newAccess
                                  RefreshToken = newRefresh
                                  Did = oldSession.Did
                                  Handle = oldSession.Handle
                                  AccessExpiresAt = now + state.Config.AccessTokenLifetime
                                  RefreshExpiresAt = now + state.Config.RefreshTokenLifetime }

                            state.Sessions.TryAdd (newAccess, newSession) |> ignore
                            state.RefreshIndex.TryAdd (newRefresh, newAccess) |> ignore

                            let response =
                                dict [
                                    "did", box (Did.value newSession.Did)
                                    "handle", box (Handle.value newSession.Handle)
                                    "accessJwt", box newAccess
                                    "refreshJwt", box newRefresh
                                ]

                            return Results.Json (response, Json.options)
        }

    let deleteSession (state : PdsState) (ctx : HttpContext) : Task<IResult> =
        task {
            match extractBearer ctx with
            | Some token ->
                match state.Sessions.TryRemove token with
                | true, session -> state.RefreshIndex.TryRemove session.RefreshToken |> ignore
                | false, _ -> ()
            | None -> ()

            return Results.Ok ()
        }

    let getSession (state : PdsState) (ctx : HttpContext) : Task<IResult> =
        task {
            match requireAuth state ctx with
            | Error err -> return err
            | Ok session ->
                let response =
                    dict [
                        "did", box (Did.value session.Did)
                        "handle", box (Handle.value session.Handle)
                    ]

                return Results.Json (response, Json.options)
        }

    let resolveHandle (state : PdsState) (ctx : HttpContext) : Task<IResult> =
        task {
            let handleStr = ctx.Request.Query.["handle"].ToString ()

            match state.Accounts.TryGetValue handleStr with
            | true, acct ->
                let response = dict [ "did", box (Did.value acct.Did) ]
                return Results.Json (response, Json.options)
            | false, _ ->
                return Json.error 400 "UnableToResolveHandle" (sprintf "Unable to resolve handle: %s" handleStr)
        }

    let createRecord (state : PdsState) (ctx : HttpContext) : Task<IResult> =
        task {
            match requireAuth state ctx with
            | Error err -> return err
            | Ok session ->
                let! bodyResult = Json.tryReadJsonBody ctx

                match bodyResult with
                | Error err -> return err
                | Ok body ->
                    let repo = Json.getStringOrEmpty "repo" body
                    let collection = Json.getStringOrEmpty "collection" body
                    let rkeyInput = Json.getStringOrEmpty "rkey" body
                    let recordEl = Json.getElement "record" body

                    if repo <> Did.value session.Did then
                        return Json.error 400 "InvalidRequest" "Can only write to your own repo"
                    else
                        match recordEl with
                        | None ->
                            return Json.error 400 "InvalidRequest" "Missing 'record' field"
                        | Some record ->
                            let rkey =
                                if String.IsNullOrEmpty rkeyInput then RecordKeys.generate ()
                                else rkeyInput

                            let uriStr =
                                sprintf "at://%s/%s/%s" (Did.value session.Did) collection rkey

                            match AtUri.parse uriStr with
                            | Error msg ->
                                return Json.error 400 "InvalidRequest" (sprintf "Invalid record URI: %s" msg)
                            | Ok uri ->
                                let cid = RecordKeys.computeCid record
                                let key = RecordKeys.recordKey session.Did collection rkey

                                let storedRecord =
                                    { Uri = uri
                                      Cid = cid
                                      Collection = collection
                                      Rkey = rkey
                                      Value = record.Clone () }

                                state.Records.[key] <- storedRecord

                                let response =
                                    dict [
                                        "uri", box (AtUri.value uri)
                                        "cid", box cid
                                    ]

                                return Results.Json (response, Json.options)
        }

    let getRecord (state : PdsState) (ctx : HttpContext) : Task<IResult> =
        task {
            let repo = ctx.Request.Query.["repo"].ToString ()
            let collection = ctx.Request.Query.["collection"].ToString ()
            let rkey = ctx.Request.Query.["rkey"].ToString ()

            match Did.parse repo with
            | Error _ ->
                match state.Accounts.TryGetValue repo with
                | true, acct ->
                    let key = RecordKeys.recordKey acct.Did collection rkey

                    match state.Records.TryGetValue key with
                    | true, record ->
                        let response =
                            dict [
                                "uri", box (AtUri.value record.Uri)
                                "cid", box record.Cid
                                "value", box record.Value
                            ]

                        return Results.Json (response, Json.options)
                    | false, _ ->
                        return Json.error 400 "RecordNotFound" "Record not found"
                | false, _ ->
                    return Json.error 400 "RepoNotFound" "Repository not found"
            | Ok did ->
                let key = RecordKeys.recordKey did collection rkey

                match state.Records.TryGetValue key with
                | true, record ->
                    let response =
                        dict [
                            "uri", box (AtUri.value record.Uri)
                            "cid", box record.Cid
                            "value", box record.Value
                        ]

                    return Results.Json (response, Json.options)
                | false, _ ->
                    return Json.error 400 "RecordNotFound" "Record not found"
        }

    let deleteRecord (state : PdsState) (ctx : HttpContext) : Task<IResult> =
        task {
            match requireAuth state ctx with
            | Error err -> return err
            | Ok session ->
                let! bodyResult = Json.tryReadJsonBody ctx

                match bodyResult with
                | Error err -> return err
                | Ok body ->
                    let repo = Json.getStringOrEmpty "repo" body
                    let collection = Json.getStringOrEmpty "collection" body
                    let rkey = Json.getStringOrEmpty "rkey" body

                    if repo <> Did.value session.Did then
                        return Json.error 400 "InvalidRequest" "Can only delete from your own repo"
                    else
                        let key = RecordKeys.recordKey session.Did collection rkey
                        state.Records.TryRemove key |> ignore
                        return Results.Ok ()
        }

    let listRecords (state : PdsState) (ctx : HttpContext) : Task<IResult> =
        task {
            let repo = ctx.Request.Query.["repo"].ToString ()
            let collection = ctx.Request.Query.["collection"].ToString ()

            let limitStr = ctx.Request.Query.["limit"].ToString ()

            let limit =
                match Int32.TryParse limitStr with
                | true, v when v >= 1 && v <= 100 -> v
                | _ -> 50

            let did =
                match Did.parse repo with
                | Ok d -> Some d
                | Error _ ->
                    match state.Accounts.TryGetValue repo with
                    | true, acct -> Some acct.Did
                    | false, _ -> None

            match did with
            | None ->
                return Json.error 400 "RepoNotFound" "Repository not found"
            | Some d ->
                let prefix = sprintf "%s/%s/" (Did.value d) collection

                let records =
                    state.Records.Values
                    |> Seq.filter (fun r ->
                        let key = RecordKeys.recordKey d r.Collection r.Rkey
                        key.StartsWith (prefix, StringComparison.Ordinal)
                        && r.Collection = collection)
                    |> Seq.truncate limit
                    |> Seq.map (fun r ->
                        dict [
                            "uri", box (AtUri.value r.Uri)
                            "cid", box r.Cid
                            "value", box r.Value
                        ]
                        :> obj)
                    |> Seq.toArray

                let response = dict [ "records", box records ]
                return Results.Json (response, Json.options)
        }

    let atprotoDid (state : PdsState) (_ctx : HttpContext) : Task<IResult> =
        task { return Results.Text (Did.value state.ServiceDid) }

/// Personal Data Server for the AT Protocol.
module Pds =

    /// Create a default PDS builder for the given hostname.
    let defaults (hostname : string) : PdsBuilder = PdsBuilder.defaults hostname

    /// Set the port the PDS listens on (default: 2583).
    let withPort (port : int) (builder : PdsBuilder) : PdsBuilder = { builder with Port = port }

    /// Set a pre-generated signing key.
    let withSigningKey (key : KeyPair) (builder : PdsBuilder) : PdsBuilder =
        { builder with SigningKey = Some key }

    /// Set the admin password.
    let withAdminPassword (password : string) (builder : PdsBuilder) : PdsBuilder =
        { builder with AdminPassword = Some password }

    /// Require invite codes for account creation.
    let withInviteCode (code : string) (builder : PdsBuilder) : PdsBuilder =
        { builder with InviteRequired = true; InviteCode = Some code }

    /// Set the access token lifetime (default: 2 hours).
    let withAccessTokenLifetime (lifetime : TimeSpan) (builder : PdsBuilder) : PdsBuilder =
        { builder with AccessTokenLifetime = lifetime }

    /// Set the refresh token lifetime (default: 90 days).
    let withRefreshTokenLifetime (lifetime : TimeSpan) (builder : PdsBuilder) : PdsBuilder =
        { builder with RefreshTokenLifetime = lifetime }

    let internal createState (builder : PdsBuilder) : PdsState =
        let signingKey =
            match builder.SigningKey with
            | Some key -> key
            | None -> Keys.generate Algorithm.P256

        let serviceDid = DidGeneration.generate ()

        { Accounts = ConcurrentDictionary<string, PdsAccount> ()
          AccountsByDid = ConcurrentDictionary<string, PdsAccount> ()
          Sessions = ConcurrentDictionary<string, SessionInfo> ()
          RefreshIndex = ConcurrentDictionary<string, string> ()
          Records = ConcurrentDictionary<string, StoredRecord> ()
          Config = builder
          SigningKey = signingKey
          ServiceDid = serviceDid }

    /// Map all PDS XRPC endpoints onto an existing WebApplication.
    let mapEndpoints (builder : PdsBuilder) (app : WebApplication) : WebApplication =
        let state = createState builder

        app.MapGet (
            "/_health",
            Func<IResult> (fun () -> Results.Json ({| version = "1.0.0" |}, Json.options))
        )
        |> ignore

        app.MapGet (
            "/.well-known/atproto-did",
            Func<HttpContext, Task<IResult>> (fun ctx -> Endpoints.atprotoDid state ctx)
        )
        |> ignore

        app.MapGet (
            "/xrpc/com.atproto.server.describeServer",
            Func<HttpContext, Task<IResult>> (fun ctx -> Endpoints.describeServer state ctx)
        )
        |> ignore

        app.MapPost (
            "/xrpc/com.atproto.server.createAccount",
            Func<HttpContext, Task<IResult>> (fun ctx -> Endpoints.createAccount state ctx)
        )
        |> ignore

        app.MapPost (
            "/xrpc/com.atproto.server.createSession",
            Func<HttpContext, Task<IResult>> (fun ctx -> Endpoints.createSession state ctx)
        )
        |> ignore

        app.MapPost (
            "/xrpc/com.atproto.server.refreshSession",
            Func<HttpContext, Task<IResult>> (fun ctx -> Endpoints.refreshSession state ctx)
        )
        |> ignore

        app.MapPost (
            "/xrpc/com.atproto.server.deleteSession",
            Func<HttpContext, Task<IResult>> (fun ctx -> Endpoints.deleteSession state ctx)
        )
        |> ignore

        app.MapGet (
            "/xrpc/com.atproto.server.getSession",
            Func<HttpContext, Task<IResult>> (fun ctx -> Endpoints.getSession state ctx)
        )
        |> ignore

        app.MapGet (
            "/xrpc/com.atproto.identity.resolveHandle",
            Func<HttpContext, Task<IResult>> (fun ctx -> Endpoints.resolveHandle state ctx)
        )
        |> ignore

        app.MapPost (
            "/xrpc/com.atproto.repo.createRecord",
            Func<HttpContext, Task<IResult>> (fun ctx -> Endpoints.createRecord state ctx)
        )
        |> ignore

        app.MapGet (
            "/xrpc/com.atproto.repo.getRecord",
            Func<HttpContext, Task<IResult>> (fun ctx -> Endpoints.getRecord state ctx)
        )
        |> ignore

        app.MapPost (
            "/xrpc/com.atproto.repo.deleteRecord",
            Func<HttpContext, Task<IResult>> (fun ctx -> Endpoints.deleteRecord state ctx)
        )
        |> ignore

        app.MapGet (
            "/xrpc/com.atproto.repo.listRecords",
            Func<HttpContext, Task<IResult>> (fun ctx -> Endpoints.listRecords state ctx)
        )
        |> ignore

        app

    /// Build and configure a WebApplication with all PDS endpoints.
    let configure (builder : PdsBuilder) : WebApplication =
        let webAppBuilder = WebApplication.CreateBuilder ()
        webAppBuilder.WebHost.UseUrls (sprintf "http://0.0.0.0:%d" builder.Port) |> ignore
        let app = webAppBuilder.Build ()
        mapEndpoints builder app

    /// Configure and immediately run the PDS (blocking).
    let run (hostname : string) (port : int) : unit =
        let app =
            defaults hostname
            |> withPort port
            |> configure

        app.Run ()
