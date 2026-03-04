namespace FSharp.ATProto.Moderation

open System

/// Context in which moderation decisions are evaluated.
/// Different contexts produce different actions for the same moderation cause.
[<RequireQualifiedAccess>]
type ModerationContext =
    /// Content shown in a list/feed view
    | ContentList
    /// Content shown in a detail/expanded view
    | ContentView
    /// Profile shown in a list
    | ProfileList
    /// Profile shown in detail view
    | ProfileView
    /// Avatar display
    | Avatar
    /// Banner display
    | Banner
    /// Display name
    | DisplayName
    /// Content media (images, video)
    | ContentMedia

/// The action a UI should take based on moderation.
[<RequireQualifiedAccess>]
type ModerationAction =
    /// Remove from view entirely (used in list contexts)
    | Filter
    /// Show with a blur/overlay that can be clicked through (unless noOverride)
    | Blur
    /// Show an alert badge/indicator
    | Alert
    /// Show an informational indicator
    | Inform
    /// No moderation action needed
    | NoAction

/// Source of a moderation cause.
[<RequireQualifiedAccess>]
type ModerationCauseSource =
    /// Caused by user's own settings
    | User
    /// Caused by a labeler service
    | Labeler of did: string
    /// Caused by a list
    | List of uri: string

/// The type of moderation cause.
[<RequireQualifiedAccess>]
type ModerationCauseType =
    /// Content has a label applied
    | Label
    /// User is blocking the account
    | Blocking
    /// User is blocked by the account
    | BlockedBy
    /// Mutual block or other block situation
    | BlockOther
    /// Account is muted by user
    | Muted
    /// Content matches a muted word
    | MuteWord
    /// Content is in the user's hidden posts list
    | Hidden

/// A single cause for moderation action.
type ModerationCause =
    { /// What type of cause this is
      Type: ModerationCauseType
      /// Where this cause originates from
      Source: ModerationCauseSource
      /// Priority for sorting (lower = higher priority)
      Priority: int
      /// Additional context (label identifier, muted word, etc.)
      Description: string
      /// Whether this cause has been downgraded (e.g., for your own content)
      Downgraded: bool }

/// A label applied to content or an account.
type Label =
    { /// DID of the labeler that created this label
      Src: string
      /// AT-URI of the target this label applies to
      Uri: string
      /// Label identifier string (e.g., "porn", "!hide")
      Val: string
      /// Whether this is a negation label (removes a previous label)
      Neg: bool
      /// Timestamp when the label was created
      Cts: string option }

/// User preferences that influence moderation decisions.
type ModerationPrefs =
    { /// Whether the user has enabled adult content
      AdultContentEnabled: bool
      /// Per-label visibility preferences (label identifier -> visibility)
      Labels: Map<string, LabelVisibility>
      /// Muted words list
      MutedWords: MutedWord list
      /// Hidden post URIs
      HiddenPosts: string list }

    static member Default =
        { AdultContentEnabled = false
          Labels = Map.empty
          MutedWords = []
          HiddenPosts = [] }

/// The result of moderation evaluation, containing all causes and the resolved UI state.
type ModerationDecision =
    { /// All moderation causes that apply, sorted by priority
      Causes: ModerationCause list
      /// Whether any filter causes are present
      Filter: bool
      /// Whether any blur causes are present
      Blur: bool
      /// Whether the blur cannot be overridden by the user
      NoOverride: bool
      /// Whether any alert causes are present
      Alert: bool
      /// Whether any inform causes are present
      Inform: bool }

    /// A decision with no moderation action.
    static member None =
        { Causes = []
          Filter = false
          Blur = false
          NoOverride = false
          Alert = false
          Inform = false }

    /// The primary (highest-priority) action to take.
    member this.Action =
        if this.Filter then ModerationAction.Filter
        elif this.Blur then ModerationAction.Blur
        elif this.Alert then ModerationAction.Alert
        elif this.Inform then ModerationAction.Inform
        else ModerationAction.NoAction

    /// The primary (highest-priority) cause, if any.
    member this.PrimaryCause =
        this.Causes |> List.tryHead

module Moderation =

    /// Look up the behavior string for a given context from a ModerationBehavior record.
    let private behaviorForContext (context: ModerationContext) (behavior: ModerationBehavior) : string option =
        match context with
        | ModerationContext.ProfileList -> behavior.ProfileList
        | ModerationContext.ProfileView -> behavior.ProfileView
        | ModerationContext.Avatar -> behavior.Avatar
        | ModerationContext.Banner -> behavior.Banner
        | ModerationContext.DisplayName -> behavior.DisplayName
        | ModerationContext.ContentList -> behavior.ContentList
        | ModerationContext.ContentView -> behavior.ContentView
        | ModerationContext.ContentMedia -> behavior.ContentMedia

    /// Get the behavior record for a given label target from a LabelBehaviors.
    let private behaviorsForTarget (target: LabelTarget) (behaviors: LabelBehaviors) : ModerationBehavior =
        match target with
        | LabelTarget.Account -> behaviors.Account
        | LabelTarget.Profile -> behaviors.Profile
        | LabelTarget.Content -> behaviors.Content
        | LabelTarget.Media -> behaviors.Content // Media labels use content behaviors

    /// Block behavior: defines how blocking affects different contexts.
    let private blockBehavior: ModerationBehavior =
        { ProfileList = Some "blur"
          ProfileView = Some "alert"
          Avatar = Some "blur"
          Banner = Some "blur"
          DisplayName = None
          ContentList = Some "blur"
          ContentView = Some "blur"
          ContentMedia = None }

    /// Mute behavior: defines how muting affects different contexts.
    let private muteBehavior: ModerationBehavior =
        { ProfileList = Some "inform"
          ProfileView = Some "alert"
          Avatar = None
          Banner = None
          DisplayName = None
          ContentList = Some "blur"
          ContentView = Some "inform"
          ContentMedia = None }

    /// Mute-word behavior: defines how muted words affect different contexts.
    let private muteWordBehavior: ModerationBehavior =
        { ModerationBehavior.Empty with
            ContentList = Some "blur"
            ContentView = Some "blur" }

    /// Hidden behavior: defines how hidden posts affect different contexts.
    let private hiddenBehavior: ModerationBehavior =
        { ModerationBehavior.Empty with
            ContentList = Some "blur"
            ContentView = Some "blur" }

    /// Determine the effective label preference, taking into account
    /// label definition, user preferences, and adult content settings.
    let private effectiveLabelPref
        (labelDef: LabelDefinition)
        (prefs: ModerationPrefs)
        : LabelVisibility option =

        if not labelDef.Configurable then
            // Non-configurable labels always use their default
            match labelDef.DefaultSetting with
            | LabelDefaultSetting.Hide -> Some LabelVisibility.Hide
            | LabelDefaultSetting.Warn -> Some LabelVisibility.Warn
            | LabelDefaultSetting.Ignore -> None
        elif labelDef.Flags |> List.contains LabelFlag.Adult && not prefs.AdultContentEnabled then
            // Adult labels with adult content disabled are always hidden
            Some LabelVisibility.Hide
        else
            // Check user preference, fall back to default
            match Map.tryFind labelDef.Identifier prefs.Labels with
            | Some LabelVisibility.Show -> None // "show" means ignore the label
            | Some pref -> Some pref
            | None ->
                match labelDef.DefaultSetting with
                | LabelDefaultSetting.Hide -> Some LabelVisibility.Hide
                | LabelDefaultSetting.Warn -> Some LabelVisibility.Warn
                | LabelDefaultSetting.Ignore -> None

    /// Determine the priority for a label cause.
    let private labelPriority
        (labelDef: LabelDefinition)
        (prefs: ModerationPrefs)
        (target: LabelTarget)
        : int =

        let behavior = behaviorsForTarget target labelDef.Behaviors

        let isNoOverride = labelDef.Flags |> List.contains LabelFlag.NoOverride
        let isAdultDisabled = labelDef.Flags |> List.contains LabelFlag.Adult && not prefs.AdultContentEnabled

        if isNoOverride || isAdultDisabled then
            1
        else
            match effectiveLabelPref labelDef prefs with
            | Some LabelVisibility.Hide -> 2
            | _ ->
                // Measure severity of behavior
                let hasHighSeverity =
                    behavior.ProfileView = Some "blur" || behavior.ContentView = Some "blur"

                let hasMediumSeverity =
                    behavior.ContentList = Some "blur" || behavior.ContentMedia = Some "blur"

                if hasHighSeverity then 5
                elif hasMediumSeverity then 7
                else 8

    /// Determine whether a label's effect can be overridden by the user.
    let private isNoOverride (labelDef: LabelDefinition) (prefs: ModerationPrefs) : bool =
        labelDef.Flags |> List.contains LabelFlag.NoOverride
        || (labelDef.Flags |> List.contains LabelFlag.Adult && not prefs.AdultContentEnabled)

    /// Check whether a context is a "list" context (where filtering applies).
    let private isListContext (context: ModerationContext) : bool =
        match context with
        | ModerationContext.ContentList
        | ModerationContext.ProfileList -> true
        | _ -> false

    /// Evaluate a single cause against a context to determine filter/blur/alert/inform.
    let private evaluateCause
        (context: ModerationContext)
        (isMe: bool)
        (cause: ModerationCause)
        : {| Filter: bool; Blur: bool; NoOverride: bool; Alert: bool; Inform: bool |} =

        if isMe && cause.Type <> ModerationCauseType.Label then
            {| Filter = false; Blur = false; NoOverride = false; Alert = false; Inform = false |}
        else

        match cause.Type with
        | ModerationCauseType.Blocking
        | ModerationCauseType.BlockedBy
        | ModerationCauseType.BlockOther ->
            if isMe then
                {| Filter = false; Blur = false; NoOverride = false; Alert = false; Inform = false |}
            else
                let filter = isListContext context
                let action = behaviorForContext context blockBehavior

                let blur = not cause.Downgraded && action = Some "blur"
                let alert = not cause.Downgraded && action = Some "alert"
                let inform = not cause.Downgraded && action = Some "inform"
                let noOverride = blur // Block blurs are always no-override

                {| Filter = filter; Blur = blur; NoOverride = noOverride; Alert = alert; Inform = inform |}

        | ModerationCauseType.Muted ->
            if isMe then
                {| Filter = false; Blur = false; NoOverride = false; Alert = false; Inform = false |}
            else
                let filter = isListContext context
                let action = behaviorForContext context muteBehavior

                let blur = not cause.Downgraded && action = Some "blur"
                let alert = not cause.Downgraded && action = Some "alert"
                let inform = not cause.Downgraded && action = Some "inform"

                {| Filter = filter; Blur = blur; NoOverride = false; Alert = alert; Inform = inform |}

        | ModerationCauseType.MuteWord ->
            if isMe then
                {| Filter = false; Blur = false; NoOverride = false; Alert = false; Inform = false |}
            else
                let filter = context = ModerationContext.ContentList
                let action = behaviorForContext context muteWordBehavior

                let blur = not cause.Downgraded && action = Some "blur"
                let alert = not cause.Downgraded && action = Some "alert"
                let inform = not cause.Downgraded && action = Some "inform"

                {| Filter = filter; Blur = blur; NoOverride = false; Alert = alert; Inform = inform |}

        | ModerationCauseType.Hidden ->
            let filter = isListContext context
            let action = behaviorForContext context hiddenBehavior

            let blur = not cause.Downgraded && action = Some "blur"
            let alert = not cause.Downgraded && action = Some "alert"
            let inform = not cause.Downgraded && action = Some "inform"

            {| Filter = filter; Blur = blur; NoOverride = false; Alert = alert; Inform = inform |}

        | ModerationCauseType.Label ->
            // For labels, we need the label definition to determine behavior.
            // The cause.Description contains the label identifier.
            match Labels.findLabel cause.Description with
            | None ->
                {| Filter = false; Blur = false; NoOverride = false; Alert = false; Inform = false |}
            | Some labelDef ->
                // Determine the target from the cause source context
                let target =
                    // Use Content as default target for label causes
                    LabelTarget.Content

                let behavior = behaviorsForTarget target labelDef.Behaviors
                let action = behaviorForContext context behavior

                let effectivePref =
                    if not labelDef.Configurable then
                        match labelDef.DefaultSetting with
                        | LabelDefaultSetting.Hide -> LabelVisibility.Hide
                        | LabelDefaultSetting.Warn -> LabelVisibility.Warn
                        | LabelDefaultSetting.Ignore -> LabelVisibility.Show
                    elif labelDef.Flags |> List.contains LabelFlag.Adult then
                        LabelVisibility.Hide
                    else
                        LabelVisibility.Warn

                let filter =
                    not isMe
                    && isListContext context
                    && effectivePref = LabelVisibility.Hide

                let blur = not cause.Downgraded && action = Some "blur"
                let noOverride = blur && not isMe && isNoOverride labelDef ModerationPrefs.Default
                let alert = not cause.Downgraded && action = Some "alert"
                let inform = not cause.Downgraded && action = Some "inform"

                {| Filter = filter; Blur = blur; NoOverride = noOverride; Alert = alert; Inform = inform |}

    /// Resolve negation labels -- a label with Neg=true cancels out a previous non-negation label
    /// with the same Val from the same Src.
    let resolveLabels (labels: Label list) : Label list =
        let negations =
            labels
            |> List.filter (fun l -> l.Neg)
            |> List.map (fun l -> (l.Src, l.Val))
            |> Set.ofList

        labels
        |> List.filter (fun l -> not l.Neg && not (negations.Contains(l.Src, l.Val)))

    /// Evaluate labels against moderation preferences and produce causes.
    let moderateLabels
        (prefs: ModerationPrefs)
        (labels: Label list)
        (target: LabelTarget)
        (userDid: string option)
        : ModerationCause list =

        let resolved = resolveLabels labels

        resolved
        |> List.choose (fun label ->
            match Labels.findLabel label.Val with
            | None -> None
            | Some labelDef ->
                // Skip labels from unknown labelers (for now, accept all labelers)
                // Skip self-labels with NoSelf flag
                let isSelf =
                    match userDid with
                    | Some did -> label.Src = did
                    | None -> false

                if isSelf && labelDef.Flags |> List.contains LabelFlag.NoSelf then
                    None
                // Skip unauthed labels when user is authenticated
                elif labelDef.Flags |> List.contains LabelFlag.Unauthed && userDid.IsSome then
                    None
                else
                    match effectiveLabelPref labelDef prefs with
                    | None -> None // "ignore" or "show" preference
                    | Some _ ->
                        let priority = labelPriority labelDef prefs target
                        let source = ModerationCauseSource.Labeler label.Src

                        Some
                            { Type = ModerationCauseType.Label
                              Source = source
                              Priority = priority
                              Description = label.Val
                              Downgraded = false })

    /// Evaluate labels using a custom label map (supports custom labeler definitions).
    /// The label map is created via Labels.createLabelMap.
    let moderateLabelsWithCustom
        (prefs: ModerationPrefs)
        (labels: Label list)
        (target: LabelTarget)
        (userDid: string option)
        (customLabelMap: Map<string * string, LabelDefinition>)
        : ModerationCause list =

        let resolved = resolveLabels labels

        resolved
        |> List.choose (fun label ->
            match Labels.findLabelWithCustom customLabelMap label.Src label.Val with
            | None -> None
            | Some labelDef ->
                let isSelf =
                    match userDid with
                    | Some did -> label.Src = did
                    | None -> false

                if isSelf && labelDef.Flags |> List.contains LabelFlag.NoSelf then
                    None
                elif labelDef.Flags |> List.contains LabelFlag.Unauthed && userDid.IsSome then
                    None
                else
                    match effectiveLabelPref labelDef prefs with
                    | None -> None
                    | Some _ ->
                        let priority = labelPriority labelDef prefs target
                        let source = ModerationCauseSource.Labeler label.Src

                        Some
                            { Type = ModerationCauseType.Label
                              Source = source
                              Priority = priority
                              Description = label.Val
                              Downgraded = false })

    /// Evaluate muted words against text and tags.
    let moderateMutedWords
        (mutedWords: MutedWord list)
        (text: string)
        (tags: string list)
        (languages: string list)
        : ModerationCause list =

        if MutedWords.hasMutedWord mutedWords text tags languages then
            [ { Type = ModerationCauseType.MuteWord
                Source = ModerationCauseSource.User
                Priority = 6
                Description = "mute-word"
                Downgraded = false } ]
        else
            []

    /// Full moderation evaluation combining labels, muted words, mute status, block status,
    /// and hidden post status into a single decision for a given context.
    let moderate
        (prefs: ModerationPrefs)
        (labels: Label list)
        (text: string option)
        (tags: string list)
        (languages: string list)
        (isMuted: bool)
        (isBlocked: bool)
        (isBlockedBy: bool)
        (isHiddenPost: bool)
        (isMe: bool)
        (target: LabelTarget)
        (userDid: string option)
        (context: ModerationContext)
        : ModerationDecision =

        let mutable causes: ModerationCause list = []

        // 1. Labels
        let labelCauses = moderateLabels prefs labels target userDid
        causes <- causes @ labelCauses

        // 2. Blocking
        if isBlocked then
            causes <-
                causes
                @ [ { Type = ModerationCauseType.Blocking
                      Source = ModerationCauseSource.User
                      Priority = 3
                      Description = "blocking"
                      Downgraded = false } ]

        // 3. Blocked by
        if isBlockedBy then
            causes <-
                causes
                @ [ { Type = ModerationCauseType.BlockedBy
                      Source = ModerationCauseSource.User
                      Priority = 4
                      Description = "blocked-by"
                      Downgraded = false } ]

        // 4. Muted
        if isMuted then
            causes <-
                causes
                @ [ { Type = ModerationCauseType.Muted
                      Source = ModerationCauseSource.User
                      Priority = 6
                      Description = "muted"
                      Downgraded = false } ]

        // 5. Muted words
        match text with
        | Some t when not isMe ->
            let muteWordCauses = moderateMutedWords prefs.MutedWords t tags languages
            causes <- causes @ muteWordCauses
        | _ -> ()

        // 6. Hidden posts
        if isHiddenPost then
            causes <-
                causes
                @ [ { Type = ModerationCauseType.Hidden
                      Source = ModerationCauseSource.User
                      Priority = 6
                      Description = "hidden"
                      Downgraded = false } ]

        // Sort by priority
        let sortedCauses = causes |> List.sortBy (fun c -> c.Priority)

        // Evaluate each cause against the context
        let evaluations =
            sortedCauses
            |> List.map (evaluateCause context isMe)

        let hasFilter = evaluations |> List.exists (fun e -> e.Filter)
        let hasBlur = evaluations |> List.exists (fun e -> e.Blur)
        let hasNoOverride = evaluations |> List.exists (fun e -> e.NoOverride)
        let hasAlert = evaluations |> List.exists (fun e -> e.Alert)
        let hasInform = evaluations |> List.exists (fun e -> e.Inform)

        { Causes = sortedCauses
          Filter = hasFilter
          Blur = hasBlur
          NoOverride = hasNoOverride
          Alert = hasAlert
          Inform = hasInform }

    /// Simplified moderation for content with just labels.
    let moderateContent
        (prefs: ModerationPrefs)
        (labels: Label list)
        (context: ModerationContext)
        : ModerationDecision =
        moderate prefs labels None [] [] false false false false false LabelTarget.Content None context

    /// Simplified moderation for a profile/account.
    let moderateProfile
        (prefs: ModerationPrefs)
        (labels: Label list)
        (isMuted: bool)
        (isBlocked: bool)
        (isBlockedBy: bool)
        (isMe: bool)
        (context: ModerationContext)
        : ModerationDecision =
        moderate prefs labels None [] [] isMuted isBlocked isBlockedBy false isMe LabelTarget.Account None context

    /// Simplified moderation for a post with text.
    let moderatePost
        (prefs: ModerationPrefs)
        (labels: Label list)
        (text: string)
        (tags: string list)
        (languages: string list)
        (isHiddenPost: bool)
        (isMe: bool)
        (userDid: string option)
        (context: ModerationContext)
        : ModerationDecision =
        moderate
            prefs
            labels
            (Some text)
            tags
            languages
            false
            false
            false
            isHiddenPost
            isMe
            LabelTarget.Content
            userDid
            context

    /// Simplified moderation for a notification.
    /// Notifications are moderated as content in a list context,
    /// primarily checking labels on the notification author and mute/block status.
    let moderateNotification
        (prefs: ModerationPrefs)
        (labels: Label list)
        (isMuted: bool)
        (isBlocked: bool)
        (isBlockedBy: bool)
        : ModerationDecision =
        moderate prefs labels None [] [] isMuted isBlocked isBlockedBy false false LabelTarget.Account None ModerationContext.ContentList

    /// Simplified moderation for a feed generator.
    /// Feed generators are moderated as content, checking labels applied to the feed itself.
    let moderateFeedGenerator
        (prefs: ModerationPrefs)
        (labels: Label list)
        (context: ModerationContext)
        : ModerationDecision =
        moderate prefs labels None [] [] false false false false false LabelTarget.Content None context

    /// Simplified moderation for a user list.
    /// User lists are moderated as content, checking labels applied to the list.
    let moderateUserList
        (prefs: ModerationPrefs)
        (labels: Label list)
        (context: ModerationContext)
        : ModerationDecision =
        moderate prefs labels None [] [] false false false false false LabelTarget.Content None context
