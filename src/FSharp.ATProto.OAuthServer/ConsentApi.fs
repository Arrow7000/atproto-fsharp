namespace FSharp.ATProto.OAuthServer

open System
open System.IO
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open FSharp.ATProto.Syntax

/// JSON API endpoints for consent UI frontend.
module ConsentApi =

    // ── Helpers ──────────────────────────────────────────────────────────

    /// Read and parse JSON body from an HttpContext.
    let private readJsonBody (ctx: HttpContext) : Task<JsonElement> =
        task {
            use reader = new StreamReader(ctx.Request.Body)
            let! body = reader.ReadToEndAsync()
            let doc = JsonDocument.Parse(body)
            return doc.RootElement
        }

    /// Try to get a string property from a JsonElement.
    let private tryGetString (name: string) (element: JsonElement) : string option =
        match element.TryGetProperty(name) with
        | true, prop when prop.ValueKind = JsonValueKind.String ->
            let v = prop.GetString()
            if String.IsNullOrWhiteSpace(v) then None else Some v
        | _ -> None

    /// Create a JSON string result.
    let private jsonResult (statusCode: int) (json: string) : IResult =
        Results.Text(json, "application/json", Encoding.UTF8, statusCode)

    /// Build a redirect URI with query parameters appended.
    let private appendQueryParams (baseUri: string) (parameters: (string * string) list) : string =
        let separator = if baseUri.Contains("?") then "&" else "?"
        let queryString = parameters |> List.map (fun (k, v) -> sprintf "%s=%s" k (Uri.EscapeDataString(v))) |> String.concat "&"
        sprintf "%s%s%s" baseUri separator queryString

    // ── Endpoint handlers ────────────────────────────────────────────────

    /// POST /api/sign-in
    /// Authenticates a user via the account store.
    let signIn (deps: Endpoints.EndpointDeps) (ctx: HttpContext) : Task<IResult> =
        task {
            try
                let! root = readJsonBody ctx
                let identifier = tryGetString "identifier" root
                let password = tryGetString "password" root

                match identifier, password with
                | None, _ | _, None ->
                    let ms = new MemoryStream()
                    use writer = new Utf8JsonWriter(ms)
                    writer.WriteStartObject()
                    writer.WriteString("error", "Missing identifier or password")
                    writer.WriteEndObject()
                    writer.Flush()
                    return jsonResult 400 (Encoding.UTF8.GetString(ms.ToArray()))
                | Some identifier, Some password ->

                let credentials = { Identifier = identifier; Password = password }
                let! result = deps.AccountStore.Authenticate(credentials)

                match result with
                | Ok info ->
                    let ms = new MemoryStream()
                    use writer = new Utf8JsonWriter(ms)
                    writer.WriteStartObject()
                    writer.WriteString("sub", Did.value info.Sub)

                    match info.Handle with
                    | Some h -> writer.WriteString("handle", Handle.value h)
                    | None -> writer.WriteNull("handle")

                    writer.WriteEndObject()
                    writer.Flush()
                    return jsonResult 200 (Encoding.UTF8.GetString(ms.ToArray()))
                | Error msg ->
                    let ms = new MemoryStream()
                    use writer = new Utf8JsonWriter(ms)
                    writer.WriteStartObject()
                    writer.WriteString("error", msg)
                    writer.WriteEndObject()
                    writer.Flush()
                    return jsonResult 401 (Encoding.UTF8.GetString(ms.ToArray()))
            with ex ->
                let ms = new MemoryStream()
                use writer = new Utf8JsonWriter(ms)
                writer.WriteStartObject()
                writer.WriteString("error", sprintf "Invalid request: %s" ex.Message)
                writer.WriteEndObject()
                writer.Flush()
                return jsonResult 400 (Encoding.UTF8.GetString(ms.ToArray()))
        }

    /// POST /api/consent
    /// Approves an authorization request, generating an auth code and returning a redirect URL.
    let consent (deps: Endpoints.EndpointDeps) (ctx: HttpContext) : Task<IResult> =
        task {
            try
                let! root = readJsonBody ctx
                let requestId = tryGetString "request_id" root
                let subStr = tryGetString "sub" root

                match requestId, subStr with
                | None, _ | _, None ->
                    let ms = new MemoryStream()
                    use writer = new Utf8JsonWriter(ms)
                    writer.WriteStartObject()
                    writer.WriteString("error", "Missing request_id or sub")
                    writer.WriteEndObject()
                    writer.Flush()
                    return jsonResult 400 (Encoding.UTF8.GetString(ms.ToArray()))
                | Some requestId, Some subStr ->

                // Parse the DID
                match Did.parse subStr with
                | Error _ ->
                    let ms = new MemoryStream()
                    use writer = new Utf8JsonWriter(ms)
                    writer.WriteStartObject()
                    writer.WriteString("error", "Invalid DID format")
                    writer.WriteEndObject()
                    writer.Flush()
                    return jsonResult 400 (Encoding.UTF8.GetString(ms.ToArray()))
                | Ok sub ->

                let! requestOpt = deps.RequestStore.ReadRequest(requestId)

                match requestOpt with
                | None ->
                    let ms = new MemoryStream()
                    use writer = new Utf8JsonWriter(ms)
                    writer.WriteStartObject()
                    writer.WriteString("error", "Unknown or expired request")
                    writer.WriteEndObject()
                    writer.Flush()
                    return jsonResult 404 (Encoding.UTF8.GetString(ms.ToArray()))
                | Some request ->

                // Check expiration
                if request.ExpiresAt < DateTimeOffset.UtcNow then
                    do! deps.RequestStore.DeleteRequest(requestId)

                    let ms = new MemoryStream()
                    use writer = new Utf8JsonWriter(ms)
                    writer.WriteStartObject()
                    writer.WriteString("error", "Authorization request has expired")
                    writer.WriteEndObject()
                    writer.Flush()
                    return jsonResult 400 (Encoding.UTF8.GetString(ms.ToArray()))
                else

                // Generate auth code and update the request
                let code = TokenSigner.generateRandomString ()

                let updatedRequest =
                    { request with
                        Code = Some code
                        AuthorizedSub = Some sub }

                // Delete old request and create updated one (atomic update)
                do! deps.RequestStore.DeleteRequest(requestId)
                do! deps.RequestStore.CreateRequest(requestId, updatedRequest)

                // Build redirect URL
                let queryParams =
                    [ "code", code
                      "iss", deps.Config.Issuer ]
                    @ (match request.State with
                       | Some state -> [ "state", state ]
                       | None -> [])

                let redirectUrl = appendQueryParams request.RedirectUri queryParams

                let ms = new MemoryStream()
                use writer = new Utf8JsonWriter(ms)
                writer.WriteStartObject()
                writer.WriteString("redirect_uri", redirectUrl)
                writer.WriteEndObject()
                writer.Flush()
                return jsonResult 200 (Encoding.UTF8.GetString(ms.ToArray()))
            with ex ->
                let ms = new MemoryStream()
                use writer = new Utf8JsonWriter(ms)
                writer.WriteStartObject()
                writer.WriteString("error", sprintf "Invalid request: %s" ex.Message)
                writer.WriteEndObject()
                writer.Flush()
                return jsonResult 400 (Encoding.UTF8.GetString(ms.ToArray()))
        }

    /// POST /api/reject
    /// Rejects an authorization request and returns a redirect URL with error.
    let reject (deps: Endpoints.EndpointDeps) (ctx: HttpContext) : Task<IResult> =
        task {
            try
                let! root = readJsonBody ctx
                let requestId = tryGetString "request_id" root

                match requestId with
                | None ->
                    let ms = new MemoryStream()
                    use writer = new Utf8JsonWriter(ms)
                    writer.WriteStartObject()
                    writer.WriteString("error", "Missing request_id")
                    writer.WriteEndObject()
                    writer.Flush()
                    return jsonResult 400 (Encoding.UTF8.GetString(ms.ToArray()))
                | Some requestId ->

                let! requestOpt = deps.RequestStore.ReadRequest(requestId)

                match requestOpt with
                | None ->
                    let ms = new MemoryStream()
                    use writer = new Utf8JsonWriter(ms)
                    writer.WriteStartObject()
                    writer.WriteString("error", "Unknown or expired request")
                    writer.WriteEndObject()
                    writer.Flush()
                    return jsonResult 404 (Encoding.UTF8.GetString(ms.ToArray()))
                | Some request ->

                // Delete the request
                do! deps.RequestStore.DeleteRequest(requestId)

                // Build redirect URL with error
                let queryParams =
                    [ "error", "access_denied"
                      "iss", deps.Config.Issuer ]
                    @ (match request.State with
                       | Some state -> [ "state", state ]
                       | None -> [])

                let redirectUrl = appendQueryParams request.RedirectUri queryParams

                let ms = new MemoryStream()
                use writer = new Utf8JsonWriter(ms)
                writer.WriteStartObject()
                writer.WriteString("redirect_uri", redirectUrl)
                writer.WriteEndObject()
                writer.Flush()
                return jsonResult 200 (Encoding.UTF8.GetString(ms.ToArray()))
            with ex ->
                let ms = new MemoryStream()
                use writer = new Utf8JsonWriter(ms)
                writer.WriteStartObject()
                writer.WriteString("error", sprintf "Invalid request: %s" ex.Message)
                writer.WriteEndObject()
                writer.Flush()
                return jsonResult 400 (Encoding.UTF8.GetString(ms.ToArray()))
        }
