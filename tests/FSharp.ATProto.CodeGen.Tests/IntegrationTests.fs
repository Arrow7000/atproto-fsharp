module IntegrationTests

open System.IO
open Expecto
open FSharp.ATProto.Lexicon
open FSharp.ATProto.Syntax
open FSharp.ATProto.CodeGen.NamespaceGen

// ---------------------------------------------------------------------------
// Test helpers -- create simple LexiconDoc values for unit tests
// ---------------------------------------------------------------------------

let private mkNsid (s : string) =
    match Nsid.parse s with
    | Ok nsid -> nsid
    | Error e -> failwithf "Invalid test NSID %s: %s" s e

let private emptyStringConstraints : LexString =
    { Description = None
      Default = None
      Const = None
      Enum = None
      KnownValues = None
      Format = None
      MinLength = None
      MaxLength = None
      MinGraphemes = None
      MaxGraphemes = None }

let private emptyIntegerConstraints : LexInteger =
    { Description = None
      Default = None
      Const = None
      Enum = None
      Minimum = None
      Maximum = None }

let private mkRecordDoc (nsid : string) (props : (string * LexType) list) (required : string list) =
    { Lexicon = 1
      Id = mkNsid nsid
      Revision = None
      Description = None
      Defs =
        Map.ofList
            [ "main",
              LexDef.Record
                  { Key = "tid"
                    Description = None
                    Record =
                      { Description = None
                        Properties = Map.ofList props
                        Required = required
                        Nullable = [] } } ] }

let private mkQueryDoc (nsid : string) (hasOutput : bool) =
    { Lexicon = 1
      Id = mkNsid nsid
      Revision = None
      Description = None
      Defs =
        Map.ofList
            [ "main",
              LexDef.Query
                  { Description = None
                    Parameters =
                      Some
                          { Description = None
                            Properties = Map.ofList [ "limit", LexType.Integer emptyIntegerConstraints ]
                            Required = [] }
                    Output =
                      if hasOutput then
                          Some
                              { Description = None
                                Encoding = "application/json"
                                Schema =
                                  Some (
                                      LexType.Object
                                          { Description = None
                                            Properties =
                                              Map.ofList
                                                  [ "items",
                                                    LexType.Array
                                                        { Description = None
                                                          Items = LexType.String emptyStringConstraints
                                                          MinLength = None
                                                          MaxLength = None } ]
                                            Required = [ "items" ]
                                            Nullable = [] }
                                  ) }
                      else
                          None
                    Errors = [] } ] }

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

[<Tests>]
let groupByNamespaceTests =
    testList
        "namespace grouping"
        [ testCase "groups docs by 3-segment namespace"
          <| fun () ->
              let doc1 =
                  mkRecordDoc "app.bsky.feed.post" [ "text", LexType.String emptyStringConstraints ] [ "text" ]

              let doc2 =
                  mkRecordDoc "app.bsky.feed.like" [ "subject", LexType.String emptyStringConstraints ] [ "subject" ]

              let doc3 =
                  mkRecordDoc "com.atproto.repo.strongRef" [ "uri", LexType.String emptyStringConstraints ] [ "uri" ]

              let groups = groupByNamespace [ doc1; doc2; doc3 ]
              Expect.equal (Map.count groups) 2 "2 namespaces"
              Expect.equal (groups.["AppBskyFeed"].Length) 2 "2 docs in feed"
              Expect.equal (groups.["ComAtprotoRepo"].Length) 1 "1 doc in repo"

          testCase "single doc produces single namespace"
          <| fun () ->
              let doc =
                  mkRecordDoc "app.bsky.actor.profile" [ "name", LexType.String emptyStringConstraints ] [ "name" ]

              let groups = groupByNamespace [ doc ]
              Expect.equal (Map.count groups) 1 "1 namespace"
              Expect.isTrue (groups.ContainsKey "AppBskyActor") "has AppBskyActor"

          testCase "empty list produces empty map"
          <| fun () ->
              let groups = groupByNamespace []
              Expect.equal (Map.count groups) 0 "0 namespaces" ]

[<Tests>]
let topologicalSortTests =
    testList
        "topological sort"
        [ testCase "dependencies come first"
          <| fun () ->
              let deps =
                  Map.ofList
                      [ "AppBskyFeed", Set.ofList [ "ComAtprotoRepo"; "AppBskyActor" ]
                        "AppBskyActor", Set.ofList [ "ComAtprotoLabel" ]
                        "ComAtprotoRepo", Set.empty
                        "ComAtprotoLabel", Set.empty ]

              let order = topologicalSort deps

              // All namespaces should be present
              Expect.equal (List.length order) 4 "4 namespaces"

              let indexOf ns = List.findIndex ((=) ns) order

              // ComAtprotoRepo before AppBskyFeed
              Expect.isTrue (indexOf "ComAtprotoRepo" < indexOf "AppBskyFeed") "repo before feed"
              // ComAtprotoLabel before AppBskyActor
              Expect.isTrue (indexOf "ComAtprotoLabel" < indexOf "AppBskyActor") "label before actor"
              // AppBskyActor before AppBskyFeed
              Expect.isTrue (indexOf "AppBskyActor" < indexOf "AppBskyFeed") "actor before feed"

          testCase "no dependencies returns all nodes"
          <| fun () ->
              let deps = Map.ofList [ "A", Set.empty; "B", Set.empty; "C", Set.empty ]

              let order = topologicalSort deps
              Expect.equal (List.length order) 3 "3 nodes"

          testCase "handles cycles gracefully"
          <| fun () ->
              let deps = Map.ofList [ "A", Set.ofList [ "B" ]; "B", Set.ofList [ "A" ] ]

              let order = topologicalSort deps
              Expect.equal (List.length order) 2 "both nodes present despite cycle"

          testCase "empty map returns empty list"
          <| fun () ->
              let order = topologicalSort Map.empty
              Expect.equal (List.length order) 0 "empty"

          testCase "includes dependency-only namespaces"
          <| fun () ->
              // "D" is only referenced, never a key
              let deps = Map.ofList [ "A", Set.ofList [ "D" ] ]

              let order = topologicalSort deps
              Expect.equal (List.length order) 2 "2 nodes"
              let indexOf ns = List.findIndex ((=) ns) order
              Expect.isTrue (indexOf "D" < indexOf "A") "D before A" ]

[<Tests>]
let collectDependenciesTests =
    testList
        "collectDependencies"
        [ testCase "collects cross-namespace ref"
          <| fun () ->
              let doc =
                  mkRecordDoc
                      "app.bsky.feed.like"
                      [ "subject",
                        LexType.Ref
                            { Description = None
                              Ref = "com.atproto.repo.strongRef" } ]
                      [ "subject" ]

              let deps = collectDependencies "AppBskyFeed" [ doc ]
              Expect.isTrue (Set.contains "ComAtprotoRepo" deps) "has ComAtprotoRepo dep"

          testCase "same-namespace ref is not a dependency"
          <| fun () ->
              let doc =
                  mkRecordDoc
                      "app.bsky.feed.post"
                      [ "reply",
                        LexType.Ref
                            { Description = None
                              Ref = "app.bsky.feed.post#replyRef" } ]
                      [ "reply" ]

              let deps = collectDependencies "AppBskyFeed" [ doc ]
              Expect.isFalse (Set.contains "AppBskyFeed" deps) "no self-dependency"

          testCase "collects union ref dependencies"
          <| fun () ->
              let doc =
                  { Lexicon = 1
                    Id = mkNsid "app.bsky.feed.post"
                    Revision = None
                    Description = None
                    Defs =
                      Map.ofList
                          [ "main",
                            LexDef.Record
                                { Key = "tid"
                                  Description = None
                                  Record =
                                    { Description = None
                                      Properties =
                                        Map.ofList
                                            [ "labels",
                                              LexType.Union
                                                  { Description = None
                                                    Refs = [ "com.atproto.label.defs#selfLabels" ]
                                                    Closed = false } ]
                                      Required = []
                                      Nullable = [] } } ] }

              let deps = collectDependencies "AppBskyFeed" [ doc ]
              Expect.isTrue (Set.contains "ComAtprotoLabel" deps) "has ComAtprotoLabel dep" ]

[<Tests>]
let generateAllTests =
    testList
        "generateAll"
        [ testCase "generates single file with namespace rec"
          <| fun () ->
              let doc =
                  mkRecordDoc
                      "app.bsky.feed.post"
                      [ "text", LexType.String emptyStringConstraints
                        "createdAt", LexType.String emptyStringConstraints ]
                      [ "text"; "createdAt" ]

              let result = generateAll [ doc ]
              Expect.equal (List.length result) 1 "1 file"
              let (fileName, content) = result.[0]
              Expect.equal fileName "Generated.fs" "file named Generated.fs"
              Expect.stringContains content "namespace rec FSharp.ATProto.Bluesky" "has namespace rec"
              Expect.stringContains content "module AppBskyFeed" "has group module"
              Expect.stringContains content "module Post" "has NSID module"
              Expect.stringContains content "let TypeId = \"app.bsky.feed.post\"" "has TypeId"
              Expect.stringContains content "type Post =" "has record type"
              Expect.stringContains content "Text: string" "has Text field"

          testCase "generates modules for multiple namespaces"
          <| fun () ->
              let doc1 =
                  mkRecordDoc "com.atproto.repo.strongRef" [ "uri", LexType.String emptyStringConstraints ] [ "uri" ]

              let doc2 =
                  mkRecordDoc
                      "app.bsky.feed.like"
                      [ "subject",
                        LexType.Ref
                            { Description = None
                              Ref = "com.atproto.repo.strongRef" } ]
                      [ "subject" ]

              let result = generateAll [ doc2; doc1 ]
              Expect.equal (List.length result) 1 "1 file"
              let (_fileName, content) = result.[0]
              Expect.stringContains content "module ComAtprotoRepo" "has repo module"
              Expect.stringContains content "module AppBskyFeed" "has feed module"

          testCase "generates query module with Params and Output"
          <| fun () ->
              let doc = mkQueryDoc "app.bsky.feed.getAuthorFeed" true
              let result = generateAll [ doc ]
              let (_fileName, content) = result.[0]
              Expect.stringContains content "module GetAuthorFeed" "has module"
              Expect.stringContains content "type Params" "has Params type"
              Expect.stringContains content "type Output" "has Output type"
              Expect.stringContains content "let TypeId" "has TypeId"

          testCase "generates token as literal constant"
          <| fun () ->
              let doc =
                  { Lexicon = 1
                    Id = mkNsid "app.bsky.feed.defs"
                    Revision = None
                    Description = None
                    Defs = Map.ofList [ "interactionSeen", LexDef.Token { Description = Some "Interaction seen" } ] }

              let result = generateAll [ doc ]
              let (_fileName, content) = result.[0]
              Expect.stringContains content "[<Literal>]" "has Literal"
              Expect.stringContains content "InteractionSeen" "has PascalCase name"
              Expect.stringContains content "app.bsky.feed.defs#interactionSeen" "has token value" ]

[<Tests>]
let realLexiconTests =
    testList
        "real lexicons"
        [ testCase "all real lexicon files parse and group into namespaces"
          <| fun () ->
              let files =
                  Directory.GetFiles (TestHelpers.lexiconDir, "*.json", SearchOption.AllDirectories)

              let docs =
                  files
                  |> Array.choose (fun f -> LexiconParser.parse (File.ReadAllText (f)) |> Result.toOption)
                  |> Array.toList

              let groups = groupByNamespace docs
              Expect.isGreaterThan (Map.count groups) 30 "30+ namespaces"

          testCase "generateAll produces single file for all namespaces"
          <| fun () ->
              let files =
                  Directory.GetFiles (TestHelpers.lexiconDir, "*.json", SearchOption.AllDirectories)

              let docs =
                  files
                  |> Array.choose (fun f -> LexiconParser.parse (File.ReadAllText (f)) |> Result.toOption)
                  |> Array.toList

              let generated = generateAll docs
              Expect.equal generated.Length 1 "1 file"
              let (_fileName, content) = generated.[0]
              Expect.stringContains content "namespace rec FSharp.ATProto.Bluesky" "has namespace rec"
              // Verify key group modules are present
              Expect.stringContains content "module AppBskyFeed" "has AppBskyFeed"
              Expect.stringContains content "module ComAtprotoRepo" "has ComAtprotoRepo"
              Expect.stringContains content "module ComAtprotoLabel" "has ComAtprotoLabel" ]
