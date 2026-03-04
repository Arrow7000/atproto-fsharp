---
title: Ozone
category: Advanced Guides
categoryindex: 3
index: 20
description: Moderation tooling for Ozone labeler operators
keywords: fsharp, atproto, bluesky, ozone, moderation, labeler, takedown
---

# Ozone

The `Ozone` module provides convenience methods for Ozone moderation tooling (`tools.ozone.*` endpoints). These are used by labeler operators and moderation teams to take actions on content and accounts.

All Ozone functions require a `serviceDid` parameter -- the DID of the Ozone moderation service. The proxy header is applied automatically; you do not need to configure it manually.

All examples use `taskResult {}`. See the [Error Handling guide](error-handling.html) for details.

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax
```

## Key Types

### OzoneSubject

The target of a moderation action:

```fsharp
type OzoneSubject =
    | Account of Did      // an entire account
    | Record of PostRef   // a specific record (post, etc.)
```

### ModerationAction

The action to perform:

| Case | Description |
|---|---|
| `Takedown (comment, durationInHours)` | Take down a subject permanently or temporarily |
| `ReverseTakedown (comment)` | Reverse a previous takedown |
| `Acknowledge (comment)` | Acknowledge a report |
| `Escalate (comment)` | Escalate for higher-level review |
| `Label (comment, createLabels, negateLabels)` | Add or remove labels |
| `Comment (comment, sticky)` | Add a comment to a subject |
| `Mute (comment, durationInHours)` | Mute incoming reports on a subject |
| `Unmute (comment)` | Unmute incoming reports |
| `MuteReporter (comment, durationInHours)` | Mute incoming reports from a reporter |
| `UnmuteReporter (comment)` | Unmute a reporter |
| `Tag (comment, add, remove)` | Add or remove tags |
| `ResolveAppeal (comment)` | Resolve an appeal |

### TeamRole

```fsharp
type TeamRole = Admin | Moderator | Triage | Verifier
```

### TeamMember

```fsharp
type TeamMember =
    { Did : Did
      Role : TeamRole
      Disabled : bool
      CreatedAt : DateTimeOffset option
      UpdatedAt : DateTimeOffset option }
```

## Moderation Events

| Function | Description |
|---|---|
| `Ozone.emitEvent` | Emit a moderation event on a subject |
| `Ozone.getEvent` | Get a moderation event by ID |
| `Ozone.queryEvents` | Query moderation events with optional filters |
| `Ozone.queryStatuses` | Query the moderation review queue |
| `Ozone.getSubjects` | Get detailed subject information |

### Taking Down a Post and Adding a Label

```fsharp
taskResult {
    let! agent = Bluesky.login "https://bsky.social" "mod-handle.bsky.social" "app-password"

    // Take down a post
    let! _ =
        Ozone.emitEvent agent serviceDid
            (OzoneSubject.Record offendingPostRef)
            (ModerationAction.Takedown (Some "Violates community guidelines", None))

    // Add a label to an account
    let! _ =
        Ozone.emitEvent agent serviceDid
            (OzoneSubject.Account userDid)
            (ModerationAction.Label (Some "Content warning", [ "nsfw" ], []))

    // Reverse the takedown later
    let! _ =
        Ozone.emitEvent agent serviceDid
            (OzoneSubject.Record offendingPostRef)
            (ModerationAction.ReverseTakedown (Some "Reviewed and cleared"))

    return ()
}
```

## Repository Inspection

| Function | Description |
|---|---|
| `Ozone.getRepo` | Get detailed moderation view for an account by DID |
| `Ozone.getRecord` | Get detailed moderation view for a record by AT-URI |
| `Ozone.searchRepos` | Search accounts in the moderation system |

```fsharp
taskResult {
    let! repoDetail = Ozone.getRepo agent serviceDid userDid
    let! recordDetail = Ozone.getRecord agent serviceDid recordUri
    let! searchResults = Ozone.searchRepos agent serviceDid "spam" (Some 10L) None
    return ()
}
```

## Team Management

| Function | Description |
|---|---|
| `Ozone.listMembers` | List moderation team members |
| `Ozone.addMember` | Add a team member with a role |
| `Ozone.removeMember` | Remove a team member |
| `Ozone.updateMember` | Update a member's role or disabled status |

```fsharp
taskResult {
    // Add a new moderator
    let! member = Ozone.addMember agent serviceDid newModeratorDid TeamRole.Moderator

    // Promote to admin
    let! _ = Ozone.updateMember agent serviceDid newModeratorDid (Some TeamRole.Admin) None

    // List all team members
    let! page = Ozone.listMembers agent serviceDid None None

    for m in page.Items do
        printfn "%s - %A" (Did.value m.Did) m.Role
}
```

## Communication Templates

Templates for standardized moderation communications:

| Function | Description |
|---|---|
| `Ozone.listTemplates` | List all communication templates |
| `Ozone.createTemplate` | Create a new template |
| `Ozone.updateTemplate` | Update an existing template |
| `Ozone.deleteTemplate` | Delete a template by ID |

```fsharp
taskResult {
    let! template =
        Ozone.createTemplate agent serviceDid
            "Spam Warning"
            "Your content was flagged as spam. Please review our community guidelines."
            "Content Policy Notice"

    printfn "Created template: %s" template.Id

    // Update the template
    let! _ = Ozone.updateTemplate agent serviceDid template.Id
                (Some "Updated Spam Warning") None None None

    // Delete it
    do! Ozone.deleteTemplate agent serviceDid template.Id
}
```
