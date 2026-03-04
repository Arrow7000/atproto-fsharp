namespace FSharp.ATProto.Moderation

/// Represents the target of a label application.
[<RequireQualifiedAccess>]
type LabelTarget =
    /// Applied to content (posts, feeds, etc.)
    | Content
    /// Applied to media (images, video)
    | Media
    /// Applied to an account
    | Account
    /// Applied to a profile (avatar, banner, display name)
    | Profile

/// Severity level of a label for UI display.
[<RequireQualifiedAccess>]
type LabelSeverity =
    /// Informational only
    | Inform
    /// Alert the user
    | Alert
    /// No inherent severity
    | None

/// What a label blurs in the UI.
[<RequireQualifiedAccess>]
type LabelBlurs =
    /// Blurs all content
    | Content
    /// Blurs only media
    | Media
    /// Blurs nothing
    | None

/// Default setting for a label when the user hasn't configured a preference.
[<RequireQualifiedAccess>]
type LabelDefaultSetting =
    /// Show a warning overlay
    | Warn
    /// Hide the content
    | Hide
    /// Ignore the label
    | Ignore

/// User's preferred visibility for a label.
[<RequireQualifiedAccess>]
type LabelVisibility =
    /// Show content normally
    | Show
    /// Show a warning overlay
    | Warn
    /// Hide the content
    | Hide

/// Flags that modify label behavior.
[<RequireQualifiedAccess>]
type LabelFlag =
    /// User cannot override this label's effect
    | NoOverride
    /// This is adult content
    | Adult
    /// Only applies to unauthenticated users
    | Unauthed
    /// Label does not apply when the labeler is the content author (self-label)
    | NoSelf

/// Behavior specification for a label in a specific context.
type ModerationBehavior =
    { ProfileList: string option
      ProfileView: string option
      Avatar: string option
      Banner: string option
      DisplayName: string option
      ContentList: string option
      ContentView: string option
      ContentMedia: string option }

    static member Empty =
        { ProfileList = None
          ProfileView = None
          Avatar = None
          Banner = None
          DisplayName = None
          ContentList = None
          ContentView = None
          ContentMedia = None }

/// Behaviors for a label across different subject types (account, profile, content).
type LabelBehaviors =
    { Account: ModerationBehavior
      Profile: ModerationBehavior
      Content: ModerationBehavior }

/// Definition of a known label in the AT Protocol.
type LabelDefinition =
    { /// Label identifier string (e.g., "!hide", "porn", "graphic-media")
      Identifier: string
      /// Whether the user can configure this label's visibility
      Configurable: bool
      /// Severity level for UI display
      Severity: LabelSeverity
      /// What this label blurs
      Blurs: LabelBlurs
      /// Default setting when user hasn't configured a preference
      DefaultSetting: LabelDefaultSetting
      /// Whether this is adult-only content
      AdultOnly: bool
      /// Behavioral flags
      Flags: LabelFlag list
      /// Context-specific behaviors for each subject type
      Behaviors: LabelBehaviors
      /// DID of the labeler that defined this label (None for built-in labels)
      DefinedBy: string option }

/// Input for interpreting a custom label value definition from a labeler.
type CustomLabelValueDef =
    { /// Label identifier string (e.g., "my-custom-label")
      Identifier: string
      /// What this label blurs: "content", "media", or "none"
      Blurs: string
      /// Severity: "inform", "alert", or "none"
      Severity: string
      /// Default setting: "ignore", "warn", or "hide" (defaults to "warn")
      DefaultSetting: string option
      /// Whether this is adult-only content
      AdultOnly: bool }

module Labels =

    let private blurAll: ModerationBehavior =
        { ProfileList = Some "blur"
          ProfileView = Some "blur"
          Avatar = Some "blur"
          Banner = Some "blur"
          DisplayName = Some "blur"
          ContentList = Some "blur"
          ContentView = Some "blur"
          ContentMedia = None }

    let private blurAllContent: ModerationBehavior =
        { ModerationBehavior.Empty with
            ContentList = Some "blur"
            ContentView = Some "blur" }

    let private blurAllProfile: ModerationBehavior =
        { ModerationBehavior.Empty with
            Avatar = Some "blur"
            Banner = Some "blur"
            DisplayName = Some "blur" }

    let private blurMedia: ModerationBehavior =
        { ModerationBehavior.Empty with
            Avatar = Some "blur"
            Banner = Some "blur" }

    let private blurContentMedia: ModerationBehavior =
        { ModerationBehavior.Empty with
            ContentMedia = Some "blur" }

    /// The built-in AT Protocol label definitions.
    let builtInLabels: LabelDefinition list =
        [ { Identifier = "!hide"
            Configurable = false
            Severity = LabelSeverity.Alert
            Blurs = LabelBlurs.Content
            DefaultSetting = LabelDefaultSetting.Hide
            AdultOnly = false
            Flags = [ LabelFlag.NoOverride; LabelFlag.NoSelf ]
            Behaviors =
                { Account = blurAll
                  Profile = blurAllProfile
                  Content = blurAllContent }
            DefinedBy = None }

          { Identifier = "!warn"
            Configurable = false
            Severity = LabelSeverity.None
            Blurs = LabelBlurs.Content
            DefaultSetting = LabelDefaultSetting.Warn
            AdultOnly = false
            Flags = [ LabelFlag.NoSelf ]
            Behaviors =
                { Account =
                    { blurAll with
                        DisplayName = None }
                  Profile = blurAllProfile
                  Content = blurAllContent }
            DefinedBy = None }

          { Identifier = "!no-unauthenticated"
            Configurable = false
            Severity = LabelSeverity.None
            Blurs = LabelBlurs.Content
            DefaultSetting = LabelDefaultSetting.Hide
            AdultOnly = false
            Flags = [ LabelFlag.NoOverride; LabelFlag.Unauthed ]
            Behaviors =
                { Account = blurAll
                  Profile = blurAllProfile
                  Content = blurAllContent }
            DefinedBy = None }

          { Identifier = "porn"
            Configurable = true
            Severity = LabelSeverity.None
            Blurs = LabelBlurs.Media
            DefaultSetting = LabelDefaultSetting.Hide
            AdultOnly = true
            Flags = [ LabelFlag.Adult ]
            Behaviors =
                { Account = blurMedia
                  Profile = blurMedia
                  Content = blurContentMedia }
            DefinedBy = None }

          { Identifier = "sexual"
            Configurable = true
            Severity = LabelSeverity.None
            Blurs = LabelBlurs.Media
            DefaultSetting = LabelDefaultSetting.Warn
            AdultOnly = true
            Flags = [ LabelFlag.Adult ]
            Behaviors =
                { Account = blurMedia
                  Profile = blurMedia
                  Content = blurContentMedia }
            DefinedBy = None }

          { Identifier = "nudity"
            Configurable = true
            Severity = LabelSeverity.None
            Blurs = LabelBlurs.Media
            DefaultSetting = LabelDefaultSetting.Ignore
            AdultOnly = false
            Flags = []
            Behaviors =
                { Account = blurMedia
                  Profile = blurMedia
                  Content = blurContentMedia }
            DefinedBy = None }

          { Identifier = "graphic-media"
            Configurable = true
            Severity = LabelSeverity.None
            Blurs = LabelBlurs.Media
            DefaultSetting = LabelDefaultSetting.Warn
            AdultOnly = false
            Flags = [ LabelFlag.Adult ]
            Behaviors =
                { Account = blurMedia
                  Profile = blurMedia
                  Content = blurContentMedia }
            DefinedBy = None }

          { Identifier = "gore"
            Configurable = true
            Severity = LabelSeverity.None
            Blurs = LabelBlurs.Media
            DefaultSetting = LabelDefaultSetting.Warn
            AdultOnly = false
            Flags = [ LabelFlag.Adult ]
            Behaviors =
                { Account = blurMedia
                  Profile = blurMedia
                  Content = blurContentMedia }
            DefinedBy = None } ]

    let private labelMap =
        builtInLabels
        |> List.map (fun l -> l.Identifier, l)
        |> Map.ofList

    /// Find a built-in label definition by its identifier string.
    let findLabel (identifier: string) : LabelDefinition option =
        Map.tryFind identifier labelMap

    /// Check whether a label identifier matches the custom label value pattern (lowercase letters and hyphens).
    let isCustomLabelValue (identifier: string) : bool =
        identifier.Length > 0
        && identifier
           |> Seq.forall (fun c -> (c >= 'a' && c <= 'z') || c = '-')

    /// Interpret a custom label value definition from a labeler into a LabelDefinition.
    /// Follows the TypeScript SDK's interpretLabelValueDefinition pattern:
    /// maps severity/blurs to context-specific behaviors, sets configurable=true,
    /// and adds NoSelf + optional Adult flags.
    let interpretLabelValueDefinition
        (def: CustomLabelValueDef)
        (definedBy: string option)
        : LabelDefinition =

        let alertOrInform: string option =
            match def.Severity with
            | "alert" -> Some "alert"
            | "inform" -> Some "inform"
            | _ -> None

        let behaviors =
            match def.Blurs with
            | "content" ->
                let contentViewAction =
                    if def.AdultOnly then Some "blur" else alertOrInform

                { Account =
                    { ModerationBehavior.Empty with
                        ProfileList = alertOrInform
                        ProfileView = alertOrInform
                        ContentList = Some "blur"
                        ContentView = contentViewAction }
                  Profile =
                    { ModerationBehavior.Empty with
                        ProfileList = alertOrInform
                        ProfileView = alertOrInform }
                  Content =
                    { ModerationBehavior.Empty with
                        ContentList = Some "blur"
                        ContentView = contentViewAction } }
            | "media" ->
                { Account =
                    { ModerationBehavior.Empty with
                        ProfileList = alertOrInform
                        ProfileView = alertOrInform
                        Avatar = Some "blur"
                        Banner = Some "blur" }
                  Profile =
                    { ModerationBehavior.Empty with
                        ProfileList = alertOrInform
                        ProfileView = alertOrInform
                        Avatar = Some "blur"
                        Banner = Some "blur" }
                  Content =
                    { ModerationBehavior.Empty with
                        ContentMedia = Some "blur" } }
            | _ -> // "none" or unknown
                { Account =
                    { ModerationBehavior.Empty with
                        ProfileList = alertOrInform
                        ProfileView = alertOrInform
                        ContentList = alertOrInform
                        ContentView = alertOrInform }
                  Profile =
                    { ModerationBehavior.Empty with
                        ProfileList = alertOrInform
                        ProfileView = alertOrInform }
                  Content =
                    { ModerationBehavior.Empty with
                        ContentList = alertOrInform
                        ContentView = alertOrInform } }

        let defaultSetting =
            match def.DefaultSetting with
            | Some "hide" -> LabelDefaultSetting.Hide
            | Some "ignore" -> LabelDefaultSetting.Ignore
            | _ -> LabelDefaultSetting.Warn

        let severity =
            match def.Severity with
            | "alert" -> LabelSeverity.Alert
            | "inform" -> LabelSeverity.Inform
            | _ -> LabelSeverity.None

        let blurs =
            match def.Blurs with
            | "content" -> LabelBlurs.Content
            | "media" -> LabelBlurs.Media
            | _ -> LabelBlurs.None

        let flags =
            LabelFlag.NoSelf
            :: (if def.AdultOnly then [ LabelFlag.Adult ] else [])

        { Identifier = def.Identifier
          Configurable = true
          Severity = severity
          Blurs = blurs
          DefaultSetting = defaultSetting
          AdultOnly = def.AdultOnly
          Flags = flags
          Behaviors = behaviors
          DefinedBy = definedBy }

    /// Interpret multiple custom label value definitions from a labeler.
    /// Filters to only valid custom label identifiers (not system labels starting with '!').
    let interpretLabelValueDefinitions
        (labelerDid: string)
        (defs: CustomLabelValueDef list)
        : LabelDefinition list =
        defs
        |> List.filter (fun d -> isCustomLabelValue d.Identifier)
        |> List.map (fun d -> interpretLabelValueDefinition d (Some labelerDid))

    /// Merge built-in labels with custom labeler-defined labels into a lookup map.
    /// Custom labels are keyed by (labelerDid, identifier). Built-in labels use ("", identifier).
    /// When looking up, the label source DID is used to prefer custom definitions from
    /// the same labeler, falling back to built-in definitions.
    let createLabelMap (customLabels: LabelDefinition list) : Map<string * string, LabelDefinition> =
        let builtInEntries =
            builtInLabels
            |> List.map (fun l -> ("", l.Identifier), l)

        let customEntries =
            customLabels
            |> List.choose (fun l ->
                match l.DefinedBy with
                | Some did -> Some ((did, l.Identifier), l)
                | None -> None)

        builtInEntries @ customEntries |> Map.ofList

    /// Find a label definition by identifier and optional labeler DID.
    /// First checks for a custom definition from the given labeler,
    /// then falls back to the built-in definition.
    let findLabelWithCustom
        (labelMap: Map<string * string, LabelDefinition>)
        (labelerDid: string)
        (identifier: string)
        : LabelDefinition option =
        match Map.tryFind (labelerDid, identifier) labelMap with
        | Some _ as result -> result
        | None -> Map.tryFind ("", identifier) labelMap
