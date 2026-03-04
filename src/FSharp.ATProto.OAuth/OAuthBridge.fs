namespace FSharp.ATProto.OAuth

open System.Net.Http
open System.Threading.Tasks
open FSharp.ATProto.Core

/// Bridges OAuth sessions with AtpAgent, enabling all convenience functions
/// to work with DPoP-authenticated OAuth sessions.
module OAuthBridge =

    /// Resume an OAuth-authenticated session on an AtpAgent.
    /// Returns a new agent configured with DPoP authentication and OAuth token refresh.
    /// All XRPC requests through the returned agent will use DPoP-bound access tokens.
    ///
    /// The optional onSessionUpdate callback is called with the new OAuthSession whenever
    /// the token is refreshed, allowing consumers to persist the session to disk or database.
    let resumeSession
        (clientMetadata : ClientMetadata)
        (session : OAuthSession)
        (onSessionUpdate : (OAuthSession -> unit) option)
        (agent : AtpAgent)
        : AtpAgent =
        let mutable currentSession = session
        let mutable dpopNonce : string option = None

        { agent with
            AuthenticateRequest =
                Some (fun (request : HttpRequestMessage) ->
                    let url = request.RequestUri.AbsoluteUri
                    let httpMethod = request.Method.Method
                    let ath = DPoP.hashAccessToken currentSession.AccessToken
                    let proof = DPoP.createProof currentSession.DpopKeyPair httpMethod url (Some ath) dpopNonce

                    request.Headers.TryAddWithoutValidation ("Authorization", sprintf "DPoP %s" currentSession.AccessToken)
                    |> ignore

                    request.Headers.TryAddWithoutValidation ("DPoP", proof) |> ignore)
            RefreshAuthentication =
                Some (fun () ->
                    task {
                        match! OAuthClient.refreshToken agent.HttpClient clientMetadata currentSession with
                        | Ok newSession ->
                            currentSession <- newSession
                            onSessionUpdate |> Option.iter (fun f -> f newSession)
                            return Ok ()
                        | Error oauthErr ->
                            return
                                Error
                                    { StatusCode = 401
                                      Error = Some "OAuthRefreshFailed"
                                      Message = Some (sprintf "%A" oauthErr) }
                    }) }

    /// Get the DID of the currently authenticated OAuth user.
    let getSessionDid (session : OAuthSession) : FSharp.ATProto.Syntax.Did = session.Did

    /// Check whether the OAuth session's access token has expired.
    let isExpired (session : OAuthSession) : bool =
        session.ExpiresAt <= System.DateTimeOffset.UtcNow
