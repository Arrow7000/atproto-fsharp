module FSharp.ATProto.CodeGen.Naming

open System

/// Convert camelCase/lowercase to PascalCase.
/// Just uppercases the first character (camelCase input already has internal capitals).
let toPascalCase (s: string) : string =
    if String.IsNullOrEmpty(s) then s
    else string (Char.ToUpperInvariant(s.[0])) + s.[1..]

/// All-but-last NSID segments, PascalCase concatenated.
/// "app.bsky.feed.post" -> "AppBskyFeed"
let nsidToNamespace (nsid: string) : string =
    let segments = nsid.Split('.')
    segments
    |> Array.take (segments.Length - 1)
    |> Array.map toPascalCase
    |> String.concat ""

/// Last NSID segment, PascalCased.
/// "app.bsky.feed.post" -> "Post"
let nsidToModuleName (nsid: string) : string =
    let segments = nsid.Split('.')
    segments |> Array.last |> toPascalCase

/// Namespace name + ".fs".
/// "AppBskyFeed" -> "AppBskyFeed.fs"
let nsidToFileName (namespaceName: string) : string =
    namespaceName + ".fs"

/// Prepend project namespace.
/// "AppBskyFeed" -> "FSharp.ATProto.Bluesky.AppBskyFeed"
let fullNamespace (namespaceName: string) : string =
    "FSharp.ATProto.Bluesky." + namespaceName

/// "main" uses the module name; others PascalCased.
/// defToTypeName "Post" "main" -> "Post"
/// defToTypeName "Post" "replyRef" -> "ReplyRef"
let defToTypeName (moduleName: string) (defName: string) : string =
    if defName = "main" then moduleName
    else toPascalCase defName

/// Resolve a fully-qualified ref to (targetNamespace, "Module.TypeName").
/// Refs are already fully qualified (LexiconParser resolves local refs).
/// refToQualifiedType "AppBskyFeed" "app.bsky.feed.defs#feedViewPost" -> ("AppBskyFeed", "Defs.FeedViewPost")
let refToQualifiedType (_currentNamespace: string) (ref: string) : string * string =
    let parts = ref.Split('#')
    let nsid = parts.[0]
    let defName = if parts.Length > 1 then parts.[1] else "main"
    let targetNamespace = nsidToNamespace nsid
    let moduleName = nsidToModuleName nsid
    let typeName = defToTypeName moduleName defName
    (targetNamespace, moduleName + "." + typeName)

/// F# reserved words that need double-backtick escaping.
let private reservedWords =
    set [
        "type"; "module"; "namespace"; "open"; "let"; "in"; "do"
        "if"; "then"; "else"; "match"; "with"; "for"; "while"
        "true"; "false"; "null"; "and"; "or"; "not"
        "begin"; "end"; "done"; "rec"; "mutable"; "lazy"
        "abstract"; "class"; "struct"; "interface"; "override"; "default"
        "member"; "static"; "val"; "new"; "as"; "base"; "global"; "void"
        "of"; "to"; "use"; "yield"; "return"; "fun"
    ]

/// Escape F# reserved words with double backticks.
/// "type" -> "``type``"
/// "Post" -> "Post" (not reserved, no change)
let escapeReservedWord (word: string) : string =
    if reservedWords.Contains(word) then
        sprintf "``%s``" word
    else
        word
