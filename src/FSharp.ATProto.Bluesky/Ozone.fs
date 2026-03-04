namespace FSharp.ATProto.Bluesky

open System.Threading.Tasks
open FSharp.ATProto.Core
open FSharp.ATProto.Syntax

// ── Domain types ─────────────────────────────────────────────────────

/// <summary>
/// The subject of an Ozone moderation action: either an account (by DID) or a record (by AT-URI + CID).
/// </summary>
[<RequireQualifiedAccess>]
type OzoneSubject =
    | Account of Did
    | Record of PostRef

/// <summary>
/// A type-safe union of moderation events that can be emitted via <c>Ozone.emitEvent</c>.
/// Each case maps to the corresponding <c>tools.ozone.moderation.defs#modEvent*</c> type.
/// </summary>
[<RequireQualifiedAccess>]
type ModerationAction =
    /// Take down a subject permanently or temporarily.
    | Takedown of comment : string option * durationInHours : int64 option
    /// Reverse a previous takedown action.
    | ReverseTakedown of comment : string option
    /// Acknowledge a report on a subject.
    | Acknowledge of comment : string option
    /// Escalate a subject for higher-level review.
    | Escalate of comment : string option
    /// Add or remove labels on a subject.
    | Label of comment : string option * createLabels : string list * negateLabels : string list
    /// Add a comment to a subject.
    | Comment of comment : string * sticky : bool option
    /// Mute incoming reports on a subject for a duration.
    | Mute of comment : string option * durationInHours : int64
    /// Unmute incoming reports on a subject.
    | Unmute of comment : string option
    /// Mute incoming reports from a reporter account.
    | MuteReporter of comment : string option * durationInHours : int64 option
    /// Unmute incoming reports from a reporter account.
    | UnmuteReporter of comment : string option
    /// Add or remove tags on a subject.
    | Tag of comment : string option * add : string list * remove : string list
    /// Resolve an appeal on a subject.
    | ResolveAppeal of comment : string option

/// <summary>
/// The role of a team member in an Ozone moderation service.
/// </summary>
[<RequireQualifiedAccess>]
type TeamRole =
    | Admin
    | Moderator
    | Triage
    | Verifier

/// <summary>
/// A member of an Ozone moderation team.
/// </summary>
type TeamMember =
    { Did : Did
      Role : TeamRole
      Disabled : bool
      CreatedAt : System.DateTimeOffset option
      UpdatedAt : System.DateTimeOffset option }

module TeamMember =
    let internal ofRaw (m : ToolsOzoneTeam.Defs.Member) : TeamMember =
        let role =
            match m.Role with
            | ToolsOzoneTeam.Defs.MemberRole.RoleAdmin -> TeamRole.Admin
            | ToolsOzoneTeam.Defs.MemberRole.RoleModerator -> TeamRole.Moderator
            | ToolsOzoneTeam.Defs.MemberRole.RoleTriage -> TeamRole.Triage
            | ToolsOzoneTeam.Defs.MemberRole.RoleVerifier -> TeamRole.Verifier
            | _ -> TeamRole.Triage // fallback for Unknown

        { Did = m.Did
          Role = role
          Disabled = m.Disabled |> Option.defaultValue false
          CreatedAt = m.CreatedAt |> Option.map ProfileSummary.toDateTimeOffset
          UpdatedAt = m.UpdatedAt |> Option.map ProfileSummary.toDateTimeOffset }

/// <summary>
/// A communication template used by the Ozone moderation service.
/// </summary>
type CommunicationTemplate =
    { Id : string
      Name : string
      ContentMarkdown : string
      Subject : string option
      Disabled : bool
      LastUpdatedBy : Did
      CreatedAt : System.DateTimeOffset
      UpdatedAt : System.DateTimeOffset }

module CommunicationTemplate =
    let internal ofRaw (t : ToolsOzoneCommunication.Defs.TemplateView) : CommunicationTemplate =
        { Id = t.Id
          Name = t.Name
          ContentMarkdown = t.ContentMarkdown
          Subject = t.Subject
          Disabled = t.Disabled
          LastUpdatedBy = t.LastUpdatedBy
          CreatedAt = ProfileSummary.toDateTimeOffset t.CreatedAt
          UpdatedAt = ProfileSummary.toDateTimeOffset t.UpdatedAt }


// ── Ozone module ─────────────────────────────────────────────────────

/// <summary>
/// Convenience methods for Ozone moderation tooling (<c>tools.ozone.*</c> endpoints).
/// Wraps the generated XRPC types with a simplified, type-safe API.
/// <para>
/// Ozone endpoints require the agent to be proxied to the moderation service.
/// Pass the labeler/moderation service DID via the <c>serviceDid</c> parameter.
/// The proxy header (<c>atproto-proxy: {did}#atproto_labeler</c>) is applied
/// automatically -- callers do not need to configure proxy headers manually.
/// </para>
/// </summary>
module Ozone =

    /// Ensures the agent has the correct Ozone proxy header for the given service DID.
    /// Replaces any existing atproto-proxy header to avoid conflicts.
    let private ensureOzoneProxy (serviceDid : Did) (agent : AtpAgent) : AtpAgent =
        let proxyValue = sprintf "%s#atproto_labeler" (Did.value serviceDid)
        let filtered = agent.ExtraHeaders |> List.filter (fun (k, _) -> k <> "atproto-proxy")
        { agent with ExtraHeaders = ("atproto-proxy", proxyValue) :: filtered }

    let private notLoggedInError : XrpcError =
        { StatusCode = 401
          Error = Some "NotLoggedIn"
          Message = Some "No active session" }

    let private sessionDid (agent : AtpAgent) : Result<Did, XrpcError> =
        match agent.Session with
        | Some s -> Ok s.Did
        | None -> Error notLoggedInError

    let private toInputSubject (subject : OzoneSubject) : ToolsOzoneModeration.EmitEvent.InputSubjectUnion =
        match subject with
        | OzoneSubject.Account did ->
            ToolsOzoneModeration.EmitEvent.InputSubjectUnion.RepoRef { Did = did }
        | OzoneSubject.Record postRef ->
            ToolsOzoneModeration.EmitEvent.InputSubjectUnion.StrongRef
                { Uri = postRef.Uri; Cid = postRef.Cid }

    let private toInputEventUnion (action : ModerationAction) : ToolsOzoneModeration.EmitEvent.InputEventUnion =
        match action with
        | ModerationAction.Takedown (comment, duration) ->
            ToolsOzoneModeration.EmitEvent.InputEventUnion.ModEventTakedown
                { Comment = comment
                  DurationInHours = duration
                  AcknowledgeAccountSubjects = None
                  Policies = None
                  SeverityLevel = None
                  StrikeCount = None
                  StrikeExpiresAt = None
                  TargetServices = None }
        | ModerationAction.ReverseTakedown comment ->
            ToolsOzoneModeration.EmitEvent.InputEventUnion.ModEventReverseTakedown
                { Comment = comment
                  Policies = None
                  SeverityLevel = None
                  StrikeCount = None }
        | ModerationAction.Acknowledge comment ->
            ToolsOzoneModeration.EmitEvent.InputEventUnion.ModEventAcknowledge
                { Comment = comment
                  AcknowledgeAccountSubjects = None }
        | ModerationAction.Escalate comment ->
            ToolsOzoneModeration.EmitEvent.InputEventUnion.ModEventEscalate
                { Comment = comment }
        | ModerationAction.Label (comment, createLabels, negateLabels) ->
            ToolsOzoneModeration.EmitEvent.InputEventUnion.ModEventLabel
                { Comment = comment
                  CreateLabelVals = createLabels
                  NegateLabelVals = negateLabels
                  DurationInHours = None }
        | ModerationAction.Comment (comment, sticky) ->
            ToolsOzoneModeration.EmitEvent.InputEventUnion.ModEventComment
                { Comment = Some comment
                  Sticky = sticky }
        | ModerationAction.Mute (comment, durationInHours) ->
            ToolsOzoneModeration.EmitEvent.InputEventUnion.ModEventMute
                { Comment = comment
                  DurationInHours = durationInHours }
        | ModerationAction.Unmute comment ->
            ToolsOzoneModeration.EmitEvent.InputEventUnion.ModEventUnmute
                { Comment = comment }
        | ModerationAction.MuteReporter (comment, durationInHours) ->
            ToolsOzoneModeration.EmitEvent.InputEventUnion.ModEventMuteReporter
                { Comment = comment
                  DurationInHours = durationInHours }
        | ModerationAction.UnmuteReporter comment ->
            ToolsOzoneModeration.EmitEvent.InputEventUnion.ModEventUnmuteReporter
                { Comment = comment }
        | ModerationAction.Tag (comment, add, remove) ->
            ToolsOzoneModeration.EmitEvent.InputEventUnion.ModEventTag
                { Comment = comment
                  Add = add
                  Remove = remove }
        | ModerationAction.ResolveAppeal comment ->
            ToolsOzoneModeration.EmitEvent.InputEventUnion.ModEventResolveAppeal
                { Comment = comment }

    let private toTeamInputRole (role : TeamRole) : ToolsOzoneTeam.AddMember.InputRole =
        match role with
        | TeamRole.Admin -> ToolsOzoneTeam.AddMember.InputRole.RoleAdmin
        | TeamRole.Moderator -> ToolsOzoneTeam.AddMember.InputRole.RoleModerator
        | TeamRole.Triage -> ToolsOzoneTeam.AddMember.InputRole.RoleTriage
        | TeamRole.Verifier -> ToolsOzoneTeam.AddMember.InputRole.RoleVerifier

    let private toUpdateMemberInputRole (role : TeamRole) : ToolsOzoneTeam.UpdateMember.InputRole =
        match role with
        | TeamRole.Admin -> ToolsOzoneTeam.UpdateMember.InputRole.RoleAdmin
        | TeamRole.Moderator -> ToolsOzoneTeam.UpdateMember.InputRole.RoleModerator
        | TeamRole.Triage -> ToolsOzoneTeam.UpdateMember.InputRole.RoleTriage
        | TeamRole.Verifier -> ToolsOzoneTeam.UpdateMember.InputRole.RoleVerifier

    // ── Moderation events ────────────────────────────────────────────

    /// <summary>
    /// Emit a moderation event on a subject. This is the primary way to take moderation
    /// actions in Ozone: takedowns, labels, flags, acknowledgments, escalations, and more.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="serviceDid">The DID of the Ozone moderation service to proxy to.</param>
    /// <param name="subject">The subject of the moderation action (account or record).</param>
    /// <param name="action">The moderation action to perform.</param>
    /// <returns>The emitted moderation event view, or an <see cref="XrpcError"/>.</returns>
    let emitEvent
        (agent : AtpAgent)
        (serviceDid : Did)
        (subject : OzoneSubject)
        (action : ModerationAction)
        : Task<Result<ToolsOzoneModeration.Defs.ModEventView, XrpcError>> =
        match sessionDid agent with
        | Error e -> Task.FromResult(Error e)
        | Ok did ->
            let proxied = ensureOzoneProxy serviceDid agent
            ToolsOzoneModeration.EmitEvent.call
                proxied
                { CreatedBy = did
                  Event = toInputEventUnion action
                  Subject = toInputSubject subject
                  SubjectBlobCids = None
                  ExternalId = None
                  ModTool = None }

    /// <summary>
    /// Get the details of a specific moderation event by its ID.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="serviceDid">The DID of the Ozone moderation service.</param>
    /// <param name="eventId">The numeric ID of the moderation event.</param>
    /// <returns>The detailed event view, or an <see cref="XrpcError"/>.</returns>
    let getEvent
        (agent : AtpAgent)
        (serviceDid : Did)
        (eventId : int64)
        : Task<Result<ToolsOzoneModeration.Defs.ModEventViewDetail, XrpcError>> =
        let proxied = ensureOzoneProxy serviceDid agent
        ToolsOzoneModeration.GetEvent.query proxied { Id = eventId }

    /// <summary>
    /// Query moderation events, optionally filtered by subject, type, or date range.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="serviceDid">The DID of the Ozone moderation service.</param>
    /// <param name="subject">Optional subject URI to filter events for.</param>
    /// <param name="limit">Maximum number of events to return.</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <returns>A page of moderation event views, or an <see cref="XrpcError"/>.</returns>
    let queryEvents
        (agent : AtpAgent)
        (serviceDid : Did)
        (subject : Uri option)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<ToolsOzoneModeration.Defs.ModEventView>, XrpcError>> =
        task {
            let proxied = ensureOzoneProxy serviceDid agent
            let! result =
                ToolsOzoneModeration.QueryEvents.query
                    proxied
                    { Subject = subject
                      Limit = limit
                      Cursor = cursor
                      Types = None
                      CreatedBy = None
                      SortDirection = None
                      HasComment = None
                      Comment = None
                      AddedLabels = None
                      RemovedLabels = None
                      AddedTags = None
                      RemovedTags = None
                      ReportTypes = None
                      IncludeAllUserRecords = None
                      CreatedAfter = None
                      CreatedBefore = None
                      SubjectType = None
                      Collections = None
                      Policies = None
                      ModTool = None
                      BatchId = None
                      WithStrike = None
                      AgeAssuranceState = None }
            return
                result
                |> Result.map (fun output ->
                    { Items = output.Events; Cursor = output.Cursor })
        }

    // ── Subject management ───────────────────────────────────────────

    /// <summary>
    /// Query subject statuses in the moderation queue. This is the primary way to
    /// list items in the moderation review queue.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="serviceDid">The DID of the Ozone moderation service.</param>
    /// <param name="reviewState">Optional review state filter (e.g. ReviewOpen, ReviewEscalated).</param>
    /// <param name="limit">Maximum number of statuses to return.</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <returns>A page of subject status views, or an <see cref="XrpcError"/>.</returns>
    let queryStatuses
        (agent : AtpAgent)
        (serviceDid : Did)
        (reviewState : ToolsOzoneModeration.QueryStatuses.ParamsReviewState option)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<ToolsOzoneModeration.Defs.SubjectStatusView>, XrpcError>> =
        task {
            let proxied = ensureOzoneProxy serviceDid agent
            let! result =
                ToolsOzoneModeration.QueryStatuses.query
                    proxied
                    { ReviewState = reviewState
                      Limit = limit
                      Cursor = cursor
                      Subject = None
                      Appealed = None
                      Comment = None
                      Collections = None
                      ExcludeTags = None
                      HostingDeletedAfter = None
                      HostingDeletedBefore = None
                      HostingStatuses = None
                      HostingUpdatedAfter = None
                      HostingUpdatedBefore = None
                      IgnoreSubjects = None
                      IncludeAllUserRecords = None
                      IncludeMuted = None
                      LastReviewedBy = None
                      MinAccountSuspendCount = None
                      MinPriorityScore = None
                      MinReportedRecordsCount = None
                      MinStrikeCount = None
                      MinTakendownRecordsCount = None
                      OnlyMuted = None
                      QueueCount = None
                      QueueIndex = None
                      QueueSeed = None
                      ReportedAfter = None
                      ReportedBefore = None
                      ReviewedAfter = None
                      ReviewedBefore = None
                      SortDirection = None
                      SortField = None
                      SubjectType = None
                      Tags = None
                      Takendown = None
                      AgeAssuranceState = None }
            return
                result
                |> Result.map (fun output ->
                    { Items = output.SubjectStatuses; Cursor = output.Cursor })
        }

    /// <summary>
    /// Get detailed subject information for one or more subjects (by DID or AT-URI string).
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="serviceDid">The DID of the Ozone moderation service.</param>
    /// <param name="subjects">A list of subject identifiers (DID strings or AT-URI strings).</param>
    /// <returns>A list of subject views, or an <see cref="XrpcError"/>.</returns>
    let getSubjects
        (agent : AtpAgent)
        (serviceDid : Did)
        (subjects : string list)
        : Task<Result<ToolsOzoneModeration.Defs.SubjectView list, XrpcError>> =
        task {
            let proxied = ensureOzoneProxy serviceDid agent
            let! result =
                ToolsOzoneModeration.GetSubjects.query proxied { Subjects = subjects }
            return result |> Result.map (fun output -> output.Subjects)
        }

    /// <summary>
    /// Get the detailed moderation view for a specific repo (account) by DID.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="serviceDid">The DID of the Ozone moderation service.</param>
    /// <param name="did">The DID of the account to look up.</param>
    /// <returns>The detailed repo view, or an <see cref="XrpcError"/>.</returns>
    let getRepo
        (agent : AtpAgent)
        (serviceDid : Did)
        (did : Did)
        : Task<Result<ToolsOzoneModeration.Defs.RepoViewDetail, XrpcError>> =
        let proxied = ensureOzoneProxy serviceDid agent
        ToolsOzoneModeration.GetRepo.query proxied { Did = did }

    /// <summary>
    /// Get the detailed moderation view for a specific record by AT-URI.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="serviceDid">The DID of the Ozone moderation service.</param>
    /// <param name="uri">The AT-URI of the record to look up.</param>
    /// <returns>The detailed record view, or an <see cref="XrpcError"/>.</returns>
    let getRecord
        (agent : AtpAgent)
        (serviceDid : Did)
        (uri : AtUri)
        : Task<Result<ToolsOzoneModeration.Defs.RecordViewDetail, XrpcError>> =
        let proxied = ensureOzoneProxy serviceDid agent
        ToolsOzoneModeration.GetRecord.query proxied { Uri = uri; Cid = None }

    /// <summary>
    /// Search repos (accounts) in the moderation system by query string.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="serviceDid">The DID of the Ozone moderation service.</param>
    /// <param name="query">The search query string.</param>
    /// <param name="limit">Maximum number of results to return.</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <returns>A page of repo views, or an <see cref="XrpcError"/>.</returns>
    let searchRepos
        (agent : AtpAgent)
        (serviceDid : Did)
        (query : string)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<ToolsOzoneModeration.Defs.RepoView>, XrpcError>> =
        task {
            let proxied = ensureOzoneProxy serviceDid agent
            let! result =
                ToolsOzoneModeration.SearchRepos.query
                    proxied
                    { Q = Some query; Term = None; Limit = limit; Cursor = cursor }
            return
                result
                |> Result.map (fun output ->
                    { Items = output.Repos; Cursor = output.Cursor })
        }

    // ── Team management ──────────────────────────────────────────────

    /// <summary>
    /// List the members of the Ozone moderation team.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="serviceDid">The DID of the Ozone moderation service.</param>
    /// <param name="limit">Maximum number of members to return.</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <returns>A page of team members, or an <see cref="XrpcError"/>.</returns>
    let listMembers
        (agent : AtpAgent)
        (serviceDid : Did)
        (limit : int64 option)
        (cursor : string option)
        : Task<Result<Page<TeamMember>, XrpcError>> =
        task {
            let proxied = ensureOzoneProxy serviceDid agent
            let! result =
                ToolsOzoneTeam.ListMembers.query
                    proxied
                    { Limit = limit; Cursor = cursor; Q = None; Roles = None; Disabled = None }
            return
                result
                |> Result.map (fun output ->
                    { Items = output.Members |> List.map TeamMember.ofRaw
                      Cursor = output.Cursor })
        }

    /// <summary>
    /// Add a new member to the Ozone moderation team.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="serviceDid">The DID of the Ozone moderation service.</param>
    /// <param name="memberDid">The DID of the user to add as a team member.</param>
    /// <param name="role">The role to assign to the new member.</param>
    /// <returns>The newly created team member, or an <see cref="XrpcError"/>.</returns>
    let addMember
        (agent : AtpAgent)
        (serviceDid : Did)
        (memberDid : Did)
        (role : TeamRole)
        : Task<Result<TeamMember, XrpcError>> =
        task {
            let proxied = ensureOzoneProxy serviceDid agent
            let! result =
                ToolsOzoneTeam.AddMember.call
                    proxied
                    { Did = memberDid; Role = toTeamInputRole role }
            return result |> Result.map TeamMember.ofRaw
        }

    /// <summary>
    /// Remove a member from the Ozone moderation team.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="serviceDid">The DID of the Ozone moderation service.</param>
    /// <param name="memberDid">The DID of the member to remove.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let removeMember
        (agent : AtpAgent)
        (serviceDid : Did)
        (memberDid : Did)
        : Task<Result<unit, XrpcError>> =
        let proxied = ensureOzoneProxy serviceDid agent
        ToolsOzoneTeam.DeleteMember.call proxied { Did = memberDid }

    /// <summary>
    /// Update an existing team member's role or disabled status.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="serviceDid">The DID of the Ozone moderation service.</param>
    /// <param name="memberDid">The DID of the member to update.</param>
    /// <param name="role">The new role for the member, or <c>None</c> to leave unchanged.</param>
    /// <param name="disabled">Whether the member should be disabled, or <c>None</c> to leave unchanged.</param>
    /// <returns>The updated team member, or an <see cref="XrpcError"/>.</returns>
    let updateMember
        (agent : AtpAgent)
        (serviceDid : Did)
        (memberDid : Did)
        (role : TeamRole option)
        (disabled : bool option)
        : Task<Result<TeamMember, XrpcError>> =
        task {
            let proxied = ensureOzoneProxy serviceDid agent
            let! result =
                ToolsOzoneTeam.UpdateMember.call
                    proxied
                    { Did = memberDid
                      Role = role |> Option.map toUpdateMemberInputRole
                      Disabled = disabled }
            return result |> Result.map TeamMember.ofRaw
        }

    // ── Communication templates ──────────────────────────────────────

    /// <summary>
    /// List all communication templates in the Ozone moderation service.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="serviceDid">The DID of the Ozone moderation service.</param>
    /// <returns>A list of communication templates, or an <see cref="XrpcError"/>.</returns>
    let listTemplates
        (agent : AtpAgent)
        (serviceDid : Did)
        : Task<Result<CommunicationTemplate list, XrpcError>> =
        task {
            let proxied = ensureOzoneProxy serviceDid agent
            let! result = ToolsOzoneCommunication.ListTemplates.query proxied
            return
                result
                |> Result.map (fun output ->
                    output.CommunicationTemplates |> List.map CommunicationTemplate.ofRaw)
        }

    /// <summary>
    /// Create a new communication template.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="serviceDid">The DID of the Ozone moderation service.</param>
    /// <param name="name">The name of the template.</param>
    /// <param name="contentMarkdown">The template content in Markdown format.</param>
    /// <param name="subject">Optional subject line for the template.</param>
    /// <returns>The newly created template, or an <see cref="XrpcError"/>.</returns>
    let createTemplate
        (agent : AtpAgent)
        (serviceDid : Did)
        (name : string)
        (contentMarkdown : string)
        (subject : string)
        : Task<Result<CommunicationTemplate, XrpcError>> =
        task {
            let proxied = ensureOzoneProxy serviceDid agent
            let! result =
                ToolsOzoneCommunication.CreateTemplate.call
                    proxied
                    { Name = name
                      ContentMarkdown = contentMarkdown
                      Subject = subject
                      CreatedBy = None
                      Lang = None }
            return result |> Result.map CommunicationTemplate.ofRaw
        }

    /// <summary>
    /// Update an existing communication template.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="serviceDid">The DID of the Ozone moderation service.</param>
    /// <param name="id">The ID of the template to update.</param>
    /// <param name="name">New name, or <c>None</c> to leave unchanged.</param>
    /// <param name="contentMarkdown">New content, or <c>None</c> to leave unchanged.</param>
    /// <param name="subject">New subject, or <c>None</c> to leave unchanged.</param>
    /// <param name="disabled">Whether to disable the template, or <c>None</c> to leave unchanged.</param>
    /// <returns>The updated template, or an <see cref="XrpcError"/>.</returns>
    let updateTemplate
        (agent : AtpAgent)
        (serviceDid : Did)
        (id : string)
        (name : string option)
        (contentMarkdown : string option)
        (subject : string option)
        (disabled : bool option)
        : Task<Result<CommunicationTemplate, XrpcError>> =
        task {
            let proxied = ensureOzoneProxy serviceDid agent
            let! result =
                ToolsOzoneCommunication.UpdateTemplate.call
                    proxied
                    { Id = id
                      Name = name
                      ContentMarkdown = contentMarkdown
                      Subject = subject
                      Disabled = disabled
                      Lang = None
                      UpdatedBy = None }
            return result |> Result.map CommunicationTemplate.ofRaw
        }

    /// <summary>
    /// Delete a communication template by ID.
    /// </summary>
    /// <param name="agent">An authenticated <see cref="AtpAgent"/>.</param>
    /// <param name="serviceDid">The DID of the Ozone moderation service.</param>
    /// <param name="id">The ID of the template to delete.</param>
    /// <returns><c>Ok ()</c> on success, or an <see cref="XrpcError"/>.</returns>
    let deleteTemplate
        (agent : AtpAgent)
        (serviceDid : Did)
        (id : string)
        : Task<Result<unit, XrpcError>> =
        let proxied = ensureOzoneProxy serviceDid agent
        ToolsOzoneCommunication.DeleteTemplate.call proxied { Id = id }
