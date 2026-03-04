module FSharp.ATProto.Moderation.Tests.ModerationTests

open Expecto
open FSharp.ATProto.Moderation

let private defaultPrefs = ModerationPrefs.Default

let private mkLabel src uri value =
    { Src = src; Uri = uri; Val = value; Neg = false; Cts = None }

let private mkNegLabel src uri value =
    { Src = src; Uri = uri; Val = value; Neg = true; Cts = None }

// ============================================================
// Labels.fs tests
// ============================================================

[<Tests>]
let labelDefinitionTests =
    testList "Labels - definitions" [
        test "builtInLabels has 8 labels" {
            Expect.equal (List.length Labels.builtInLabels) 8 "Should have 8 built-in labels"
        }

        test "findLabel returns !hide" {
            let label = Labels.findLabel "!hide"
            Expect.isSome label "Should find !hide"
            Expect.equal label.Value.Identifier "!hide" "Identifier should be !hide"
            Expect.equal label.Value.Severity LabelSeverity.Alert "Severity should be Alert"
            Expect.equal label.Value.Blurs LabelBlurs.Content "Blurs should be Content"
            Expect.equal label.Value.DefaultSetting LabelDefaultSetting.Hide "Default should be Hide"
            Expect.isFalse label.Value.Configurable "Should not be configurable"
        }

        test "findLabel returns !warn" {
            let label = Labels.findLabel "!warn"
            Expect.isSome label "Should find !warn"
            Expect.equal label.Value.DefaultSetting LabelDefaultSetting.Warn "Default should be Warn"
            Expect.isFalse label.Value.Configurable "Should not be configurable"
        }

        test "findLabel returns !no-unauthenticated" {
            let label = Labels.findLabel "!no-unauthenticated"
            Expect.isSome label "Should find !no-unauthenticated"
            Expect.isTrue (label.Value.Flags |> List.contains LabelFlag.Unauthed) "Should have Unauthed flag"
            Expect.isTrue (label.Value.Flags |> List.contains LabelFlag.NoOverride) "Should have NoOverride flag"
        }

        test "findLabel returns porn" {
            let label = Labels.findLabel "porn"
            Expect.isSome label "Should find porn"
            Expect.isTrue label.Value.AdultOnly "Should be adult-only"
            Expect.equal label.Value.Blurs LabelBlurs.Media "Blurs should be Media"
            Expect.equal label.Value.DefaultSetting LabelDefaultSetting.Hide "Default should be Hide"
            Expect.isTrue label.Value.Configurable "Should be configurable"
        }

        test "findLabel returns sexual" {
            let label = Labels.findLabel "sexual"
            Expect.isSome label "Should find sexual"
            Expect.isTrue label.Value.AdultOnly "Should be adult-only"
            Expect.equal label.Value.DefaultSetting LabelDefaultSetting.Warn "Default should be Warn"
        }

        test "findLabel returns nudity" {
            let label = Labels.findLabel "nudity"
            Expect.isSome label "Should find nudity"
            Expect.isFalse label.Value.AdultOnly "Should not be adult-only"
            Expect.equal label.Value.DefaultSetting LabelDefaultSetting.Ignore "Default should be Ignore"
        }

        test "findLabel returns graphic-media" {
            let label = Labels.findLabel "graphic-media"
            Expect.isSome label "Should find graphic-media"
            Expect.equal label.Value.DefaultSetting LabelDefaultSetting.Warn "Default should be Warn"
            Expect.isTrue (label.Value.Flags |> List.contains LabelFlag.Adult) "Should have Adult flag"
        }

        test "findLabel returns gore" {
            let label = Labels.findLabel "gore"
            Expect.isSome label "Should find gore"
            Expect.equal label.Value.DefaultSetting LabelDefaultSetting.Warn "Default should be Warn"
        }

        test "findLabel returns None for unknown" {
            let label = Labels.findLabel "unknown-label"
            Expect.isNone label "Should return None for unknown label"
        }

        test "isCustomLabelValue accepts lowercase-hyphen" {
            Expect.isTrue (Labels.isCustomLabelValue "my-custom-label") "Should accept lowercase-hyphen"
        }

        test "isCustomLabelValue rejects exclamation prefix" {
            Expect.isFalse (Labels.isCustomLabelValue "!hide") "Should reject ! prefix"
        }

        test "isCustomLabelValue rejects uppercase" {
            Expect.isFalse (Labels.isCustomLabelValue "MyLabel") "Should reject uppercase"
        }

        test "isCustomLabelValue rejects empty" {
            Expect.isFalse (Labels.isCustomLabelValue "") "Should reject empty string"
        }

        test "!hide has NoOverride flag" {
            let label = (Labels.findLabel "!hide").Value
            Expect.isTrue (label.Flags |> List.contains LabelFlag.NoOverride) "!hide should have NoOverride"
        }

        test "!hide has NoSelf flag" {
            let label = (Labels.findLabel "!hide").Value
            Expect.isTrue (label.Flags |> List.contains LabelFlag.NoSelf) "!hide should have NoSelf"
        }

        test "porn has Adult flag" {
            let label = (Labels.findLabel "porn").Value
            Expect.isTrue (label.Flags |> List.contains LabelFlag.Adult) "porn should have Adult flag"
        }

        test "nudity has no flags" {
            let label = (Labels.findLabel "nudity").Value
            Expect.isEmpty label.Flags "nudity should have no flags"
        }
    ]

// ============================================================
// Label negation
// ============================================================

[<Tests>]
let labelNegationTests =
    testList "Moderation - label negation" [
        test "negation cancels out matching label" {
            let labels =
                [ mkLabel "did:plc:labeler1" "at://post1" "porn"
                  mkNegLabel "did:plc:labeler1" "at://post1" "porn" ]

            let resolved = Moderation.resolveLabels labels
            Expect.isEmpty resolved "Negation should cancel the label"
        }

        test "negation only cancels from same source" {
            let labels =
                [ mkLabel "did:plc:labeler1" "at://post1" "porn"
                  mkNegLabel "did:plc:labeler2" "at://post1" "porn" ]

            let resolved = Moderation.resolveLabels labels
            Expect.hasLength resolved 1 "Should keep label from different source"
            Expect.equal resolved.[0].Val "porn" "Remaining label should be porn"
        }

        test "negation does not cancel different label value" {
            let labels =
                [ mkLabel "did:plc:labeler1" "at://post1" "porn"
                  mkNegLabel "did:plc:labeler1" "at://post1" "nudity" ]

            let resolved = Moderation.resolveLabels labels
            Expect.hasLength resolved 1 "Should keep label with different value"
        }

        test "empty labels returns empty" {
            let resolved = Moderation.resolveLabels []
            Expect.isEmpty resolved "Empty input should give empty output"
        }

        test "negation-only labels result in empty" {
            let labels = [ mkNegLabel "did:plc:labeler1" "at://post1" "porn" ]
            let resolved = Moderation.resolveLabels labels
            Expect.isEmpty resolved "Negation-only should result in empty"
        }
    ]

// ============================================================
// Label moderation
// ============================================================

[<Tests>]
let moderateLabelsTests =
    testList "Moderation - moderateLabels" [
        test "!hide label produces a cause" {
            let labels = [ mkLabel "did:plc:labeler1" "at://post1" "!hide" ]
            let causes = Moderation.moderateLabels defaultPrefs labels LabelTarget.Content None
            Expect.hasLength causes 1 "Should have one cause"
            Expect.equal causes.[0].Type ModerationCauseType.Label "Should be Label type"
            Expect.equal causes.[0].Description "!hide" "Description should be !hide"
        }

        test "!warn label produces a cause" {
            let labels = [ mkLabel "did:plc:labeler1" "at://post1" "!warn" ]
            let causes = Moderation.moderateLabels defaultPrefs labels LabelTarget.Content None
            Expect.hasLength causes 1 "Should have one cause"
            Expect.equal causes.[0].Description "!warn" "Description should be !warn"
        }

        test "!no-unauthenticated skipped for authenticated user" {
            let labels = [ mkLabel "did:plc:labeler1" "at://post1" "!no-unauthenticated" ]
            let causes = Moderation.moderateLabels defaultPrefs labels LabelTarget.Content (Some "did:plc:user1")
            Expect.isEmpty causes "Should skip !no-unauthenticated for authenticated user"
        }

        test "!no-unauthenticated applies for unauthenticated user" {
            let labels = [ mkLabel "did:plc:labeler1" "at://post1" "!no-unauthenticated" ]
            let causes = Moderation.moderateLabels defaultPrefs labels LabelTarget.Content None
            Expect.hasLength causes 1 "Should apply for unauthenticated user"
        }

        test "porn label with adult content disabled" {
            let labels = [ mkLabel "did:plc:labeler1" "at://post1" "porn" ]
            let causes = Moderation.moderateLabels defaultPrefs labels LabelTarget.Content None
            Expect.hasLength causes 1 "Should have one cause"
            Expect.equal causes.[0].Priority 1 "Priority should be 1 (forced hide)"
        }

        test "porn label with adult content enabled uses default hide" {
            let prefs = { defaultPrefs with AdultContentEnabled = true }
            let labels = [ mkLabel "did:plc:labeler1" "at://post1" "porn" ]
            let causes = Moderation.moderateLabels prefs labels LabelTarget.Content None
            Expect.hasLength causes 1 "Should have one cause"
            Expect.equal causes.[0].Priority 2 "Priority should be 2 (user hide)"
        }

        test "porn label with user preference Show is ignored" {
            let prefs =
                { defaultPrefs with
                    AdultContentEnabled = true
                    Labels = Map.ofList [ "porn", LabelVisibility.Show ] }

            let labels = [ mkLabel "did:plc:labeler1" "at://post1" "porn" ]
            let causes = Moderation.moderateLabels prefs labels LabelTarget.Content None
            Expect.isEmpty causes "Should ignore porn label when user set Show"
        }

        test "porn label with user preference Warn" {
            let prefs =
                { defaultPrefs with
                    AdultContentEnabled = true
                    Labels = Map.ofList [ "porn", LabelVisibility.Warn ] }

            let labels = [ mkLabel "did:plc:labeler1" "at://post1" "porn" ]
            let causes = Moderation.moderateLabels prefs labels LabelTarget.Content None
            Expect.hasLength causes 1 "Should have one cause"
        }

        test "nudity label with default ignore is skipped" {
            let prefs = { defaultPrefs with AdultContentEnabled = true }
            let labels = [ mkLabel "did:plc:labeler1" "at://post1" "nudity" ]
            let causes = Moderation.moderateLabels prefs labels LabelTarget.Content None
            Expect.isEmpty causes "nudity default is Ignore, should be skipped"
        }

        test "nudity label with user preference Warn" {
            let prefs =
                { defaultPrefs with
                    AdultContentEnabled = true
                    Labels = Map.ofList [ "nudity", LabelVisibility.Warn ] }

            let labels = [ mkLabel "did:plc:labeler1" "at://post1" "nudity" ]
            let causes = Moderation.moderateLabels prefs labels LabelTarget.Content None
            Expect.hasLength causes 1 "Should have one cause when user set Warn"
        }

        test "unknown label is ignored" {
            let labels = [ mkLabel "did:plc:labeler1" "at://post1" "totally-unknown" ]
            let causes = Moderation.moderateLabels defaultPrefs labels LabelTarget.Content None
            Expect.isEmpty causes "Unknown labels should be ignored"
        }

        test "self-label with NoSelf flag is skipped" {
            let labels = [ mkLabel "did:plc:me" "at://post1" "!hide" ]
            let causes = Moderation.moderateLabels defaultPrefs labels LabelTarget.Content (Some "did:plc:me")
            Expect.isEmpty causes "Self-label with NoSelf should be skipped"
        }

        test "self-label without NoSelf flag is kept" {
            let labels = [ mkLabel "did:plc:me" "at://post1" "porn" ]
            let causes = Moderation.moderateLabels defaultPrefs labels LabelTarget.Content (Some "did:plc:me")
            Expect.hasLength causes 1 "Self-label without NoSelf should be kept"
        }

        test "multiple labels produce multiple causes sorted by priority" {
            let labels =
                [ mkLabel "did:plc:labeler1" "at://post1" "!hide"
                  mkLabel "did:plc:labeler1" "at://post1" "porn" ]

            let causes = Moderation.moderateLabels defaultPrefs labels LabelTarget.Content None
            Expect.hasLength causes 2 "Should have two causes"
            // !hide priority = 1, porn priority = 1 (adult disabled)
            Expect.equal causes.[0].Priority 1 "First should be priority 1"
        }
    ]

// ============================================================
// Muted words moderation
// ============================================================

[<Tests>]
let moderateMutedWordsTests =
    testList "Moderation - moderateMutedWords" [
        test "matching muted word produces cause" {
            let words =
                [ { Value = "test"
                    Targets = [ MutedWordTarget.Content ]
                    ActorTarget = Some "all"
                    ExpiresAt = None } ]

            let causes = Moderation.moderateMutedWords words "this is a test" [] []
            Expect.hasLength causes 1 "Should have one cause"
            Expect.equal causes.[0].Type ModerationCauseType.MuteWord "Should be MuteWord type"
            Expect.equal causes.[0].Priority 6 "Priority should be 6"
        }

        test "no match produces empty" {
            let words =
                [ { Value = "missing"
                    Targets = [ MutedWordTarget.Content ]
                    ActorTarget = Some "all"
                    ExpiresAt = None } ]

            let causes = Moderation.moderateMutedWords words "nothing here" [] []
            Expect.isEmpty causes "No match should produce empty"
        }
    ]

// ============================================================
// Full moderation decisions
// ============================================================

[<Tests>]
let moderateBlockingTests =
    testList "Moderation - blocking" [
        test "blocked user in content list is filtered" {
            let decision =
                Moderation.moderate
                    defaultPrefs [] None [] [] false true false false false
                    LabelTarget.Content None ModerationContext.ContentList

            Expect.isTrue decision.Filter "Blocked user should be filtered in list"
            Expect.isTrue decision.Blur "Blocked user should be blurred"
            Expect.isTrue decision.NoOverride "Block blur should be no-override"
        }

        test "blocked user in content view is blurred" {
            let decision =
                Moderation.moderate
                    defaultPrefs [] None [] [] false true false false false
                    LabelTarget.Content None ModerationContext.ContentView

            Expect.isFalse decision.Filter "Blocked user should not be filtered in view"
            Expect.isTrue decision.Blur "Blocked user should be blurred in view"
        }

        test "blocked user in profile list is filtered and blurred" {
            let decision =
                Moderation.moderate
                    defaultPrefs [] None [] [] false true false false false
                    LabelTarget.Account None ModerationContext.ProfileList

            Expect.isTrue decision.Filter "Blocked user should be filtered in profile list"
            Expect.isTrue decision.Blur "Blocked user should be blurred in profile list"
        }

        test "blocked user in profile view shows alert" {
            let decision =
                Moderation.moderate
                    defaultPrefs [] None [] [] false true false false false
                    LabelTarget.Account None ModerationContext.ProfileView

            Expect.isFalse decision.Filter "Should not filter in profile view"
            Expect.isTrue decision.Alert "Should show alert in profile view"
        }

        test "blocked-by user in content list is filtered" {
            let decision =
                Moderation.moderate
                    defaultPrefs [] None [] [] false false true false false
                    LabelTarget.Content None ModerationContext.ContentList

            Expect.isTrue decision.Filter "Blocked-by should be filtered in list"
        }

        test "blocking does not apply to self" {
            let decision =
                Moderation.moderate
                    defaultPrefs [] None [] [] false true false false true
                    LabelTarget.Content None ModerationContext.ContentList

            Expect.isFalse decision.Filter "Blocking should not apply to self"
            Expect.isFalse decision.Blur "Blocking should not blur self"
        }
    ]

[<Tests>]
let moderateMutedTests =
    testList "Moderation - muted" [
        test "muted user in content list is filtered and blurred" {
            let decision =
                Moderation.moderate
                    defaultPrefs [] None [] [] true false false false false
                    LabelTarget.Content None ModerationContext.ContentList

            Expect.isTrue decision.Filter "Muted user should be filtered in list"
            Expect.isTrue decision.Blur "Muted user should be blurred in content list"
        }

        test "muted user in content view shows inform" {
            let decision =
                Moderation.moderate
                    defaultPrefs [] None [] [] true false false false false
                    LabelTarget.Content None ModerationContext.ContentView

            Expect.isFalse decision.Filter "Muted user should not be filtered in view"
            Expect.isTrue decision.Inform "Muted user should show inform in view"
        }

        test "muted user in profile list shows inform" {
            let decision =
                Moderation.moderate
                    defaultPrefs [] None [] [] true false false false false
                    LabelTarget.Account None ModerationContext.ProfileList

            Expect.isTrue decision.Filter "Muted user should be filtered in profile list"
            Expect.isTrue decision.Inform "Muted user should show inform in profile list"
        }

        test "muted user in profile view shows alert" {
            let decision =
                Moderation.moderate
                    defaultPrefs [] None [] [] true false false false false
                    LabelTarget.Account None ModerationContext.ProfileView

            Expect.isTrue decision.Alert "Muted user should show alert in profile view"
        }

        test "muted does not apply to self" {
            let decision =
                Moderation.moderate
                    defaultPrefs [] None [] [] true false false false true
                    LabelTarget.Content None ModerationContext.ContentList

            Expect.isFalse decision.Filter "Muted should not filter self"
        }
    ]

[<Tests>]
let moderateHiddenTests =
    testList "Moderation - hidden posts" [
        test "hidden post in content list is filtered and blurred" {
            let decision =
                Moderation.moderate
                    defaultPrefs [] None [] [] false false false true false
                    LabelTarget.Content None ModerationContext.ContentList

            Expect.isTrue decision.Filter "Hidden post should be filtered in list"
            Expect.isTrue decision.Blur "Hidden post should be blurred"
        }

        test "hidden post in content view is blurred" {
            let decision =
                Moderation.moderate
                    defaultPrefs [] None [] [] false false false true false
                    LabelTarget.Content None ModerationContext.ContentView

            Expect.isFalse decision.Filter "Hidden post should not be filtered in view"
            Expect.isTrue decision.Blur "Hidden post should be blurred in view"
        }
    ]

[<Tests>]
let moderateMuteWordIntegrationTests =
    testList "Moderation - mute words integration" [
        test "muted word in content list filters" {
            let prefs =
                { defaultPrefs with
                    MutedWords =
                        [ { Value = "javascript"
                            Targets = [ MutedWordTarget.Content ]
                            ActorTarget = Some "all"
                            ExpiresAt = None } ] }

            let decision =
                Moderation.moderate
                    prefs [] (Some "This is about javascript") [] [] false false false false false
                    LabelTarget.Content None ModerationContext.ContentList

            Expect.isTrue decision.Filter "Muted word should filter in content list"
        }

        test "muted word in content view blurs" {
            let prefs =
                { defaultPrefs with
                    MutedWords =
                        [ { Value = "javascript"
                            Targets = [ MutedWordTarget.Content ]
                            ActorTarget = Some "all"
                            ExpiresAt = None } ] }

            let decision =
                Moderation.moderate
                    prefs [] (Some "This is about javascript") [] [] false false false false false
                    LabelTarget.Content None ModerationContext.ContentView

            Expect.isTrue decision.Blur "Muted word should blur in content view"
        }

        test "muted word does not apply to self" {
            let prefs =
                { defaultPrefs with
                    MutedWords =
                        [ { Value = "javascript"
                            Targets = [ MutedWordTarget.Content ]
                            ActorTarget = Some "all"
                            ExpiresAt = None } ] }

            let decision =
                Moderation.moderate
                    prefs [] (Some "This is about javascript") [] [] false false false false true
                    LabelTarget.Content None ModerationContext.ContentList

            Expect.isFalse decision.Filter "Muted word should not apply to self"
        }

        test "no text means no muted word check" {
            let prefs =
                { defaultPrefs with
                    MutedWords =
                        [ { Value = "test"
                            Targets = [ MutedWordTarget.Content ]
                            ActorTarget = Some "all"
                            ExpiresAt = None } ] }

            let decision =
                Moderation.moderate
                    prefs [] None [] [] false false false false false
                    LabelTarget.Content None ModerationContext.ContentList

            Expect.isFalse decision.Filter "No text should mean no mute word check"
        }
    ]

[<Tests>]
let moderateLabelIntegrationTests =
    testList "Moderation - label integration" [
        test "!hide label in content list blurs" {
            let labels = [ mkLabel "did:plc:labeler1" "at://post1" "!hide" ]

            let decision =
                Moderation.moderateContent defaultPrefs labels ModerationContext.ContentList

            Expect.isTrue decision.Blur "!hide should blur in content list"
        }

        test "!hide label in content view blurs" {
            let labels = [ mkLabel "did:plc:labeler1" "at://post1" "!hide" ]

            let decision =
                Moderation.moderateContent defaultPrefs labels ModerationContext.ContentView

            Expect.isTrue decision.Blur "!hide should blur in content view"
        }

        test "!warn label in content list blurs" {
            let labels = [ mkLabel "did:plc:labeler1" "at://post1" "!warn" ]

            let decision =
                Moderation.moderateContent defaultPrefs labels ModerationContext.ContentList

            Expect.isTrue decision.Blur "!warn should blur in content list"
        }

        test "porn label with adult disabled blurs media" {
            let labels = [ mkLabel "did:plc:labeler1" "at://post1" "porn" ]

            let decision =
                Moderation.moderateContent defaultPrefs labels ModerationContext.ContentMedia

            Expect.isTrue decision.Blur "porn with adult disabled should blur media"
            Expect.isTrue decision.NoOverride "porn with adult disabled should be no-override"
        }
    ]

[<Tests>]
let moderateDecisionPropertyTests =
    testList "Moderation - decision properties" [
        test "None decision has no action" {
            let d = ModerationDecision.None
            Expect.equal d.Action ModerationAction.NoAction "None should have NoAction"
            Expect.isNone d.PrimaryCause "None should have no primary cause"
        }

        test "Action priority: Filter > Blur > Alert > Inform" {
            let decision =
                Moderation.moderate
                    defaultPrefs [] None [] [] true true false false false
                    LabelTarget.Content None ModerationContext.ContentList

            // Both muted and blocked: should have both Filter and Blur
            Expect.isTrue decision.Filter "Should have filter"
            Expect.isTrue decision.Blur "Should have blur"
            Expect.equal decision.Action ModerationAction.Filter "Action should be Filter (highest priority)"
        }

        test "causes are sorted by priority" {
            let labels = [ mkLabel "did:plc:labeler1" "at://post1" "!hide" ]

            let prefs =
                { defaultPrefs with
                    MutedWords =
                        [ { Value = "test"
                            Targets = [ MutedWordTarget.Content ]
                            ActorTarget = Some "all"
                            ExpiresAt = None } ] }

            let decision =
                Moderation.moderate
                    prefs labels (Some "test content") [] [] true false false false false
                    LabelTarget.Content None ModerationContext.ContentList

            // Should have label (1), muted (6), mute-word (6)
            Expect.isNonEmpty decision.Causes "Should have causes"

            let priorities = decision.Causes |> List.map (fun c -> c.Priority)

            let isSorted =
                priorities
                |> List.pairwise
                |> List.forall (fun (a, b) -> a <= b)

            Expect.isTrue isSorted "Causes should be sorted by priority"
        }
    ]

[<Tests>]
let moderateSimplifiedTests =
    testList "Moderation - simplified helpers" [
        test "moderateContent with no labels returns no action" {
            let decision =
                Moderation.moderateContent defaultPrefs [] ModerationContext.ContentList

            Expect.equal decision.Action ModerationAction.NoAction "No labels should mean no action"
        }

        test "moderateProfile with muted user" {
            let decision =
                Moderation.moderateProfile defaultPrefs [] true false false false ModerationContext.ProfileList

            Expect.isTrue decision.Filter "Muted user should be filtered in profile list"
        }

        test "moderateProfile for self is not affected by mute" {
            let decision =
                Moderation.moderateProfile defaultPrefs [] true false false true ModerationContext.ProfileList

            Expect.isFalse decision.Filter "Self should not be filtered even if muted"
        }

        test "moderatePost with muted word" {
            let prefs =
                { defaultPrefs with
                    MutedWords =
                        [ { Value = "secret"
                            Targets = [ MutedWordTarget.Content ]
                            ActorTarget = Some "all"
                            ExpiresAt = None } ] }

            let decision =
                Moderation.moderatePost prefs [] "This is a secret message" [] [] false false None ModerationContext.ContentList

            Expect.isTrue decision.Filter "Post with muted word should be filtered"
        }

        test "moderatePost for own post ignores muted word" {
            let prefs =
                { defaultPrefs with
                    MutedWords =
                        [ { Value = "secret"
                            Targets = [ MutedWordTarget.Content ]
                            ActorTarget = Some "all"
                            ExpiresAt = None } ] }

            let decision =
                Moderation.moderatePost prefs [] "This is a secret message" [] [] false true None ModerationContext.ContentList

            Expect.isFalse decision.Filter "Own post should not be filtered by muted word"
        }
    ]

[<Tests>]
let moderateContextTests =
    testList "Moderation - context-specific behaviors" [
        test "avatar context: block blurs avatar" {
            let decision =
                Moderation.moderate
                    defaultPrefs [] None [] [] false true false false false
                    LabelTarget.Account None ModerationContext.Avatar

            Expect.isTrue decision.Blur "Block should blur avatar"
        }

        test "banner context: block blurs banner" {
            let decision =
                Moderation.moderate
                    defaultPrefs [] None [] [] false true false false false
                    LabelTarget.Account None ModerationContext.Banner

            Expect.isTrue decision.Blur "Block should blur banner"
        }

        test "display name context: muted user has no blur" {
            let decision =
                Moderation.moderate
                    defaultPrefs [] None [] [] true false false false false
                    LabelTarget.Account None ModerationContext.DisplayName

            Expect.isFalse decision.Blur "Muted user should not blur display name"
        }

        test "content media context: muted word no effect" {
            let prefs =
                { defaultPrefs with
                    MutedWords =
                        [ { Value = "test"
                            Targets = [ MutedWordTarget.Content ]
                            ActorTarget = Some "all"
                            ExpiresAt = None } ] }

            let decision =
                Moderation.moderate
                    prefs [] (Some "test content") [] [] false false false false false
                    LabelTarget.Content None ModerationContext.ContentMedia

            // Mute word behavior only applies to contentList and contentView
            Expect.isFalse decision.Blur "Mute word should not blur content media"
        }
    ]

[<Tests>]
let moderateCombinedTests =
    testList "Moderation - combined causes" [
        test "blocked + labeled user combines effects" {
            let labels = [ mkLabel "did:plc:labeler1" "at://user1" "!warn" ]

            let decision =
                Moderation.moderate
                    defaultPrefs labels None [] [] false true false false false
                    LabelTarget.Content None ModerationContext.ContentList

            Expect.isTrue decision.Filter "Should be filtered"
            Expect.isTrue decision.Blur "Should be blurred"
            Expect.isNonEmpty decision.Causes "Should have multiple causes"
        }

        test "muted + hidden post combines effects" {
            let decision =
                Moderation.moderate
                    defaultPrefs [] None [] [] true false false true false
                    LabelTarget.Content None ModerationContext.ContentList

            Expect.isTrue decision.Filter "Should be filtered"
            Expect.isTrue decision.Blur "Should be blurred"

            let causeTypes = decision.Causes |> List.map (fun c -> c.Type)
            Expect.contains causeTypes ModerationCauseType.Muted "Should have muted cause"
            Expect.contains causeTypes ModerationCauseType.Hidden "Should have hidden cause"
        }
    ]
