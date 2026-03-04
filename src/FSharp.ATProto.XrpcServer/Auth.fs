namespace FSharp.ATProto.XrpcServer

open System.Security.Claims
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

/// Authentication middleware for XRPC servers.
/// Accepts a user-supplied verification function -- no direct crypto dependency.
module Auth =

    /// Key used to store the authenticated ClaimsPrincipal in HttpContext.Items.
    [<Literal>]
    let ClaimsPrincipalKey = "xrpc.auth.principal"

    /// Extract the Bearer token from the Authorization header.
    let extractBearerToken (ctx : HttpContext) : string option =
        match ctx.Request.Headers.Authorization.ToString () with
        | "" -> None
        | header ->
            let trimmed = header.Trim ()

            if trimmed.StartsWith "Bearer " then
                Some (trimmed.Substring 7)
            else
                None

    /// Verify the request's bearer token using the supplied verification function.
    /// On success, stores the ClaimsPrincipal in HttpContext.Items.
    /// Returns Ok with the principal on success, or Error with a message on failure.
    let verifyRequest
        (verifyToken : string -> Task<Result<ClaimsPrincipal, string>>)
        (ctx : HttpContext)
        : Task<Result<ClaimsPrincipal, string>> =
        task {
            match extractBearerToken ctx with
            | None ->
                return Error "Missing or invalid Authorization header"
            | Some token ->
                let! result = verifyToken token

                match result with
                | Ok principal ->
                    ctx.Items.[ClaimsPrincipalKey] <- principal
                    return Ok principal
                | Error message ->
                    return Error message
        }

    /// Retrieve the authenticated ClaimsPrincipal from HttpContext.Items.
    /// Returns None if the request was not authenticated.
    let getPrincipal (ctx : HttpContext) : ClaimsPrincipal option =
        match ctx.Items.TryGetValue ClaimsPrincipalKey with
        | true, (:? ClaimsPrincipal as p) -> Some p
        | _ -> None

    /// Get a specific claim value from the authenticated principal.
    let getClaim (claimType : string) (ctx : HttpContext) : string option =
        getPrincipal ctx
        |> Option.bind (fun p ->
            match p.FindFirst claimType with
            | null -> None
            | claim -> Some claim.Value)
