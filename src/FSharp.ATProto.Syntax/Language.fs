namespace FSharp.ATProto.Syntax

open System.Text.RegularExpressions

type Language = private Language of string

module Language =
    // BCP 47 language tag (simplified/naive parser)
    // From test data analysis:
    // - Primary subtag: 2-3 lowercase letters, OR "i" (grandfathered)
    // - Subsequent subtags: alphanumeric, separated by hyphens
    // - No trailing hyphens
    // - Case sensitive: primary must be lowercase
    // Valid: ja, ban, pt-BR, i-default, i-navajo, zh-hakka, en-GB-boont-r-extended-sequence-x-private
    // Invalid: jaja (4 lowercase), JA (uppercase primary), j (single letter that's not 'i'),
    //          ja- (trailing hyphen), a-DE (single letter that's not 'i'), 123, .
    let private pattern =
        Regex(@"^(i|[a-z]{2,3})(-[a-zA-Z0-9]+)*$", RegexOptions.Compiled)

    let value (Language s) = s

    let parse (s: string) : Result<Language, string> =
        if isNull s then Error "Language cannot be null"
        elif not (pattern.IsMatch(s)) then Error (sprintf "Invalid language tag: %s" s)
        else Ok (Language s)
