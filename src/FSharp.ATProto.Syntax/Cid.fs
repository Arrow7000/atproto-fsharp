namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

/// <summary>
/// A Content Identifier (CID) used to reference content-addressed data in the AT Protocol.
/// CIDs are self-describing content hashes that uniquely identify a piece of data.
/// Only CIDv1 is supported; CIDv0 (starting with <c>Qmb</c>) is rejected.
/// </summary>
/// <remarks>
/// CIDs in the AT Protocol use CIDv1 with DAG-CBOR codec and SHA-256 hash.
/// This type performs syntactic validation only (base-encoded alphanumeric string of 8-256 characters).
/// See https://github.com/multiformats/cid for the CID specification.
/// </remarks>
type Cid =
    private
    | Cid of string

    override this.ToString () = let (Cid s) = this in s

/// <summary>
/// Functions for creating, validating, and extracting data from <see cref="Cid"/> values.
/// </summary>
module Cid =
    let private pattern = Regex (@"^[a-zA-Z0-9+/=]{8,256}$", RegexOptions.Compiled)

    /// <summary>
    /// Extract the string representation of a CID.
    /// </summary>
    /// <param name="cid">The CID to extract the value from.</param>
    /// <returns>The CID string in its base-encoded form.</returns>
    let value (Cid s) = s

    /// <summary>
    /// Parse and validate a CID string.
    /// </summary>
    /// <param name="s">
    /// A CID string of 8-256 alphanumeric characters (plus <c>+</c>, <c>/</c>, <c>=</c>).
    /// CIDv0 strings (starting with <c>Qmb</c>) are not supported.
    /// </param>
    /// <returns>
    /// <c>Ok</c> with a validated <see cref="Cid"/>, or <c>Error</c> with a message describing the validation failure.
    /// Validation failures include: null input, CIDv0 format, or invalid syntax.
    /// </returns>
    let parse (s : string) : Result<Cid, string> =
        if isNull s then
            Error "CID cannot be null"
        elif s.StartsWith ("Qmb") then
            Error "CIDv0 is not supported"
        elif not (pattern.IsMatch (s)) then
            Error (sprintf "Invalid CID syntax: %s" s)
        else
            Ok (Cid s)

    /// <summary>
    /// Create a CID from a string that has already been validated.
    /// This bypasses validation and should only be used by trusted internal code.
    /// </summary>
    /// <param name="s">A pre-validated CID string.</param>
    /// <returns>A <see cref="Cid"/> wrapping the string.</returns>
    let internal fromValidated (s : string) = Cid s
