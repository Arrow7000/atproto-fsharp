module FSharp.ATProto.Moderation.Tests.CustomLabelerTests

open Expecto
open FSharp.ATProto.Moderation

let private defaultPrefs = ModerationPrefs.Default

let private mkLabel src uri value =
    { Src = src; Uri = uri; Val = value; Neg = false; Cts = None }

// ============================================================
// interpretLabelValueDefinition
// ============================================================

[<Tests>]
let interpretLabelValueDefinitionTests =
    testList "Labels - interpretLabelValueDefinition" [

        test "content blur with alert severity" {
            let def =
                { Identifier = "spoiler"
                  Blurs = "content"
                  Severity = "alert"
                  DefaultSetting = None
                  AdultOnly = false }

            let result = Labels.interpretLabelValueDefinition def (Some "did:plc:labeler1")

            Expect.equal result.Identifier "spoiler" "Identifier"
            Expect.isTrue result.Configurable "Custom labels are always configurable"
            Expect.equal result.Severity LabelSeverity.Alert "Severity should be Alert"
            Expect.equal result.Blurs LabelBlurs.Content "Blurs should be Content"
            Expect.equal result.DefaultSetting LabelDefaultSetting.Warn "Default should be Warn"
            Expect.isFalse result.AdultOnly "Should not be adult-only"
            Expect.equal result.DefinedBy (Some "did:plc:labeler1") "Should track labeler DID"
            Expect.isTrue (result.Flags |> List.contains LabelFlag.NoSelf) "Should have NoSelf flag"
            Expect.isFalse (result.Flags |> List.contains LabelFlag.Adult) "Should not have Adult flag"
        }

        test "content blur: account behaviors" {
            let def =
                { Identifier = "spoiler"
                  Blurs = "content"
                  Severity = "alert"
                  DefaultSetting = None
                  AdultOnly = false }

            let result = Labels.interpretLabelValueDefinition def None
            let account = result.Behaviors.Account

            Expect.equal account.ProfileList (Some "alert") "Account.ProfileList"
            Expect.equal account.ProfileView (Some "alert") "Account.ProfileView"
            Expect.equal account.ContentList (Some "blur") "Account.ContentList"
            Expect.equal account.ContentView (Some "alert") "Account.ContentView (non-adult)"
            Expect.isNone account.Avatar "Account.Avatar should be None"
            Expect.isNone account.Banner "Account.Banner should be None"
        }

        test "content blur: adult-only content view is blur" {
            let def =
                { Identifier = "adult-spoiler"
                  Blurs = "content"
                  Severity = "alert"
                  DefaultSetting = None
                  AdultOnly = true }

            let result = Labels.interpretLabelValueDefinition def None
            let account = result.Behaviors.Account

            Expect.equal account.ContentView (Some "blur") "Adult content should blur content view"
            Expect.isTrue result.AdultOnly "Should be adult-only"
            Expect.isTrue (result.Flags |> List.contains LabelFlag.Adult) "Should have Adult flag"
        }

        test "content blur: content behaviors" {
            let def =
                { Identifier = "spoiler"
                  Blurs = "content"
                  Severity = "inform"
                  DefaultSetting = None
                  AdultOnly = false }

            let result = Labels.interpretLabelValueDefinition def None
            let content = result.Behaviors.Content

            Expect.equal content.ContentList (Some "blur") "Content.ContentList"
            Expect.equal content.ContentView (Some "inform") "Content.ContentView"
            Expect.isNone content.Avatar "Content.Avatar should be None"
        }

        test "media blur: account behaviors" {
            let def =
                { Identifier = "nsfw-media"
                  Blurs = "media"
                  Severity = "alert"
                  DefaultSetting = None
                  AdultOnly = false }

            let result = Labels.interpretLabelValueDefinition def None
            let account = result.Behaviors.Account

            Expect.equal account.ProfileList (Some "alert") "Account.ProfileList"
            Expect.equal account.ProfileView (Some "alert") "Account.ProfileView"
            Expect.equal account.Avatar (Some "blur") "Account.Avatar should blur"
            Expect.equal account.Banner (Some "blur") "Account.Banner should blur"
            Expect.isNone account.ContentList "Account.ContentList should be None for media blur"
        }

        test "media blur: content behaviors" {
            let def =
                { Identifier = "nsfw-media"
                  Blurs = "media"
                  Severity = "alert"
                  DefaultSetting = None
                  AdultOnly = false }

            let result = Labels.interpretLabelValueDefinition def None
            let content = result.Behaviors.Content

            Expect.equal content.ContentMedia (Some "blur") "Content.ContentMedia should blur"
            Expect.isNone content.ContentList "Content.ContentList should be None for media blur"
            Expect.isNone content.ContentView "Content.ContentView should be None for media blur"
        }

        test "media blur: profile behaviors" {
            let def =
                { Identifier = "nsfw-media"
                  Blurs = "media"
                  Severity = "inform"
                  DefaultSetting = None
                  AdultOnly = false }

            let result = Labels.interpretLabelValueDefinition def None
            let profile = result.Behaviors.Profile

            Expect.equal profile.ProfileList (Some "inform") "Profile.ProfileList"
            Expect.equal profile.ProfileView (Some "inform") "Profile.ProfileView"
            Expect.equal profile.Avatar (Some "blur") "Profile.Avatar should blur"
            Expect.equal profile.Banner (Some "blur") "Profile.Banner should blur"
        }

        test "none blur: spreads alert/inform to all contexts" {
            let def =
                { Identifier = "informational"
                  Blurs = "none"
                  Severity = "alert"
                  DefaultSetting = None
                  AdultOnly = false }

            let result = Labels.interpretLabelValueDefinition def None
            let account = result.Behaviors.Account

            Expect.equal account.ProfileList (Some "alert") "Account.ProfileList"
            Expect.equal account.ProfileView (Some "alert") "Account.ProfileView"
            Expect.equal account.ContentList (Some "alert") "Account.ContentList"
            Expect.equal account.ContentView (Some "alert") "Account.ContentView"
            Expect.isNone account.Avatar "Account.Avatar should be None"
        }

        test "none blur with none severity: all contexts are None" {
            let def =
                { Identifier = "silent"
                  Blurs = "none"
                  Severity = "none"
                  DefaultSetting = None
                  AdultOnly = false }

            let result = Labels.interpretLabelValueDefinition def None
            let account = result.Behaviors.Account

            Expect.isNone account.ProfileList "Account.ProfileList"
            Expect.isNone account.ProfileView "Account.ProfileView"
            Expect.isNone account.ContentList "Account.ContentList"
            Expect.isNone account.ContentView "Account.ContentView"
        }

        test "default setting: hide" {
            let def =
                { Identifier = "spam"
                  Blurs = "content"
                  Severity = "none"
                  DefaultSetting = Some "hide"
                  AdultOnly = false }

            let result = Labels.interpretLabelValueDefinition def None
            Expect.equal result.DefaultSetting LabelDefaultSetting.Hide "Default should be Hide"
        }

        test "default setting: ignore" {
            let def =
                { Identifier = "mild"
                  Blurs = "content"
                  Severity = "none"
                  DefaultSetting = Some "ignore"
                  AdultOnly = false }

            let result = Labels.interpretLabelValueDefinition def None
            Expect.equal result.DefaultSetting LabelDefaultSetting.Ignore "Default should be Ignore"
        }

        test "default setting: defaults to warn" {
            let def =
                { Identifier = "default"
                  Blurs = "content"
                  Severity = "none"
                  DefaultSetting = None
                  AdultOnly = false }

            let result = Labels.interpretLabelValueDefinition def None
            Expect.equal result.DefaultSetting LabelDefaultSetting.Warn "Default should be Warn"
        }

        test "severity parsing" {
            let mkDef sev =
                { Identifier = "test"
                  Blurs = "none"
                  Severity = sev
                  DefaultSetting = None
                  AdultOnly = false }

            Expect.equal
                (Labels.interpretLabelValueDefinition (mkDef "alert") None).Severity
                LabelSeverity.Alert
                "alert"

            Expect.equal
                (Labels.interpretLabelValueDefinition (mkDef "inform") None).Severity
                LabelSeverity.Inform
                "inform"

            Expect.equal
                (Labels.interpretLabelValueDefinition (mkDef "none") None).Severity
                LabelSeverity.None
                "none"

            Expect.equal
                (Labels.interpretLabelValueDefinition (mkDef "unknown") None).Severity
                LabelSeverity.None
                "unknown defaults to None"
        }

        test "blurs parsing" {
            let mkDef blurs =
                { Identifier = "test"
                  Blurs = blurs
                  Severity = "none"
                  DefaultSetting = None
                  AdultOnly = false }

            Expect.equal
                (Labels.interpretLabelValueDefinition (mkDef "content") None).Blurs
                LabelBlurs.Content
                "content"

            Expect.equal
                (Labels.interpretLabelValueDefinition (mkDef "media") None).Blurs
                LabelBlurs.Media
                "media"

            Expect.equal
                (Labels.interpretLabelValueDefinition (mkDef "none") None).Blurs
                LabelBlurs.None
                "none"

            Expect.equal
                (Labels.interpretLabelValueDefinition (mkDef "unknown") None).Blurs
                LabelBlurs.None
                "unknown defaults to None"
        }
    ]

// ============================================================
// interpretLabelValueDefinitions
// ============================================================

[<Tests>]
let interpretLabelValueDefinitionsTests =
    testList "Labels - interpretLabelValueDefinitions" [

        test "filters out system labels (starting with !)" {
            let defs =
                [ { Identifier = "!hide"
                    Blurs = "content"
                    Severity = "alert"
                    DefaultSetting = None
                    AdultOnly = false }
                  { Identifier = "my-custom"
                    Blurs = "content"
                    Severity = "inform"
                    DefaultSetting = None
                    AdultOnly = false } ]

            let result = Labels.interpretLabelValueDefinitions "did:plc:labeler1" defs
            Expect.hasLength result 1 "Should filter out system labels"
            Expect.equal result.[0].Identifier "my-custom" "Should keep custom label"
            Expect.equal result.[0].DefinedBy (Some "did:plc:labeler1") "Should have labeler DID"
        }

        test "filters out labels with uppercase" {
            let defs =
                [ { Identifier = "MyLabel"
                    Blurs = "content"
                    Severity = "alert"
                    DefaultSetting = None
                    AdultOnly = false }
                  { Identifier = "valid-label"
                    Blurs = "media"
                    Severity = "inform"
                    DefaultSetting = None
                    AdultOnly = false } ]

            let result = Labels.interpretLabelValueDefinitions "did:plc:labeler1" defs
            Expect.hasLength result 1 "Should filter out uppercase labels"
            Expect.equal result.[0].Identifier "valid-label" "Should keep valid label"
        }

        test "empty list returns empty" {
            let result = Labels.interpretLabelValueDefinitions "did:plc:labeler1" []
            Expect.isEmpty result "Empty input should give empty output"
        }

        test "multiple valid definitions" {
            let defs =
                [ { Identifier = "spoiler"
                    Blurs = "content"
                    Severity = "alert"
                    DefaultSetting = None
                    AdultOnly = false }
                  { Identifier = "graphic"
                    Blurs = "media"
                    Severity = "inform"
                    DefaultSetting = Some "hide"
                    AdultOnly = true } ]

            let result = Labels.interpretLabelValueDefinitions "did:plc:labeler1" defs
            Expect.hasLength result 2 "Should have two results"
            Expect.equal result.[0].Identifier "spoiler" "First should be spoiler"
            Expect.equal result.[1].Identifier "graphic" "Second should be graphic"
            Expect.isTrue result.[1].AdultOnly "graphic should be adult-only"
        }
    ]

// ============================================================
// createLabelMap and findLabelWithCustom
// ============================================================

[<Tests>]
let customLabelMapTests =
    testList "Labels - custom label map" [

        test "empty custom labels returns built-in map" {
            let map = Labels.createLabelMap []
            let hideLabel = Labels.findLabelWithCustom map "did:plc:labeler1" "!hide"
            Expect.isSome hideLabel "Should find !hide in built-in map"
            Expect.equal hideLabel.Value.Identifier "!hide" "Should be !hide"
        }

        test "custom label overrides for specific labeler" {
            let customDef =
                { Identifier = "spoiler"
                  Blurs = "content"
                  Severity = "alert"
                  DefaultSetting = None
                  AdultOnly = false }

            let custom = Labels.interpretLabelValueDefinition customDef (Some "did:plc:labeler1")
            let map = Labels.createLabelMap [ custom ]

            let result = Labels.findLabelWithCustom map "did:plc:labeler1" "spoiler"
            Expect.isSome result "Should find custom label"
            Expect.equal result.Value.Identifier "spoiler" "Should be spoiler"
            Expect.equal result.Value.DefinedBy (Some "did:plc:labeler1") "Should be from labeler1"
        }

        test "custom label not found for different labeler" {
            let customDef =
                { Identifier = "spoiler"
                  Blurs = "content"
                  Severity = "alert"
                  DefaultSetting = None
                  AdultOnly = false }

            let custom = Labels.interpretLabelValueDefinition customDef (Some "did:plc:labeler1")
            let map = Labels.createLabelMap [ custom ]

            let result = Labels.findLabelWithCustom map "did:plc:labeler2" "spoiler"
            Expect.isNone result "Should not find custom label from different labeler"
        }

        test "falls back to built-in for known labels" {
            let customDef =
                { Identifier = "spoiler"
                  Blurs = "content"
                  Severity = "alert"
                  DefaultSetting = None
                  AdultOnly = false }

            let custom = Labels.interpretLabelValueDefinition customDef (Some "did:plc:labeler1")
            let map = Labels.createLabelMap [ custom ]

            let result = Labels.findLabelWithCustom map "did:plc:labeler2" "porn"
            Expect.isSome result "Should fall back to built-in porn label"
            Expect.equal result.Value.Identifier "porn" "Should be porn"
            Expect.isNone result.Value.DefinedBy "Built-in should have no DefinedBy"
        }

        test "unknown label from unknown labeler returns None" {
            let map = Labels.createLabelMap []
            let result = Labels.findLabelWithCustom map "did:plc:labeler1" "totally-unknown"
            Expect.isNone result "Unknown label should return None"
        }

        test "multiple labelers with same label identifier" {
            let customDef1 =
                Labels.interpretLabelValueDefinition
                    { Identifier = "spoiler"
                      Blurs = "content"
                      Severity = "alert"
                      DefaultSetting = None
                      AdultOnly = false }
                    (Some "did:plc:labeler1")

            let customDef2 =
                Labels.interpretLabelValueDefinition
                    { Identifier = "spoiler"
                      Blurs = "media"
                      Severity = "inform"
                      DefaultSetting = Some "hide"
                      AdultOnly = false }
                    (Some "did:plc:labeler2")

            let map = Labels.createLabelMap [ customDef1; customDef2 ]

            let r1 = Labels.findLabelWithCustom map "did:plc:labeler1" "spoiler"
            Expect.equal r1.Value.Blurs LabelBlurs.Content "Labeler1's spoiler should blur content"

            let r2 = Labels.findLabelWithCustom map "did:plc:labeler2" "spoiler"
            Expect.equal r2.Value.Blurs LabelBlurs.Media "Labeler2's spoiler should blur media"
        }
    ]

// ============================================================
// moderateLabelsWithCustom
// ============================================================

[<Tests>]
let moderateLabelsWithCustomTests =
    testList "Moderation - moderateLabelsWithCustom" [

        test "custom label from known labeler produces cause" {
            let custom =
                Labels.interpretLabelValueDefinition
                    { Identifier = "spoiler"
                      Blurs = "content"
                      Severity = "alert"
                      DefaultSetting = None
                      AdultOnly = false }
                    (Some "did:plc:labeler1")

            let map = Labels.createLabelMap [ custom ]
            let labels = [ mkLabel "did:plc:labeler1" "at://post1" "spoiler" ]

            let causes =
                Moderation.moderateLabelsWithCustom defaultPrefs labels LabelTarget.Content None map

            Expect.hasLength causes 1 "Should have one cause"
            Expect.equal causes.[0].Type ModerationCauseType.Label "Should be Label type"
            Expect.equal causes.[0].Description "spoiler" "Description should be spoiler"
        }

        test "custom label from unknown labeler is ignored" {
            let custom =
                Labels.interpretLabelValueDefinition
                    { Identifier = "spoiler"
                      Blurs = "content"
                      Severity = "alert"
                      DefaultSetting = None
                      AdultOnly = false }
                    (Some "did:plc:labeler1")

            let map = Labels.createLabelMap [ custom ]
            // Label comes from labeler2, but only labeler1 defined "spoiler"
            let labels = [ mkLabel "did:plc:labeler2" "at://post1" "spoiler" ]

            let causes =
                Moderation.moderateLabelsWithCustom defaultPrefs labels LabelTarget.Content None map

            Expect.isEmpty causes "Custom label from unknown labeler should be ignored"
        }

        test "built-in label still works through custom label map" {
            let map = Labels.createLabelMap []
            let labels = [ mkLabel "did:plc:labeler1" "at://post1" "!hide" ]

            let causes =
                Moderation.moderateLabelsWithCustom defaultPrefs labels LabelTarget.Content None map

            Expect.hasLength causes 1 "Built-in label should still produce cause"
            Expect.equal causes.[0].Description "!hide" "Should be !hide"
        }

        test "custom adult label with adult content disabled is force-hidden" {
            let custom =
                Labels.interpretLabelValueDefinition
                    { Identifier = "adult-custom"
                      Blurs = "media"
                      Severity = "alert"
                      DefaultSetting = None
                      AdultOnly = true }
                    (Some "did:plc:labeler1")

            let map = Labels.createLabelMap [ custom ]
            let prefs = { defaultPrefs with AdultContentEnabled = false }
            let labels = [ mkLabel "did:plc:labeler1" "at://post1" "adult-custom" ]

            let causes =
                Moderation.moderateLabelsWithCustom prefs labels LabelTarget.Content None map

            Expect.hasLength causes 1 "Adult label with adult disabled should produce cause"
            Expect.equal causes.[0].Priority 1 "Priority should be 1 (forced)"
        }

        test "negation still works with custom labels" {
            let custom =
                Labels.interpretLabelValueDefinition
                    { Identifier = "spoiler"
                      Blurs = "content"
                      Severity = "alert"
                      DefaultSetting = None
                      AdultOnly = false }
                    (Some "did:plc:labeler1")

            let map = Labels.createLabelMap [ custom ]

            let labels =
                [ mkLabel "did:plc:labeler1" "at://post1" "spoiler"
                  { Src = "did:plc:labeler1"; Uri = "at://post1"; Val = "spoiler"; Neg = true; Cts = None } ]

            let causes =
                Moderation.moderateLabelsWithCustom defaultPrefs labels LabelTarget.Content None map

            Expect.isEmpty causes "Negation should cancel custom label"
        }
    ]

// ============================================================
// moderateNotification, moderateFeedGenerator, moderateUserList
// ============================================================

[<Tests>]
let moderateConvenienceTests =
    testList "Moderation - convenience functions" [

        // moderateNotification
        test "moderateNotification: muted user is filtered" {
            let decision =
                Moderation.moderateNotification defaultPrefs [] true false false

            Expect.isTrue decision.Filter "Muted user in notification should be filtered"
        }

        test "moderateNotification: blocked user is filtered" {
            let decision =
                Moderation.moderateNotification defaultPrefs [] false true false

            Expect.isTrue decision.Filter "Blocked user in notification should be filtered"
        }

        test "moderateNotification: blocked-by user is filtered" {
            let decision =
                Moderation.moderateNotification defaultPrefs [] false false true

            Expect.isTrue decision.Filter "Blocked-by user in notification should be filtered"
        }

        test "moderateNotification: labeled notification" {
            let labels = [ mkLabel "did:plc:labeler1" "at://post1" "!hide" ]
            let decision =
                Moderation.moderateNotification defaultPrefs labels false false false

            Expect.isTrue decision.Blur "!hide label in notification should blur"
        }

        test "moderateNotification: clean notification has no action" {
            let decision =
                Moderation.moderateNotification defaultPrefs [] false false false

            Expect.equal decision.Action ModerationAction.NoAction "Clean notification should have no action"
        }

        // moderateFeedGenerator
        test "moderateFeedGenerator: labeled feed in list view" {
            let labels = [ mkLabel "did:plc:labeler1" "at://feed1" "!warn" ]
            let decision =
                Moderation.moderateFeedGenerator defaultPrefs labels ModerationContext.ContentList

            Expect.isTrue decision.Blur "!warn label on feed should blur in list"
        }

        test "moderateFeedGenerator: clean feed has no action" {
            let decision =
                Moderation.moderateFeedGenerator defaultPrefs [] ModerationContext.ContentList

            Expect.equal decision.Action ModerationAction.NoAction "Clean feed should have no action"
        }

        test "moderateFeedGenerator: labeled feed in content view" {
            let labels = [ mkLabel "did:plc:labeler1" "at://feed1" "!hide" ]
            let decision =
                Moderation.moderateFeedGenerator defaultPrefs labels ModerationContext.ContentView

            Expect.isTrue decision.Blur "!hide label on feed should blur in content view"
        }

        // moderateUserList
        test "moderateUserList: labeled list in list view" {
            let labels = [ mkLabel "did:plc:labeler1" "at://list1" "!warn" ]
            let decision =
                Moderation.moderateUserList defaultPrefs labels ModerationContext.ContentList

            Expect.isTrue decision.Blur "!warn label on list should blur in list"
        }

        test "moderateUserList: clean list has no action" {
            let decision =
                Moderation.moderateUserList defaultPrefs [] ModerationContext.ContentList

            Expect.equal decision.Action ModerationAction.NoAction "Clean list should have no action"
        }

        test "moderateUserList: porn label on list with adult disabled" {
            let labels = [ mkLabel "did:plc:labeler1" "at://list1" "porn" ]
            let decision =
                Moderation.moderateUserList defaultPrefs labels ModerationContext.ContentMedia

            Expect.isTrue decision.Blur "porn on list should blur media"
            Expect.isTrue decision.NoOverride "porn with adult disabled should be no-override"
        }
    ]
