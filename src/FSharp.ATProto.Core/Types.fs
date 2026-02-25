namespace FSharp.ATProto.Core

open System
open System.Net.Http

/// XRPC error returned by AT Protocol endpoints.
type XrpcError =
    { StatusCode: int
      Error: string option
      Message: string option }

/// Authenticated session with a PDS.
type AtpSession =
    { AccessJwt: string
      RefreshJwt: string
      Did: string
      Handle: string }

/// Client agent for communicating with an AT Protocol PDS.
type AtpAgent =
    { HttpClient: HttpClient
      BaseUrl: Uri
      mutable Session: AtpSession option }
