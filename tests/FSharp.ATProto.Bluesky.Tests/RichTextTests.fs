module FSharp.ATProto.Bluesky.Tests.RichTextTests

open Expecto
open FSharp.ATProto.Bluesky

[<Tests>]
let detectTests =
    testList
        "RichText.detect"
        [ testCase "detects mention in text"
          <| fun _ ->
              let facets = RichText.detect "Hello @my-handle.bsky.social!"
              Expect.equal facets.Length 1 "should find one facet"

              match facets.[0] with
              | RichText.DetectedMention (s, e, h) ->
                  Expect.equal s 6 "byteStart"
                  Expect.equal e 28 "byteEnd"
                  Expect.equal h "my-handle.bsky.social" "handle"
              | _ -> failtest "expected mention"

          testCase "detects link in text"
          <| fun _ ->
              let facets = RichText.detect "Check https://example.com ok"
              Expect.equal facets.Length 1 "should find one facet"

              match facets.[0] with
              | RichText.DetectedLink (s, e, u) ->
                  Expect.equal s 6 "byteStart"
                  Expect.equal e 25 "byteEnd"
                  Expect.equal u "https://example.com" "uri"
              | _ -> failtest "expected link"

          testCase "detects hashtag in text"
          <| fun _ ->
              let facets = RichText.detect "Hello #atproto world"
              Expect.equal facets.Length 1 "should find one facet"

              match facets.[0] with
              | RichText.DetectedTag (s, e, t) ->
                  Expect.equal s 6 "byteStart"
                  Expect.equal e 14 "byteEnd"
                  Expect.equal t "atproto" "tag"
              | _ -> failtest "expected tag"

          testCase "detects multiple facets"
          <| fun _ ->
              let facets = RichText.detect "Hi @my-handle.bsky.social check #atproto"
              Expect.equal facets.Length 2 "should find two facets"

          testCase "no facets in plain text"
          <| fun _ ->
              let facets = RichText.detect "Hello world"
              Expect.equal facets.Length 0 "should find no facets"

          testCase "mention must have dot (no bare @word)"
          <| fun _ ->
              let facets = RichText.detect "Hello @alice"
              Expect.equal facets.Length 0 "bare @word is not a mention"

          testCase "correct byte offsets with emoji"
          <| fun _ ->
              // 👋 is 4 bytes in UTF-8
              let facets = RichText.detect "👋 @my-handle.bsky.social"
              Expect.equal facets.Length 1 "should find one facet"

              match facets.[0] with
              | RichText.DetectedMention (s, e, _) ->
                  Expect.equal s 5 "byteStart (4 bytes emoji + 1 byte space)"
                  Expect.equal e 27 "byteEnd (5 + 22 = 27)"
              | _ -> failtest "expected mention"

          testCase "correct byte offsets with accented chars"
          <| fun _ ->
              // "Posição " has multibyte chars: ç (2 bytes), ã (2 bytes)
              let facets = RichText.detect "Posição @my-handle.bsky.social"
              Expect.equal facets.Length 1 "should find one facet"

              match facets.[0] with
              | RichText.DetectedMention (s, e, _) ->
                  // P(1)+o(1)+s(1)+i(1)+ç(2)+ã(2)+o(1)+ (1) = 10 bytes
                  Expect.equal s 10 "byteStart"
                  Expect.equal e 32 "byteEnd (10 + 22 = 32)"
              | _ -> failtest "expected mention"

          testCase "strips trailing punctuation from links"
          <| fun _ ->
              let facets = RichText.detect "See https://example.com."

              match facets.[0] with
              | RichText.DetectedLink (_, _, u) -> Expect.equal u "https://example.com" "trailing period stripped"
              | _ -> failtest "expected link"

          testCase "hashtag excludes pure numeric"
          <| fun _ ->
              let facets = RichText.detect "Test #123"
              Expect.equal facets.Length 0 "pure numeric hashtag excluded"

          testCase "mention at start of text"
          <| fun _ ->
              let facets = RichText.detect "@my-handle.bsky.social hello"
              Expect.equal facets.Length 1 "mention at start"

              match facets.[0] with
              | RichText.DetectedMention (s, _, _) -> Expect.equal s 0 "byteStart at 0"
              | _ -> failtest "expected mention"

          testCase "link with path and query"
          <| fun _ ->
              let facets = RichText.detect "Go to https://example.com/path?q=1 now"

              match facets.[0] with
              | RichText.DetectedLink (_, _, u) -> Expect.equal u "https://example.com/path?q=1" "full URL preserved"
              | _ -> failtest "expected link"

          testCase "hashtag with fullwidth hash"
          <| fun _ ->
              // ＃ (U+FF03) is 3 bytes in UTF-8
              let facets = RichText.detect "Hello ＃atproto"
              Expect.equal facets.Length 1 "fullwidth hash detected"

              match facets.[0] with
              | RichText.DetectedTag (_, _, t) -> Expect.equal t "atproto" "tag without hash prefix"
              | _ -> failtest "expected tag" ]

open System.Net
open System.Text
open System.Text.Json
open System.Threading.Tasks
open FSharp.ATProto.Core
open TestHelpers

[<Tests>]
let resolveTests =
    testList
        "RichText.resolve"
        [ testCase "resolves mention handle to DID"
          <| fun _ ->
              let agent =
                  createMockAgent (fun req ->
                      if req.RequestUri.PathAndQuery.Contains ("resolveHandle") then
                          jsonResponse HttpStatusCode.OK {| did = "did:plc:abc123" |}
                      else
                          emptyResponse HttpStatusCode.NotFound)

              agent.Session <-
                  Some
                      { AccessJwt = "test"
                        RefreshJwt = "test"
                        Did = FSharp.ATProto.Syntax.Did.parse "did:plc:me" |> Result.defaultWith failwith
                        Handle =
                          FSharp.ATProto.Syntax.Handle.parse "me.bsky.social"
                          |> Result.defaultWith failwith }

              let detected = [ RichText.DetectedMention (0, 22, "my-handle.bsky.social") ]

              let facets =
                  RichText.resolve agent detected |> Async.AwaitTask |> Async.RunSynchronously

              Expect.equal facets.Length 1 "one facet"
              Expect.equal facets.[0].Index.ByteStart 0L "byteStart"
              Expect.equal facets.[0].Index.ByteEnd 22L "byteEnd"

          testCase "drops mention when handle resolution fails"
          <| fun _ ->
              let agent =
                  createMockAgent (fun _ ->
                      jsonResponse
                          HttpStatusCode.BadRequest
                          {| error = "HandleNotFound"
                             message = "not found" |})

              agent.Session <-
                  Some
                      { AccessJwt = "test"
                        RefreshJwt = "test"
                        Did = FSharp.ATProto.Syntax.Did.parse "did:plc:me" |> Result.defaultWith failwith
                        Handle =
                          FSharp.ATProto.Syntax.Handle.parse "me.bsky.social"
                          |> Result.defaultWith failwith }

              let detected = [ RichText.DetectedMention (0, 22, "my-handle.bsky.social") ]

              let facets =
                  RichText.resolve agent detected |> Async.AwaitTask |> Async.RunSynchronously

              Expect.equal facets.Length 0 "mention dropped on failure"

          testCase "passes through links and tags without resolution"
          <| fun _ ->
              let agent = createMockAgent (fun _ -> emptyResponse HttpStatusCode.NotFound)

              let detected =
                  [ RichText.DetectedLink (0, 20, "https://example.com")
                    RichText.DetectedTag (21, 29, "atproto") ]

              let facets =
                  RichText.resolve agent detected |> Async.AwaitTask |> Async.RunSynchronously

              Expect.equal facets.Length 2 "both facets preserved"

          testCase "parse detects and resolves in one step"
          <| fun _ ->
              let agent =
                  createMockAgent (fun req ->
                      if req.RequestUri.PathAndQuery.Contains ("resolveHandle") then
                          jsonResponse HttpStatusCode.OK {| did = "did:plc:abc123" |}
                      else
                          emptyResponse HttpStatusCode.NotFound)

              agent.Session <-
                  Some
                      { AccessJwt = "test"
                        RefreshJwt = "test"
                        Did = FSharp.ATProto.Syntax.Did.parse "did:plc:me" |> Result.defaultWith failwith
                        Handle =
                          FSharp.ATProto.Syntax.Handle.parse "me.bsky.social"
                          |> Result.defaultWith failwith }

              let facets =
                  RichText.parse agent "Hello @my-handle.bsky.social #atproto"
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.equal facets.Length 2 "mention + hashtag" ]

[<Tests>]
let utilityTests =
    testList
        "RichText utilities"
        [ testCase "graphemeLength counts grapheme clusters"
          <| fun _ ->
              Expect.equal (RichText.graphemeLength "Hello") 5 "ASCII"
              Expect.equal (RichText.graphemeLength "\U0001F44B\U0001F3FD") 1 "emoji with skin tone = 1 grapheme"
              Expect.equal (RichText.graphemeLength "caf\u00E9") 4 "accented"

          testCase "byteLength counts UTF-8 bytes"
          <| fun _ ->
              Expect.equal (RichText.byteLength "Hello") 5 "ASCII"
              Expect.equal (RichText.byteLength "\U0001F44B") 4 "emoji"
              Expect.equal (RichText.byteLength "caf\u00E9") 5 "e-acute is 2 bytes" ]

open FsCheck

[<Tests>]
let propertyTests =
    testList
        "RichText.detect properties"
        [ testProperty "byte ranges within text bounds"
          <| fun (text : NonNull<string>) ->
              let facets = RichText.detect text.Get
              let totalBytes = Encoding.UTF8.GetByteCount (text.Get)

              facets
              |> List.iter (fun f ->
                  let s, e =
                      match f with
                      | RichText.DetectedMention (s, e, _) -> s, e
                      | RichText.DetectedLink (s, e, _) -> s, e
                      | RichText.DetectedTag (s, e, _) -> s, e

                  Expect.isLessThanOrEqual s totalBytes "start within bounds"
                  Expect.isLessThanOrEqual e totalBytes "end within bounds"
                  Expect.isLessThan s e "start < end")

          testProperty "detected facets are non-overlapping and sorted"
          <| fun (text : NonNull<string>) ->
              let facets = RichText.detect text.Get

              let ranges =
                  facets
                  |> List.map (fun f ->
                      match f with
                      | RichText.DetectedMention (s, e, _) -> s, e
                      | RichText.DetectedLink (s, e, _) -> s, e
                      | RichText.DetectedTag (s, e, _) -> s, e)

              ranges
              |> List.pairwise
              |> List.iter (fun ((_, e1), (s2, _)) -> Expect.isLessThanOrEqual e1 s2 "non-overlapping") ]
