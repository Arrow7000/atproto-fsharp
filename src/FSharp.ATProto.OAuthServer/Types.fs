namespace FSharp.ATProto.OAuthServer

open System
open System.Threading.Tasks
open FSharp.ATProto.Syntax

/// Grant type for OAuth token requests.
[<RequireQualifiedAccess>]
type GrantType =
    | AuthorizationCode
    | RefreshToken

/// Error type for OAuth server operations.
[<RequireQualifiedAccess>]
type OAuthServerError =
    | InvalidRequest of message: string
    | InvalidClient of message: string
    | InvalidGrant of message: string
    | UnauthorizedClient of message: string
    | UnsupportedGrantType of message: string
    | InvalidScope of message: string
    | AccessDenied of message: string
    | ServerError of message: string
    | InvalidDpopProof of message: string
    | UseDpopNonce of nonce: string

    member this.ErrorCode =
        match this with
        | InvalidRequest _ -> "invalid_request"
        | InvalidClient _ -> "invalid_client"
        | InvalidGrant _ -> "invalid_grant"
        | UnauthorizedClient _ -> "unauthorized_client"
        | UnsupportedGrantType _ -> "unsupported_grant_type"
        | InvalidScope _ -> "invalid_scope"
        | AccessDenied _ -> "access_denied"
        | ServerError _ -> "server_error"
        | InvalidDpopProof _ -> "invalid_dpop_proof"
        | UseDpopNonce _ -> "use_dpop_nonce"

    member this.Description =
        match this with
        | InvalidRequest msg -> msg
        | InvalidClient msg -> msg
        | InvalidGrant msg -> msg
        | UnauthorizedClient msg -> msg
        | UnsupportedGrantType msg -> msg
        | InvalidScope msg -> msg
        | AccessDenied msg -> msg
        | ServerError msg -> msg
        | InvalidDpopProof msg -> msg
        | UseDpopNonce nonce -> sprintf "Use DPoP nonce: %s" nonce

/// Credentials provided during user sign-in.
type LoginCredentials =
    { Identifier: string
      Password: string }

/// Information about an authenticated account.
type AccountInfo =
    { Sub: Did
      Handle: Handle option
      DisplayName: string option }

/// Data stored for an in-progress authorization request.
type RequestData =
    { ClientId: string
      RedirectUri: string
      Scope: string
      State: string option
      CodeChallenge: string
      CodeChallengeMethod: string
      DpopJkt: string
      Code: string option
      AuthorizedSub: Did option
      ExpiresAt: DateTimeOffset
      CreatedAt: DateTimeOffset }

/// Data stored for an issued token.
type TokenData =
    { Sub: Did
      ClientId: string
      Scope: string
      DpopJkt: string
      AccessToken: string
      RefreshToken: string
      ExpiresAt: DateTimeOffset
      CreatedAt: DateTimeOffset }

/// Configuration for the OAuth authorization server.
type OAuthServerConfig =
    { Issuer: string
      ServiceDid: Did option
      AccessTokenLifetime: TimeSpan
      RefreshTokenLifetime: TimeSpan
      RequestLifetime: TimeSpan
      DpopNonceLifetime: TimeSpan
      ScopesSupported: string list
      SigningKey: byte[] -> byte[]
      PublicKeyJwk: string }

module OAuthServerConfig =
    let defaults =
        { Issuer = ""
          ServiceDid = None
          AccessTokenLifetime = TimeSpan.FromMinutes 5.0
          RefreshTokenLifetime = TimeSpan.FromDays 90.0
          RequestLifetime = TimeSpan.FromMinutes 10.0
          DpopNonceLifetime = TimeSpan.FromMinutes 5.0
          ScopesSupported = [ "atproto"; "transition:generic" ]
          SigningKey = fun _ -> Array.empty
          PublicKeyJwk = "" }

/// Scopes module for AT Protocol OAuth scope handling.
module OAuthScope =
    let atproto = "atproto"
    let transitionGeneric = "transition:generic"

    let parse (scopeString: string) =
        scopeString.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        |> Array.toList

    let format (scopes: string list) = String.Join(" ", scopes)

    let isValid (supported: string list) (requested: string list) =
        requested |> List.forall (fun s -> List.contains s supported)

    let hasAtproto (scopes: string list) = List.contains atproto scopes

/// Store for authorization tokens.
type ITokenStore =
    abstract CreateToken: tokenId: string * data: TokenData -> Task<unit>
    abstract ReadToken: tokenId: string -> Task<TokenData option>
    abstract DeleteToken: tokenId: string -> Task<unit>
    abstract RotateToken: tokenId: string * newId: string * newRefreshToken: string * newData: TokenData -> Task<unit>
    abstract FindByRefreshToken: refreshToken: string -> Task<(string * TokenData) option>

/// Store for pending authorization requests.
type IRequestStore =
    abstract CreateRequest: requestId: string * data: RequestData -> Task<unit>
    abstract ReadRequest: requestId: string -> Task<RequestData option>
    abstract ConsumeCode: code: string -> Task<RequestData option>
    abstract DeleteRequest: requestId: string -> Task<unit>

/// Store for DPoP/nonce replay detection.
type IReplayStore =
    abstract IsUnique: ns: string * key: string * expiresAt: DateTimeOffset -> Task<bool>

/// Store for user account authentication and lookup.
type IAccountStore =
    abstract Authenticate: credentials: LoginCredentials -> Task<Result<AccountInfo, string>>
    abstract GetAccount: sub: Did -> Task<AccountInfo option>
