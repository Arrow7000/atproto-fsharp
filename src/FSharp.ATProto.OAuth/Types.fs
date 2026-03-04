namespace FSharp.ATProto.OAuth

open System
open System.Security.Cryptography
open FSharp.ATProto.Syntax

/// OAuth error types for the AT Protocol OAuth flow.
[<RequireQualifiedAccess>]
type OAuthError =
    | DiscoveryFailed of message: string
    | TokenRequestFailed of error: string * description: string option
    | DPoPError of message: string
    | InvalidState of message: string
    | NetworkError of message: string

/// Client metadata for OAuth registration.
/// Describes the client application to the authorization server.
type ClientMetadata =
    { ClientId: string
      ClientUri: string option
      RedirectUris: string list
      Scope: string
      GrantTypes: string list
      ResponseTypes: string list
      TokenEndpointAuthMethod: string
      ApplicationType: string
      DpopBoundAccessTokens: bool }

/// Authorization server metadata (RFC 8414).
/// Discovered from the authorization server's well-known endpoint.
type AuthorizationServerMetadata =
    { Issuer: string
      AuthorizationEndpoint: string
      TokenEndpoint: string
      PushedAuthorizationRequestEndpoint: string option
      ScopesSupported: string list
      ResponseTypesSupported: string list
      GrantTypesSupported: string list
      TokenEndpointAuthMethodsSupported: string list
      DpopSigningAlgValuesSupported: string list
      RequirePushedAuthorizationRequests: bool }

/// Protected resource metadata (RFC 9728).
/// Discovered from the PDS's well-known endpoint.
type ProtectedResourceMetadata =
    { Resource: string
      AuthorizationServers: string list
      ScopesSupported: string list }

/// Token response from the authorization server.
type TokenResponse =
    { AccessToken: string
      TokenType: string
      ExpiresIn: int
      RefreshToken: string option
      Scope: string option
      Sub: string }

/// PKCE code verifier and challenge pair (RFC 7636).
type PkceChallenge =
    { Verifier: string
      Challenge: string
      Method: string }

/// State for an in-progress authorization flow.
/// Save this between the redirect to the authorization server and the callback.
type AuthorizationState =
    { State: string
      Pkce: PkceChallenge
      DpopKeyPair: ECDsa
      RedirectUri: string
      AuthorizationServer: AuthorizationServerMetadata }

/// A completed OAuth session with DPoP-bound tokens.
type OAuthSession =
    { AccessToken: string
      RefreshToken: string option
      ExpiresAt: DateTimeOffset
      Did: Did
      DpopKeyPair: ECDsa
      TokenEndpoint: string }
