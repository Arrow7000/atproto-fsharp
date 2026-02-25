module NamingTests

open Expecto
open FSharp.ATProto.CodeGen.Naming

[<Tests>]
let toPascalCaseTests =
    testList "toPascalCase" [
        testCase "lowercases single word" <| fun () ->
            Expect.equal (toPascalCase "post") "Post" ""

        testCase "camelCase two words" <| fun () ->
            Expect.equal (toPascalCase "replyRef") "ReplyRef" ""

        testCase "camelCase three words" <| fun () ->
            Expect.equal (toPascalCase "feedViewPost") "FeedViewPost" ""

        testCase "already PascalCase" <| fun () ->
            Expect.equal (toPascalCase "ReplyRef") "ReplyRef" ""

        testCase "single char" <| fun () ->
            Expect.equal (toPascalCase "a") "A" ""

        testCase "empty string" <| fun () ->
            Expect.equal (toPascalCase "") "" ""

        testCase "all lowercase single word" <| fun () ->
            Expect.equal (toPascalCase "defs") "Defs" ""

        testCase "already uppercase first char" <| fun () ->
            Expect.equal (toPascalCase "Post") "Post" ""
    ]

[<Tests>]
let nsidToNamespaceTests =
    testList "nsidToNamespace" [
        testCase "four segment NSID" <| fun () ->
            Expect.equal (nsidToNamespace "app.bsky.feed.post") "AppBskyFeed" ""

        testCase "three segment NSID" <| fun () ->
            Expect.equal (nsidToNamespace "app.bsky.authFullApp") "AppBsky" ""

        testCase "four segment com.atproto" <| fun () ->
            Expect.equal (nsidToNamespace "com.atproto.server.createSession") "ComAtprotoServer" ""

        testCase "four segment com.atproto.label.defs" <| fun () ->
            Expect.equal (nsidToNamespace "com.atproto.label.defs") "ComAtprotoLabel" ""

        testCase "four segment app.bsky.actor.defs" <| fun () ->
            Expect.equal (nsidToNamespace "app.bsky.actor.defs") "AppBskyActor" ""
    ]

[<Tests>]
let nsidToModuleNameTests =
    testList "nsidToModuleName" [
        testCase "post" <| fun () ->
            Expect.equal (nsidToModuleName "app.bsky.feed.post") "Post" ""

        testCase "defs" <| fun () ->
            Expect.equal (nsidToModuleName "app.bsky.feed.defs") "Defs" ""

        testCase "createSession" <| fun () ->
            Expect.equal (nsidToModuleName "com.atproto.server.createSession") "CreateSession" ""

        testCase "three segment NSID" <| fun () ->
            Expect.equal (nsidToModuleName "app.bsky.authFullApp") "AuthFullApp" ""
    ]

[<Tests>]
let nsidToFileNameTests =
    testList "nsidToFileName" [
        testCase "namespace to filename" <| fun () ->
            Expect.equal (nsidToFileName "AppBskyFeed") "AppBskyFeed.fs" ""

        testCase "another namespace" <| fun () ->
            Expect.equal (nsidToFileName "ComAtprotoServer") "ComAtprotoServer.fs" ""
    ]

[<Tests>]
let fullNamespaceTests =
    testList "fullNamespace" [
        testCase "prepends project namespace" <| fun () ->
            Expect.equal (fullNamespace "AppBskyFeed") "FSharp.ATProto.Bluesky.AppBskyFeed" ""

        testCase "another namespace" <| fun () ->
            Expect.equal (fullNamespace "ComAtprotoServer") "FSharp.ATProto.Bluesky.ComAtprotoServer" ""
    ]

[<Tests>]
let defToTypeNameTests =
    testList "defToTypeName" [
        testCase "main uses module name" <| fun () ->
            Expect.equal (defToTypeName "Post" "main") "Post" ""

        testCase "non-main gets PascalCased" <| fun () ->
            Expect.equal (defToTypeName "Post" "replyRef") "ReplyRef" ""

        testCase "non-main already PascalCase" <| fun () ->
            Expect.equal (defToTypeName "Defs" "FeedViewPost") "FeedViewPost" ""

        testCase "main with different module" <| fun () ->
            Expect.equal (defToTypeName "Like" "main") "Like" ""
    ]

[<Tests>]
let refToQualifiedTypeTests =
    testList "refToQualifiedType" [
        testCase "same namespace no fragment" <| fun () ->
            let (ns, typeName) = refToQualifiedType "AppBskyFeed" "app.bsky.feed.like"
            Expect.equal ns "AppBskyFeed" "namespace"
            Expect.equal typeName "Like.Like" "type name"

        testCase "same namespace with fragment" <| fun () ->
            let (ns, typeName) = refToQualifiedType "AppBskyFeed" "app.bsky.feed.defs#feedViewPost"
            Expect.equal ns "AppBskyFeed" "namespace"
            Expect.equal typeName "Defs.FeedViewPost" "type name"

        testCase "different namespace no fragment" <| fun () ->
            let (ns, typeName) = refToQualifiedType "AppBskyFeed" "com.atproto.repo.strongRef"
            Expect.equal ns "ComAtprotoRepo" "namespace"
            Expect.equal typeName "ComAtprotoRepo.StrongRef.StrongRef" "type name"

        testCase "different namespace with fragment" <| fun () ->
            let (ns, typeName) = refToQualifiedType "AppBskyFeed" "com.atproto.label.defs#label"
            Expect.equal ns "ComAtprotoLabel" "namespace"
            Expect.equal typeName "ComAtprotoLabel.Defs.Label" "type name"

        testCase "three segment ref no fragment" <| fun () ->
            let (ns, typeName) = refToQualifiedType "AppBsky" "app.bsky.authFullApp"
            Expect.equal ns "AppBsky" "namespace"
            Expect.equal typeName "AuthFullApp.AuthFullApp" "type name"
    ]

[<Tests>]
let escapeReservedWordTests =
    testList "escapeReservedWord" [
        testCase "type is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "type") "``type``" ""

        testCase "module is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "module") "``module``" ""

        testCase "namespace is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "namespace") "``namespace``" ""

        testCase "let is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "let") "``let``" ""

        testCase "match is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "match") "``match``" ""

        testCase "true is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "true") "``true``" ""

        testCase "false is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "false") "``false``" ""

        testCase "null is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "null") "``null``" ""

        testCase "non-reserved word unchanged" <| fun () ->
            Expect.equal (escapeReservedWord "Post") "Post" ""

        testCase "another non-reserved word" <| fun () ->
            Expect.equal (escapeReservedWord "feed") "feed" ""

        testCase "open is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "open") "``open``" ""

        testCase "in is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "in") "``in``" ""

        testCase "do is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "do") "``do``" ""

        testCase "if is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "if") "``if``" ""

        testCase "then is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "then") "``then``" ""

        testCase "else is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "else") "``else``" ""

        testCase "with is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "with") "``with``" ""

        testCase "for is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "for") "``for``" ""

        testCase "while is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "while") "``while``" ""

        testCase "and is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "and") "``and``" ""

        testCase "or is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "or") "``or``" ""

        testCase "not is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "not") "``not``" ""

        testCase "begin is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "begin") "``begin``" ""

        testCase "end is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "end") "``end``" ""

        testCase "done is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "done") "``done``" ""

        testCase "rec is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "rec") "``rec``" ""

        testCase "mutable is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "mutable") "``mutable``" ""

        testCase "lazy is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "lazy") "``lazy``" ""

        testCase "abstract is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "abstract") "``abstract``" ""

        testCase "class is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "class") "``class``" ""

        testCase "struct is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "struct") "``struct``" ""

        testCase "interface is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "interface") "``interface``" ""

        testCase "override is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "override") "``override``" ""

        testCase "default is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "default") "``default``" ""

        testCase "member is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "member") "``member``" ""

        testCase "static is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "static") "``static``" ""

        testCase "val is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "val") "``val``" ""

        testCase "new is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "new") "``new``" ""

        testCase "as is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "as") "``as``" ""

        testCase "base is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "base") "``base``" ""

        testCase "global is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "global") "``global``" ""

        testCase "void is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "void") "``void``" ""

        testCase "of is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "of") "``of``" ""

        testCase "to is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "to") "``to``" ""

        testCase "use is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "use") "``use``" ""

        testCase "yield is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "yield") "``yield``" ""

        testCase "return is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "return") "``return``" ""

        testCase "fun is reserved" <| fun () ->
            Expect.equal (escapeReservedWord "fun") "``fun``" ""
    ]
