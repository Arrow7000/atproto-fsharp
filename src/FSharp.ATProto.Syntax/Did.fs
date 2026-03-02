namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

/// <summary>
/// A decentralized identifier (DID) as defined by the AT Protocol.
/// DIDs are the primary stable identifier for accounts. Two methods are currently supported:
/// <c>did:plc:</c> (hosted, managed by PLC directory) and <c>did:web:</c> (self-hosted, DNS-based).
/// </summary>
/// <remarks>
/// See the AT Protocol specification: https://atproto.com/specs/did
/// and the W3C DID specification: https://www.w3.org/TR/did-core/
/// </remarks>
type Did =
    private
    | Did of string

    override this.ToString () = let (Did s) = this in s

/// <summary>
/// Functions for creating, validating, and extracting data from <see cref="Did"/> values.
/// </summary>
module Did =
    let private pattern =
        Regex (@"^did:[a-z]+:[a-zA-Z0-9._:%-]*[a-zA-Z0-9._-]$", RegexOptions.Compiled)

    /// <summary>
    /// Extract the string representation of a DID.
    /// </summary>
    /// <param name="did">The DID to extract the value from.</param>
    /// <returns>The full DID string (e.g. <c>"did:plc:z72i7hdynmk6r22z27h6tvur"</c>).</returns>
    let value (Did s) = s

    let private hasValidPercentEncoding (s : string) =
        let mutable i = 0
        let mutable valid = true

        while valid && i < s.Length do
            if s.[i] = '%' then
                if i + 2 >= s.Length then
                    valid <- false
                elif not (System.Uri.IsHexDigit (s.[i + 1]) && System.Uri.IsHexDigit (s.[i + 2])) then
                    valid <- false
                else
                    i <- i + 3
            else
                i <- i + 1

        valid

    /// <summary>
    /// Parse and validate a DID string.
    /// </summary>
    /// <param name="s">
    /// A DID string in the format <c>did:&lt;method&gt;:&lt;method-specific-id&gt;</c>
    /// (e.g. <c>"did:plc:z72i7hdynmk6r22z27h6tvur"</c> or <c>"did:web:example.com"</c>).
    /// </param>
    /// <returns>
    /// <c>Ok</c> with a validated <see cref="Did"/>, or <c>Error</c> with a message describing the validation failure.
    /// Validation failures include: null input, exceeding the 2048-character limit,
    /// invalid DID syntax, or invalid percent-encoding.
    /// </returns>
    /// <example>
    /// <code>
    /// match Did.parse "did:plc:z72i7hdynmk6r22z27h6tvur" with
    /// | Ok did -> printfn "Valid: %s" (Did.value did)
    /// | Error e -> printfn "Invalid: %s" e
    /// </code>
    /// </example>
    let parse (s : string) : Result<Did, string> =
        if isNull s then
            Error "DID cannot be null"
        elif s.Length > 2048 then
            Error (sprintf "DID exceeds max length of 2048: %d" s.Length)
        elif not (pattern.IsMatch (s)) then
            Error (sprintf "Invalid DID syntax: %s" s)
        elif not (hasValidPercentEncoding s) then
            Error (sprintf "Invalid percent-encoding in DID: %s" s)
        else
            Ok (Did s)

    /// <summary>
    /// Extract the DID method from a validated DID.
    /// </summary>
    /// <param name="did">The DID to extract the method from.</param>
    /// <returns>The method string (e.g. <c>"plc"</c> or <c>"web"</c>).</returns>
    let method (Did s) =
        let i1 = s.IndexOf (':')
        let i2 = s.IndexOf (':', i1 + 1)
        s.Substring (i1 + 1, i2 - i1 - 1)
