namespace FSharp.ATProto.Core

open System
open System.Net.Http
open System.Threading.Tasks

/// Agent for communicating with an AT Protocol PDS.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module AtpAgent =

    /// Create a new agent pointing at a PDS.
    let create (baseUrl: string) : AtpAgent =
        let uri = if baseUrl.EndsWith("/") then Uri(baseUrl) else Uri(baseUrl + "/")
        { HttpClient = new HttpClient()
          BaseUrl = uri
          Session = None }

    /// Create a new agent with a provided HttpClient (for testing).
    let createWithClient (httpClient: HttpClient) (baseUrl: string) : AtpAgent =
        let uri = if baseUrl.EndsWith("/") then Uri(baseUrl) else Uri(baseUrl + "/")
        { HttpClient = httpClient
          BaseUrl = uri
          Session = None }

    /// Log in with identifier (handle or DID) + app password.
    let login (identifier: string) (password: string) (agent: AtpAgent) : Task<Result<AtpSession, XrpcError>> =
        task {
            let input = {| identifier = identifier; password = password |}
            let! result = Xrpc.procedure<{| identifier: string; password: string |}, AtpSession>
                            "com.atproto.server.createSession" input agent
            match result with
            | Ok session ->
                agent.Session <- Some session
                return Ok session
            | Error e ->
                return Error e
        }
