module FSharp.ATProto.Moderation.Tests.MutedWordsTests

open System
open Expecto
open FSharp.ATProto.Moderation

let private mkMutedWord value targets =
    { Value = value
      Targets = targets
      ActorTarget = Some "all"
      ExpiresAt = None }

let private contentWord value =
    mkMutedWord value [ MutedWordTarget.Content ]

let private tagWord value =
    mkMutedWord value [ MutedWordTarget.Tag ]

let private contentAndTagWord value =
    mkMutedWord value [ MutedWordTarget.Content; MutedWordTarget.Tag ]

[<Tests>]
let tagTests =
    testList "MutedWords - tags" [
        test "match: outline tag" {
            let mw = tagWord "outlineTag"
            let result = MutedWords.matchesMutedWord mw "This is a post" [ "outlineTag" ] []
            Expect.isTrue result "Should match outline tag"
        }

        test "match: tag is case-insensitive" {
            let mw = tagWord "OutlineTag"
            let result = MutedWords.matchesMutedWord mw "This is a post" [ "outlinetag" ] []
            Expect.isTrue result "Should match tag case-insensitively"
        }

        test "match: content target also matches tags" {
            let mw = contentWord "myTag"
            let result = MutedWords.matchesMutedWord mw "This is a post" [ "myTag" ] []
            Expect.isTrue result "Content target should also match tags"
        }

        test "no match: tag target only, word not in tags" {
            let mw = tagWord "missing"
            let result = MutedWords.matchesMutedWord mw "This is a post about missing things" [] []
            Expect.isFalse result "Tag-only target should not match text content"
        }

        test "no match: empty tags list" {
            let mw = tagWord "something"
            let result = MutedWords.matchesMutedWord mw "something" [] []
            Expect.isFalse result "Tag-only target should not match text when no tags"
        }

        test "match: content+tag word matches via tag" {
            let mw = contentAndTagWord "hello"
            let result = MutedWords.matchesMutedWord mw "unrelated text" [ "hello" ] []
            Expect.isTrue result "Content+tag target should match via tag"
        }

        test "match: content+tag word matches via content" {
            let mw = contentAndTagWord "hello"
            let result = MutedWords.matchesMutedWord mw "say hello there" [] []
            Expect.isTrue result "Content+tag target should match via content"
        }
    ]

[<Tests>]
let earlyExitTests =
    testList "MutedWords - early exits" [
        test "match: single character" {
            let mw = contentWord "x"
            let result = MutedWords.matchesMutedWord mw "fox" [] []
            Expect.isTrue result "Single character should use contains"
        }

        test "no match: long muted word, short post" {
            let mw = contentWord "politics"
            let result = MutedWords.matchesMutedWord mw "hey" [] []
            Expect.isFalse result "Should not match when muted word is longer than post"
        }

        test "match: exact text" {
            let mw = contentWord "javascript"
            let result = MutedWords.matchesMutedWord mw "javascript" [] []
            Expect.isTrue result "Should match exact text"
        }

        test "match: exact text case-insensitive" {
            let mw = contentWord "JavaScript"
            let result = MutedWords.matchesMutedWord mw "javascript" [] []
            Expect.isTrue result "Should match exact text case-insensitively"
        }
    ]

[<Tests>]
let generalContentTests =
    testList "MutedWords - general content" [
        test "match: word within post" {
            let mw = contentWord "javascript"
            let result = MutedWords.matchesMutedWord mw "This is a post about javascript" [] []
            Expect.isTrue result "Should match word within post"
        }

        test "no match: partial word" {
            let mw = contentWord "ai"
            let result = MutedWords.matchesMutedWord mw "Use your brain, Eric" [] []
            Expect.isFalse result "Should not match partial word"
        }

        test "match: multiline text" {
            let mw = contentWord "brain"
            let result = MutedWords.matchesMutedWord mw "Use your\n\tbrain, Eric" [] []
            Expect.isTrue result "Should match across whitespace/newlines"
        }

        test "match: emoji" {
            let mw = contentWord ":)"
            let result = MutedWords.matchesMutedWord mw "So happy :)" [] []
            Expect.isTrue result "Should match :) emoticon"
        }
    ]

[<Tests>]
let punctuationTests =
    testList "MutedWords - punctuation" [
        test "match: yay! against yay!" {
            let mw = contentWord "yay!"
            let result = MutedWords.matchesMutedWord mw "We're federating, yay!" [] []
            Expect.isTrue result "Should match word with punctuation"
        }

        test "match: yay against yay!" {
            let mw = contentWord "yay"
            let result = MutedWords.matchesMutedWord mw "We're federating, yay!" [] []
            Expect.isTrue result "Should match word ignoring trailing punctuation"
        }

        test "match: !command" {
            let mw = contentWord "!command"
            let result = MutedWords.matchesMutedWord mw "Idk maybe a bot !command" [] []
            Expect.isTrue result "Should match !command"
        }

        test "match: command against !command in text" {
            let mw = contentWord "command"
            let result = MutedWords.matchesMutedWord mw "Idk maybe a bot !command" [] []
            Expect.isTrue result "Should match command stripping leading punctuation"
        }

        test "no match: !command against text without !" {
            let mw = contentWord "!command"
            let result = MutedWords.matchesMutedWord mw "Idk maybe a bot command" [] []
            Expect.isFalse result "Should not match !command when ! is missing"
        }

        test "match: and/or" {
            let mw = contentWord "and/or"
            let result = MutedWords.matchesMutedWord mw "Tomatoes are fruits and/or vegetables" [] []
            Expect.isTrue result "Should match and/or as phrase with punctuation"
        }

        test "no match: Andor against and/or" {
            let mw = contentWord "Andor"
            let result = MutedWords.matchesMutedWord mw "Tomatoes are fruits and/or vegetables" [] []
            Expect.isFalse result "Should not match Andor against and/or (slash is a separator)"
        }

        test "match: super-bad" {
            let mw = contentWord "super-bad"
            let result = MutedWords.matchesMutedWord mw "I'm super-bad" [] []
            Expect.isTrue result "Should match hyphenated word"
        }

        test "match: super against super-bad" {
            let mw = contentWord "super"
            let result = MutedWords.matchesMutedWord mw "I'm super-bad" [] []
            Expect.isTrue result "Should match first part of hyphenated word"
        }

        test "match: bad against super-bad" {
            let mw = contentWord "bad"
            let result = MutedWords.matchesMutedWord mw "I'm super-bad" [] []
            Expect.isTrue result "Should match second part of hyphenated word"
        }

        test "match: super bad against super-bad" {
            let mw = contentWord "super bad"
            let result = MutedWords.matchesMutedWord mw "I'm super-bad" [] []
            Expect.isTrue result "Should match spaced version against hyphenated"
        }

        test "match: superbad against super-bad" {
            let mw = contentWord "superbad"
            let result = MutedWords.matchesMutedWord mw "I'm super-bad" [] []
            Expect.isTrue result "Should match contiguous version against hyphenated"
        }

        test "match: Bluesky's" {
            let mw = contentWord "Bluesky's"
            let result = MutedWords.matchesMutedWord mw "Yay, Bluesky's mutewords work" [] []
            Expect.isTrue result "Should match word with apostrophe"
        }

        test "match: Bluesky against Bluesky's" {
            let mw = contentWord "Bluesky"
            let result = MutedWords.matchesMutedWord mw "Yay, Bluesky's mutewords work" [] []
            Expect.isTrue result "Should match word part before apostrophe"
        }

        test "match: context against context(iykyk)" {
            let mw = contentWord "context"
            let result = MutedWords.matchesMutedWord mw "Post with context(iykyk)" [] []
            Expect.isTrue result "Should match word before parentheses"
        }

        test "match: iykyk against context(iykyk)" {
            let mw = contentWord "iykyk"
            let result = MutedWords.matchesMutedWord mw "Post with context(iykyk)" [] []
            Expect.isTrue result "Should match word inside parentheses"
        }
    ]

[<Tests>]
let phraseTests =
    testList "MutedWords - phrases" [
        test "match: phrase within text" {
            let mw = contentWord "stop worrying"
            let result =
                MutedWords.matchesMutedWord
                    mw
                    "I like turtles, or how I learned to stop worrying and love the internet."
                    []
                    []
            Expect.isTrue result "Should match phrase within text"
        }

        test "match: phrase with punctuation" {
            let mw = contentWord "turtles, or how"
            let result =
                MutedWords.matchesMutedWord
                    mw
                    "I like turtles, or how I learned to stop worrying and love the internet."
                    []
                    []
            Expect.isTrue result "Should match phrase containing punctuation"
        }

        test "match: new york times case-insensitive" {
            let mw = contentWord "new york times"
            let result = MutedWords.matchesMutedWord mw "New York Times" [] []
            Expect.isTrue result "Should match multi-word phrase case-insensitively"
        }
    ]

[<Tests>]
let languageExceptionTests =
    testList "MutedWords - language exceptions" [
        test "match: Japanese text with language hint" {
            let mw = contentWord "インターネット"
            let result =
                MutedWords.matchesMutedWord
                    mw
                    "私はカメが好きです、またはどのようにして心配するのをやめてインターネットを愛するようになったのか"
                    []
                    [ "ja" ]
            Expect.isTrue result "Should match Japanese text with language exception"
        }

        test "match: Chinese text with language hint" {
            let mw = contentWord "你好"
            let result = MutedWords.matchesMutedWord mw "大家你好世界" [] [ "zh" ]
            Expect.isTrue result "Should match Chinese text with language exception"
        }

        test "match: Korean text with language hint" {
            let mw = contentWord "안녕"
            let result = MutedWords.matchesMutedWord mw "안녕하세요" [] [ "ko" ]
            Expect.isTrue result "Should match Korean text with language exception"
        }
    ]

[<Tests>]
let expirationTests =
    testList "MutedWords - expiration" [
        test "expired word is ignored" {
            let past = DateTimeOffset.UtcNow.AddSeconds(-60.0).ToString("o")

            let mw =
                { Value = "test"
                  Targets = [ MutedWordTarget.Content ]
                  ActorTarget = Some "all"
                  ExpiresAt = Some past }

            let result = MutedWords.matchesMutedWord mw "this is a test" [] []
            Expect.isFalse result "Expired word should not match"
        }

        test "non-expired word matches" {
            let future = DateTimeOffset.UtcNow.AddSeconds(60.0).ToString("o")

            let mw =
                { Value = "test"
                  Targets = [ MutedWordTarget.Content ]
                  ActorTarget = Some "all"
                  ExpiresAt = Some future }

            let result = MutedWords.matchesMutedWord mw "this is a test" [] []
            Expect.isTrue result "Non-expired word should match"
        }

        test "no expiration always matches" {
            let mw = contentWord "test"
            let result = MutedWords.matchesMutedWord mw "this is a test" [] []
            Expect.isTrue result "Word with no expiration should match"
        }

        test "isExpired returns true for past date" {
            let past = DateTimeOffset.UtcNow.AddSeconds(-60.0).ToString("o")

            let mw =
                { Value = "test"
                  Targets = [ MutedWordTarget.Content ]
                  ActorTarget = Some "all"
                  ExpiresAt = Some past }

            Expect.isTrue (MutedWords.isExpired mw) "Should be expired"
        }

        test "isExpired returns false for future date" {
            let future = DateTimeOffset.UtcNow.AddSeconds(60.0).ToString("o")

            let mw =
                { Value = "test"
                  Targets = [ MutedWordTarget.Content ]
                  ActorTarget = Some "all"
                  ExpiresAt = Some future }

            Expect.isFalse (MutedWords.isExpired mw) "Should not be expired"
        }

        test "isExpired returns false for None" {
            let mw = contentWord "test"
            Expect.isFalse (MutedWords.isExpired mw) "No expiration should not be expired"
        }
    ]

[<Tests>]
let hasMutedWordTests =
    testList "MutedWords - hasMutedWord" [
        test "returns true when any word matches" {
            let words = [ contentWord "cats"; contentWord "dogs" ]
            let result = MutedWords.hasMutedWord words "I love dogs" [] []
            Expect.isTrue result "Should return true when any word matches"
        }

        test "returns false when no words match" {
            let words = [ contentWord "cats"; contentWord "dogs" ]
            let result = MutedWords.hasMutedWord words "I love birds" [] []
            Expect.isFalse result "Should return false when no words match"
        }

        test "returns false for empty word list" {
            let result = MutedWords.hasMutedWord [] "some text" [] []
            Expect.isFalse result "Should return false for empty list"
        }

        test "matches first word in list" {
            let words = [ contentWord "first"; contentWord "second" ]
            let result = MutedWords.hasMutedWord words "the first item" [] []
            Expect.isTrue result "Should match first word"
        }

        test "matches last word in list" {
            let words = [ contentWord "first"; contentWord "second" ]
            let result = MutedWords.hasMutedWord words "the second item" [] []
            Expect.isTrue result "Should match last word"
        }
    ]

[<Tests>]
let unicodeTests =
    testList "MutedWords - unicode" [
        test "match: emoji in text" {
            let mw = contentWord "🦋"
            let result = MutedWords.matchesMutedWord mw "Post with 🦋" [] []
            Expect.isTrue result "Should match emoji"
        }

        test "match: accented characters" {
            let mw = contentWord "café"
            let result = MutedWords.matchesMutedWord mw "Let's go to the café" [] []
            Expect.isTrue result "Should match accented characters"
        }
    ]

[<Tests>]
let underscoreTests =
    testList "MutedWords - underscores" [
        test "match: idk against idk_what_this_would_be" {
            let mw = contentWord "idk"
            let result = MutedWords.matchesMutedWord mw "Weird post with idk_what_this_would_be" [] []
            Expect.isTrue result "Should match part of underscore-separated word"
        }

        test "match: idk what this would be against underscored" {
            let mw = contentWord "idk what this would be"
            let result = MutedWords.matchesMutedWord mw "Weird post with idk_what_this_would_be" [] []
            Expect.isTrue result "Should match spaced phrase against underscored"
        }

        test "match: idkwhatthiswouldbe against underscored" {
            let mw = contentWord "idkwhatthiswouldbe"
            let result = MutedWords.matchesMutedWord mw "Weird post with idk_what_this_would_be" [] []
            Expect.isTrue result "Should match contiguous against underscored"
        }
    ]
