namespace rec FSharp.ATProto.Bluesky

open System.Text.Json
open System.Text.Json.Serialization

module AppBsky =
    begin end

module AppBskyRichtext =
    module Facet =
        /// Annotation of a sub-string within rich text.
        type Facet =
            { [<JsonPropertyName("features")>]
              Features: JsonElement list
              [<JsonPropertyName("index")>]
              Index: Facet.ByteSlice }

        /// Specifies the sub-string range a facet feature applies to. Start index is inclusive, end index is exclusive. Indices are zero-indexed, counting bytes of the UTF-8 encoded text. NOTE: some languages, like Javascript, use UTF-16 or Unicode codepoints for string slice indexing; in these languages, convert to byte arrays before working with facets.
        type ByteSlice =
            { [<JsonPropertyName("byteEnd")>]
              ByteEnd: int64
              [<JsonPropertyName("byteStart")>]
              ByteStart: int64 }

        /// Facet feature for a URL. The text URL may have been simplified or truncated, but the facet reference should be a complete URL.
        type Link =
            { [<JsonPropertyName("uri")>]
              Uri: string }

        /// Facet feature for mention of another account. The text is usually a handle, including a '@' prefix, but the facet reference is a DID.
        type Mention =
            { [<JsonPropertyName("did")>]
              Did: string }

        /// Facet feature for a hashtag. The text usually includes a '#' prefix, but the facet reference should not (except in the case of 'double hash tags').
        type Tag =
            { [<JsonPropertyName("tag")>]
              Tag: string }

module ComAtprotoLabel =
    module Defs =
        /// Metadata tag on an atproto resource (eg, repo or record).
        type Label =
            { [<JsonPropertyName("cid")>]
              Cid: string option
              [<JsonPropertyName("cts")>]
              Cts: string
              [<JsonPropertyName("exp")>]
              Exp: string option
              [<JsonPropertyName("neg")>]
              Neg: bool option
              [<JsonPropertyName("sig")>]
              Sig: byte[] option
              [<JsonPropertyName("src")>]
              Src: string
              [<JsonPropertyName("uri")>]
              Uri: string
              [<JsonPropertyName("val")>]
              Val: string
              [<JsonPropertyName("ver")>]
              Ver: int64 option }

        type LabelValue = string

        /// Declares a label value and its expected interpretations and behaviors.
        type LabelValueDefinition =
            { [<JsonPropertyName("adultOnly")>]
              AdultOnly: bool option
              [<JsonPropertyName("blurs")>]
              Blurs: string
              [<JsonPropertyName("defaultSetting")>]
              DefaultSetting: string option
              [<JsonPropertyName("identifier")>]
              Identifier: string
              [<JsonPropertyName("locales")>]
              Locales: Defs.LabelValueDefinitionStrings list
              [<JsonPropertyName("severity")>]
              Severity: string }

        /// Strings which describe the label in the UI, localized into a specific language.
        type LabelValueDefinitionStrings =
            { [<JsonPropertyName("description")>]
              Description: string
              [<JsonPropertyName("lang")>]
              Lang: string
              [<JsonPropertyName("name")>]
              Name: string }

        /// Metadata tag on an atproto record, published by the author within the record. Note that schemas should use #selfLabels, not #selfLabel.
        type SelfLabel =
            { [<JsonPropertyName("val")>]
              Val: string }

        /// Metadata tags on an atproto record, published by the author within the record.
        type SelfLabels =
            { [<JsonPropertyName("values")>]
              Values: Defs.SelfLabel list }

    module QueryLabels =
        [<Literal>]
        let TypeId = "com.atproto.label.queryLabels"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("sources")>]
              Sources: string list option
              [<JsonPropertyName("uriPatterns")>]
              UriPatterns: string list }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("labels")>]
              Labels: Defs.Label list }

    module SubscribeLabels =
        [<Literal>]
        let TypeId = "com.atproto.label.subscribeLabels"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: int64 option }

        [<JsonFSharpConverter(JsonUnionEncoding.InternalTag ||| JsonUnionEncoding.UnwrapSingleFieldCases, unionTagName = "$type")>]
        type Message =
            | [<JsonName("com.atproto.label.subscribeLabels#labels")>] Labels of SubscribeLabels.Labels
            | [<JsonName("com.atproto.label.subscribeLabels#info")>] Info of SubscribeLabels.Info
            | Unknown of string * System.Text.Json.JsonElement

        module Errors =
            [<Literal>]
            let FutureCursor = "FutureCursor"

        type Info =
            { [<JsonPropertyName("message")>]
              Message: string option
              [<JsonPropertyName("name")>]
              Name: string }

        type Labels =
            { [<JsonPropertyName("labels")>]
              Labels: Defs.Label list
              [<JsonPropertyName("seq")>]
              Seq: int64 }

module ComAtprotoRepo =
    module ApplyWrites =
        [<Literal>]
        let TypeId = "com.atproto.repo.applyWrites"

        type Input =
            { [<JsonPropertyName("repo")>]
              Repo: string
              [<JsonPropertyName("swapCommit")>]
              SwapCommit: string option
              [<JsonPropertyName("validate")>]
              Validate: bool option
              [<JsonPropertyName("writes")>]
              Writes: JsonElement list }

        type Output =
            { [<JsonPropertyName("commit")>]
              Commit: Defs.CommitMeta option
              [<JsonPropertyName("results")>]
              Results: JsonElement list option }

        module Errors =
            [<Literal>]
            let InvalidSwap = "InvalidSwap"

        /// Operation which creates a new record.
        type Create =
            { [<JsonPropertyName("collection")>]
              Collection: string
              [<JsonPropertyName("rkey")>]
              Rkey: string option
              [<JsonPropertyName("value")>]
              Value: JsonElement }

        type CreateResult =
            { [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("uri")>]
              Uri: string
              [<JsonPropertyName("validationStatus")>]
              ValidationStatus: string option }

        /// Operation which deletes an existing record.
        type Delete =
            { [<JsonPropertyName("collection")>]
              Collection: string
              [<JsonPropertyName("rkey")>]
              Rkey: string }

        /// Operation which updates an existing record.
        type Update =
            { [<JsonPropertyName("collection")>]
              Collection: string
              [<JsonPropertyName("rkey")>]
              Rkey: string
              [<JsonPropertyName("value")>]
              Value: JsonElement }

        type UpdateResult =
            { [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("uri")>]
              Uri: string
              [<JsonPropertyName("validationStatus")>]
              ValidationStatus: string option }

    module CreateRecord =
        [<Literal>]
        let TypeId = "com.atproto.repo.createRecord"

        type Input =
            { [<JsonPropertyName("collection")>]
              Collection: string
              [<JsonPropertyName("record")>]
              Record: JsonElement
              [<JsonPropertyName("repo")>]
              Repo: string
              [<JsonPropertyName("rkey")>]
              Rkey: string option
              [<JsonPropertyName("swapCommit")>]
              SwapCommit: string option
              [<JsonPropertyName("validate")>]
              Validate: bool option }

        type Output =
            { [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("commit")>]
              Commit: Defs.CommitMeta option
              [<JsonPropertyName("uri")>]
              Uri: string
              [<JsonPropertyName("validationStatus")>]
              ValidationStatus: string option }

        module Errors =
            [<Literal>]
            let InvalidSwap = "InvalidSwap"

    module Defs =
        type CommitMeta =
            { [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("rev")>]
              Rev: string }

    module DeleteRecord =
        [<Literal>]
        let TypeId = "com.atproto.repo.deleteRecord"

        type Input =
            { [<JsonPropertyName("collection")>]
              Collection: string
              [<JsonPropertyName("repo")>]
              Repo: string
              [<JsonPropertyName("rkey")>]
              Rkey: string
              [<JsonPropertyName("swapCommit")>]
              SwapCommit: string option
              [<JsonPropertyName("swapRecord")>]
              SwapRecord: string option }

        type Output =
            { [<JsonPropertyName("commit")>]
              Commit: Defs.CommitMeta option }

        module Errors =
            [<Literal>]
            let InvalidSwap = "InvalidSwap"

    module DescribeRepo =
        [<Literal>]
        let TypeId = "com.atproto.repo.describeRepo"

        type Params =
            { [<JsonPropertyName("repo")>]
              Repo: string }

        type Output =
            { [<JsonPropertyName("collections")>]
              Collections: string list
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("didDoc")>]
              DidDoc: JsonElement
              [<JsonPropertyName("handle")>]
              Handle: string
              [<JsonPropertyName("handleIsCorrect")>]
              HandleIsCorrect: bool }

    module GetRecord =
        [<Literal>]
        let TypeId = "com.atproto.repo.getRecord"

        type Params =
            { [<JsonPropertyName("cid")>]
              Cid: string option
              [<JsonPropertyName("collection")>]
              Collection: string
              [<JsonPropertyName("repo")>]
              Repo: string
              [<JsonPropertyName("rkey")>]
              Rkey: string }

        type Output =
            { [<JsonPropertyName("cid")>]
              Cid: string option
              [<JsonPropertyName("uri")>]
              Uri: string
              [<JsonPropertyName("value")>]
              Value: JsonElement }

        module Errors =
            [<Literal>]
            let RecordNotFound = "RecordNotFound"

    module ImportRepo =
        [<Literal>]
        let TypeId = "com.atproto.repo.importRepo"

    module ListMissingBlobs =
        [<Literal>]
        let TypeId = "com.atproto.repo.listMissingBlobs"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("blobs")>]
              Blobs: ListMissingBlobs.RecordBlob list
              [<JsonPropertyName("cursor")>]
              Cursor: string option }

        type RecordBlob =
            { [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("recordUri")>]
              RecordUri: string }

    module ListRecords =
        [<Literal>]
        let TypeId = "com.atproto.repo.listRecords"

        type Params =
            { [<JsonPropertyName("collection")>]
              Collection: string
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("repo")>]
              Repo: string
              [<JsonPropertyName("reverse")>]
              Reverse: bool option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("records")>]
              Records: ListRecords.Record list }

        type Record =
            { [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("uri")>]
              Uri: string
              [<JsonPropertyName("value")>]
              Value: JsonElement }

    module PutRecord =
        [<Literal>]
        let TypeId = "com.atproto.repo.putRecord"

        type Input =
            { [<JsonPropertyName("collection")>]
              Collection: string
              [<JsonPropertyName("record")>]
              Record: JsonElement
              [<JsonPropertyName("repo")>]
              Repo: string
              [<JsonPropertyName("rkey")>]
              Rkey: string
              [<JsonPropertyName("swapCommit")>]
              SwapCommit: string option
              [<JsonPropertyName("swapRecord")>]
              SwapRecord: string option
              [<JsonPropertyName("validate")>]
              Validate: bool option }

        type Output =
            { [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("commit")>]
              Commit: Defs.CommitMeta option
              [<JsonPropertyName("uri")>]
              Uri: string
              [<JsonPropertyName("validationStatus")>]
              ValidationStatus: string option }

        module Errors =
            [<Literal>]
            let InvalidSwap = "InvalidSwap"

    module StrongRef =
        type StrongRef =
            { [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("uri")>]
              Uri: string }

    module UploadBlob =
        [<Literal>]
        let TypeId = "com.atproto.repo.uploadBlob"

        type Output =
            { [<JsonPropertyName("blob")>]
              Blob: JsonElement }

module AppBskyGraph =
    module Block =
        [<Literal>]
        let TypeId = "app.bsky.graph.block"

        /// Record declaring a 'block' relationship against another account. NOTE: blocks are public in Bluesky; see blog posts for details.
        type Block =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("subject")>]
              Subject: string }

    module Defs =
        [<Literal>]
        let Curatelist = "app.bsky.graph.defs#curatelist"

        type ListItemView =
            { [<JsonPropertyName("subject")>]
              Subject: AppBskyActor.Defs.ProfileView
              [<JsonPropertyName("uri")>]
              Uri: string }

        type ListPurpose = string

        type ListView =
            { [<JsonPropertyName("avatar")>]
              Avatar: string option
              [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("creator")>]
              Creator: AppBskyActor.Defs.ProfileView
              [<JsonPropertyName("description")>]
              Description: string option
              [<JsonPropertyName("descriptionFacets")>]
              DescriptionFacets: AppBskyRichtext.Facet.Facet list option
              [<JsonPropertyName("indexedAt")>]
              IndexedAt: string
              [<JsonPropertyName("labels")>]
              Labels: ComAtprotoLabel.Defs.Label list option
              [<JsonPropertyName("listItemCount")>]
              ListItemCount: int64 option
              [<JsonPropertyName("name")>]
              Name: string
              [<JsonPropertyName("purpose")>]
              Purpose: Defs.ListPurpose
              [<JsonPropertyName("uri")>]
              Uri: string
              [<JsonPropertyName("viewer")>]
              Viewer: Defs.ListViewerState option }

        type ListViewBasic =
            { [<JsonPropertyName("avatar")>]
              Avatar: string option
              [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("indexedAt")>]
              IndexedAt: string option
              [<JsonPropertyName("labels")>]
              Labels: ComAtprotoLabel.Defs.Label list option
              [<JsonPropertyName("listItemCount")>]
              ListItemCount: int64 option
              [<JsonPropertyName("name")>]
              Name: string
              [<JsonPropertyName("purpose")>]
              Purpose: Defs.ListPurpose
              [<JsonPropertyName("uri")>]
              Uri: string
              [<JsonPropertyName("viewer")>]
              Viewer: Defs.ListViewerState option }

        type ListViewerState =
            { [<JsonPropertyName("blocked")>]
              Blocked: string option
              [<JsonPropertyName("muted")>]
              Muted: bool option }

        [<Literal>]
        let Modlist = "app.bsky.graph.defs#modlist"

        /// indicates that a handle or DID could not be resolved
        type NotFoundActor =
            { [<JsonPropertyName("actor")>]
              Actor: string
              [<JsonPropertyName("notFound")>]
              NotFound: bool }

        [<Literal>]
        let Referencelist = "app.bsky.graph.defs#referencelist"

        /// lists the bi-directional graph relationships between one actor (not indicated in the object), and the target actors (the DID included in the object)
        type Relationship =
            { [<JsonPropertyName("blockedBy")>]
              BlockedBy: string option
              [<JsonPropertyName("blockedByList")>]
              BlockedByList: string option
              [<JsonPropertyName("blocking")>]
              Blocking: string option
              [<JsonPropertyName("blockingByList")>]
              BlockingByList: string option
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("followedBy")>]
              FollowedBy: string option
              [<JsonPropertyName("following")>]
              Following: string option }

        type StarterPackView =
            { [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("creator")>]
              Creator: AppBskyActor.Defs.ProfileViewBasic
              [<JsonPropertyName("feeds")>]
              Feeds: AppBskyFeed.Defs.GeneratorView list option
              [<JsonPropertyName("indexedAt")>]
              IndexedAt: string
              [<JsonPropertyName("joinedAllTimeCount")>]
              JoinedAllTimeCount: int64 option
              [<JsonPropertyName("joinedWeekCount")>]
              JoinedWeekCount: int64 option
              [<JsonPropertyName("labels")>]
              Labels: ComAtprotoLabel.Defs.Label list option
              [<JsonPropertyName("list")>]
              List: Defs.ListViewBasic option
              [<JsonPropertyName("listItemsSample")>]
              ListItemsSample: Defs.ListItemView list option
              [<JsonPropertyName("record")>]
              Record: JsonElement
              [<JsonPropertyName("uri")>]
              Uri: string }

        type StarterPackViewBasic =
            { [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("creator")>]
              Creator: AppBskyActor.Defs.ProfileViewBasic
              [<JsonPropertyName("indexedAt")>]
              IndexedAt: string
              [<JsonPropertyName("joinedAllTimeCount")>]
              JoinedAllTimeCount: int64 option
              [<JsonPropertyName("joinedWeekCount")>]
              JoinedWeekCount: int64 option
              [<JsonPropertyName("labels")>]
              Labels: ComAtprotoLabel.Defs.Label list option
              [<JsonPropertyName("listItemCount")>]
              ListItemCount: int64 option
              [<JsonPropertyName("record")>]
              Record: JsonElement
              [<JsonPropertyName("uri")>]
              Uri: string }

    module Follow =
        [<Literal>]
        let TypeId = "app.bsky.graph.follow"

        /// Record declaring a social 'follow' relationship of another account. Duplicate follows will be ignored by the AppView.
        type Follow =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("subject")>]
              Subject: string
              [<JsonPropertyName("via")>]
              Via: ComAtprotoRepo.StrongRef.StrongRef option }

    module GetActorStarterPacks =
        [<Literal>]
        let TypeId = "app.bsky.graph.getActorStarterPacks"

        type Params =
            { [<JsonPropertyName("actor")>]
              Actor: string
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("starterPacks")>]
              StarterPacks: Defs.StarterPackViewBasic list }

    module GetBlocks =
        [<Literal>]
        let TypeId = "app.bsky.graph.getBlocks"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("blocks")>]
              Blocks: AppBskyActor.Defs.ProfileView list
              [<JsonPropertyName("cursor")>]
              Cursor: string option }

    module GetFollowers =
        [<Literal>]
        let TypeId = "app.bsky.graph.getFollowers"

        type Params =
            { [<JsonPropertyName("actor")>]
              Actor: string
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("followers")>]
              Followers: AppBskyActor.Defs.ProfileView list
              [<JsonPropertyName("subject")>]
              Subject: AppBskyActor.Defs.ProfileView }

    module GetFollows =
        [<Literal>]
        let TypeId = "app.bsky.graph.getFollows"

        type Params =
            { [<JsonPropertyName("actor")>]
              Actor: string
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("follows")>]
              Follows: AppBskyActor.Defs.ProfileView list
              [<JsonPropertyName("subject")>]
              Subject: AppBskyActor.Defs.ProfileView }

    module GetKnownFollowers =
        [<Literal>]
        let TypeId = "app.bsky.graph.getKnownFollowers"

        type Params =
            { [<JsonPropertyName("actor")>]
              Actor: string
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("followers")>]
              Followers: AppBskyActor.Defs.ProfileView list
              [<JsonPropertyName("subject")>]
              Subject: AppBskyActor.Defs.ProfileView }

    module GetList =
        [<Literal>]
        let TypeId = "app.bsky.graph.getList"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("list")>]
              List: string }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("items")>]
              Items: Defs.ListItemView list
              [<JsonPropertyName("list")>]
              List: Defs.ListView }

    module GetListBlocks =
        [<Literal>]
        let TypeId = "app.bsky.graph.getListBlocks"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("lists")>]
              Lists: Defs.ListView list }

    module GetListMutes =
        [<Literal>]
        let TypeId = "app.bsky.graph.getListMutes"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("lists")>]
              Lists: Defs.ListView list }

    module GetLists =
        [<Literal>]
        let TypeId = "app.bsky.graph.getLists"

        type Params =
            { [<JsonPropertyName("actor")>]
              Actor: string
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("purposes")>]
              Purposes: string list option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("lists")>]
              Lists: Defs.ListView list }

    module GetListsWithMembership =
        [<Literal>]
        let TypeId = "app.bsky.graph.getListsWithMembership"

        type Params =
            { [<JsonPropertyName("actor")>]
              Actor: string
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("purposes")>]
              Purposes: string list option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("listsWithMembership")>]
              ListsWithMembership: GetListsWithMembership.ListWithMembership list }

        /// A list and an optional list item indicating membership of a target user to that list.
        type ListWithMembership =
            { [<JsonPropertyName("list")>]
              List: Defs.ListView
              [<JsonPropertyName("listItem")>]
              ListItem: Defs.ListItemView option }

    module GetMutes =
        [<Literal>]
        let TypeId = "app.bsky.graph.getMutes"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("mutes")>]
              Mutes: AppBskyActor.Defs.ProfileView list }

    module GetRelationships =
        [<Literal>]
        let TypeId = "app.bsky.graph.getRelationships"

        type Params =
            { [<JsonPropertyName("actor")>]
              Actor: string
              [<JsonPropertyName("others")>]
              Others: string list option }

        type Output =
            { [<JsonPropertyName("actor")>]
              Actor: string option
              [<JsonPropertyName("relationships")>]
              Relationships: JsonElement list }

        module Errors =
            [<Literal>]
            let ActorNotFound = "ActorNotFound"

    module GetStarterPack =
        [<Literal>]
        let TypeId = "app.bsky.graph.getStarterPack"

        type Params =
            { [<JsonPropertyName("starterPack")>]
              StarterPack: string }

        type Output =
            { [<JsonPropertyName("starterPack")>]
              StarterPack: Defs.StarterPackView }

    module GetStarterPacks =
        [<Literal>]
        let TypeId = "app.bsky.graph.getStarterPacks"

        type Params =
            { [<JsonPropertyName("uris")>]
              Uris: string list }

        type Output =
            { [<JsonPropertyName("starterPacks")>]
              StarterPacks: Defs.StarterPackViewBasic list }

    module GetStarterPacksWithMembership =
        [<Literal>]
        let TypeId = "app.bsky.graph.getStarterPacksWithMembership"

        type Params =
            { [<JsonPropertyName("actor")>]
              Actor: string
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("starterPacksWithMembership")>]
              StarterPacksWithMembership: GetStarterPacksWithMembership.StarterPackWithMembership list }

        /// A starter pack and an optional list item indicating membership of a target user to that starter pack.
        type StarterPackWithMembership =
            { [<JsonPropertyName("listItem")>]
              ListItem: Defs.ListItemView option
              [<JsonPropertyName("starterPack")>]
              StarterPack: Defs.StarterPackView }

    module GetSuggestedFollowsByActor =
        [<Literal>]
        let TypeId = "app.bsky.graph.getSuggestedFollowsByActor"

        type Params =
            { [<JsonPropertyName("actor")>]
              Actor: string }

        type Output =
            { [<JsonPropertyName("isFallback")>]
              IsFallback: bool option
              [<JsonPropertyName("recId")>]
              RecId: int64 option
              [<JsonPropertyName("recIdStr")>]
              RecIdStr: string option
              [<JsonPropertyName("suggestions")>]
              Suggestions: AppBskyActor.Defs.ProfileView list }

    module List =
        [<Literal>]
        let TypeId = "app.bsky.graph.list"

        /// Record representing a list of accounts (actors). Scope includes both moderation-oriented lists and curration-oriented lists.
        type List =
            { [<JsonPropertyName("avatar")>]
              Avatar: JsonElement option
              [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("description")>]
              Description: string option
              [<JsonPropertyName("descriptionFacets")>]
              DescriptionFacets: AppBskyRichtext.Facet.Facet list option
              [<JsonPropertyName("labels")>]
              Labels: JsonElement option
              [<JsonPropertyName("name")>]
              Name: string
              [<JsonPropertyName("purpose")>]
              Purpose: Defs.ListPurpose }

    module Listblock =
        [<Literal>]
        let TypeId = "app.bsky.graph.listblock"

        /// Record representing a block relationship against an entire an entire list of accounts (actors).
        type Listblock =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("subject")>]
              Subject: string }

    module Listitem =
        [<Literal>]
        let TypeId = "app.bsky.graph.listitem"

        /// Record representing an account's inclusion on a specific list. The AppView will ignore duplicate listitem records.
        type Listitem =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("list")>]
              List: string
              [<JsonPropertyName("subject")>]
              Subject: string }

    module MuteActor =
        [<Literal>]
        let TypeId = "app.bsky.graph.muteActor"

        type Input =
            { [<JsonPropertyName("actor")>]
              Actor: string }

    module MuteActorList =
        [<Literal>]
        let TypeId = "app.bsky.graph.muteActorList"

        type Input =
            { [<JsonPropertyName("list")>]
              List: string }

    module MuteThread =
        [<Literal>]
        let TypeId = "app.bsky.graph.muteThread"

        type Input =
            { [<JsonPropertyName("root")>]
              Root: string }

    module SearchStarterPacks =
        [<Literal>]
        let TypeId = "app.bsky.graph.searchStarterPacks"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("q")>]
              Q: string }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("starterPacks")>]
              StarterPacks: Defs.StarterPackViewBasic list }

    module Starterpack =
        [<Literal>]
        let TypeId = "app.bsky.graph.starterpack"

        /// Record defining a starter pack of actors and feeds for new users.
        type Starterpack =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("description")>]
              Description: string option
              [<JsonPropertyName("descriptionFacets")>]
              DescriptionFacets: AppBskyRichtext.Facet.Facet list option
              [<JsonPropertyName("feeds")>]
              Feeds: Starterpack.FeedItem list option
              [<JsonPropertyName("list")>]
              List: string
              [<JsonPropertyName("name")>]
              Name: string }

        type FeedItem =
            { [<JsonPropertyName("uri")>]
              Uri: string }

    module UnmuteActor =
        [<Literal>]
        let TypeId = "app.bsky.graph.unmuteActor"

        type Input =
            { [<JsonPropertyName("actor")>]
              Actor: string }

    module UnmuteActorList =
        [<Literal>]
        let TypeId = "app.bsky.graph.unmuteActorList"

        type Input =
            { [<JsonPropertyName("list")>]
              List: string }

    module UnmuteThread =
        [<Literal>]
        let TypeId = "app.bsky.graph.unmuteThread"

        type Input =
            { [<JsonPropertyName("root")>]
              Root: string }

    module Verification =
        [<Literal>]
        let TypeId = "app.bsky.graph.verification"

        /// Record declaring a verification relationship between two accounts. Verifications are only considered valid by an app if issued by an account the app considers trusted.
        type Verification =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("displayName")>]
              DisplayName: string
              [<JsonPropertyName("handle")>]
              Handle: string
              [<JsonPropertyName("subject")>]
              Subject: string }

module AppBskyFeed =
    module Defs =
        type BlockedAuthor =
            { [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("viewer")>]
              Viewer: AppBskyActor.Defs.ViewerState option }

        type BlockedPost =
            { [<JsonPropertyName("author")>]
              Author: Defs.BlockedAuthor
              [<JsonPropertyName("blocked")>]
              Blocked: bool
              [<JsonPropertyName("uri")>]
              Uri: string }

        [<Literal>]
        let ClickthroughAuthor = "app.bsky.feed.defs#clickthroughAuthor"

        [<Literal>]
        let ClickthroughEmbed = "app.bsky.feed.defs#clickthroughEmbed"

        [<Literal>]
        let ClickthroughItem = "app.bsky.feed.defs#clickthroughItem"

        [<Literal>]
        let ClickthroughReposter = "app.bsky.feed.defs#clickthroughReposter"

        [<Literal>]
        let ContentModeUnspecified = "app.bsky.feed.defs#contentModeUnspecified"

        [<Literal>]
        let ContentModeVideo = "app.bsky.feed.defs#contentModeVideo"

        type FeedViewPost =
            { [<JsonPropertyName("feedContext")>]
              FeedContext: string option
              [<JsonPropertyName("post")>]
              Post: Defs.PostView
              [<JsonPropertyName("reason")>]
              Reason: JsonElement option
              [<JsonPropertyName("reply")>]
              Reply: Defs.ReplyRef option
              [<JsonPropertyName("reqId")>]
              ReqId: string option }

        type GeneratorView =
            { [<JsonPropertyName("acceptsInteractions")>]
              AcceptsInteractions: bool option
              [<JsonPropertyName("avatar")>]
              Avatar: string option
              [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("contentMode")>]
              ContentMode: string option
              [<JsonPropertyName("creator")>]
              Creator: AppBskyActor.Defs.ProfileView
              [<JsonPropertyName("description")>]
              Description: string option
              [<JsonPropertyName("descriptionFacets")>]
              DescriptionFacets: AppBskyRichtext.Facet.Facet list option
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("displayName")>]
              DisplayName: string
              [<JsonPropertyName("indexedAt")>]
              IndexedAt: string
              [<JsonPropertyName("labels")>]
              Labels: ComAtprotoLabel.Defs.Label list option
              [<JsonPropertyName("likeCount")>]
              LikeCount: int64 option
              [<JsonPropertyName("uri")>]
              Uri: string
              [<JsonPropertyName("viewer")>]
              Viewer: Defs.GeneratorViewerState option }

        type GeneratorViewerState =
            { [<JsonPropertyName("like")>]
              Like: string option }

        type Interaction =
            { [<JsonPropertyName("event")>]
              Event: string option
              [<JsonPropertyName("feedContext")>]
              FeedContext: string option
              [<JsonPropertyName("item")>]
              Item: string option
              [<JsonPropertyName("reqId")>]
              ReqId: string option }

        [<Literal>]
        let InteractionLike = "app.bsky.feed.defs#interactionLike"

        [<Literal>]
        let InteractionQuote = "app.bsky.feed.defs#interactionQuote"

        [<Literal>]
        let InteractionReply = "app.bsky.feed.defs#interactionReply"

        [<Literal>]
        let InteractionRepost = "app.bsky.feed.defs#interactionRepost"

        [<Literal>]
        let InteractionSeen = "app.bsky.feed.defs#interactionSeen"

        [<Literal>]
        let InteractionShare = "app.bsky.feed.defs#interactionShare"

        type NotFoundPost =
            { [<JsonPropertyName("notFound")>]
              NotFound: bool
              [<JsonPropertyName("uri")>]
              Uri: string }

        type PostView =
            { [<JsonPropertyName("author")>]
              Author: AppBskyActor.Defs.ProfileViewBasic
              [<JsonPropertyName("bookmarkCount")>]
              BookmarkCount: int64 option
              [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("debug")>]
              Debug: JsonElement option
              [<JsonPropertyName("embed")>]
              Embed: JsonElement option
              [<JsonPropertyName("indexedAt")>]
              IndexedAt: string
              [<JsonPropertyName("labels")>]
              Labels: ComAtprotoLabel.Defs.Label list option
              [<JsonPropertyName("likeCount")>]
              LikeCount: int64 option
              [<JsonPropertyName("quoteCount")>]
              QuoteCount: int64 option
              [<JsonPropertyName("record")>]
              Record: JsonElement
              [<JsonPropertyName("replyCount")>]
              ReplyCount: int64 option
              [<JsonPropertyName("repostCount")>]
              RepostCount: int64 option
              [<JsonPropertyName("threadgate")>]
              Threadgate: Defs.ThreadgateView option
              [<JsonPropertyName("uri")>]
              Uri: string
              [<JsonPropertyName("viewer")>]
              Viewer: Defs.ViewerState option }

        type ReasonRepost =
            { [<JsonPropertyName("by")>]
              By: AppBskyActor.Defs.ProfileViewBasic
              [<JsonPropertyName("cid")>]
              Cid: string option
              [<JsonPropertyName("indexedAt")>]
              IndexedAt: string
              [<JsonPropertyName("uri")>]
              Uri: string option }

        type ReplyRef =
            { [<JsonPropertyName("grandparentAuthor")>]
              GrandparentAuthor: AppBskyActor.Defs.ProfileViewBasic option
              [<JsonPropertyName("parent")>]
              Parent: JsonElement
              [<JsonPropertyName("root")>]
              Root: JsonElement }

        [<Literal>]
        let RequestLess = "app.bsky.feed.defs#requestLess"

        [<Literal>]
        let RequestMore = "app.bsky.feed.defs#requestMore"

        type SkeletonFeedPost =
            { [<JsonPropertyName("feedContext")>]
              FeedContext: string option
              [<JsonPropertyName("post")>]
              Post: string
              [<JsonPropertyName("reason")>]
              Reason: JsonElement option }

        type SkeletonReasonRepost =
            { [<JsonPropertyName("repost")>]
              Repost: string }

        /// Metadata about this post within the context of the thread it is in.
        type ThreadContext =
            { [<JsonPropertyName("rootAuthorLike")>]
              RootAuthorLike: string option }

        type ThreadViewPost =
            { [<JsonPropertyName("parent")>]
              Parent: JsonElement option
              [<JsonPropertyName("post")>]
              Post: Defs.PostView
              [<JsonPropertyName("replies")>]
              Replies: JsonElement list option
              [<JsonPropertyName("threadContext")>]
              ThreadContext: Defs.ThreadContext option }

        type ThreadgateView =
            { [<JsonPropertyName("cid")>]
              Cid: string option
              [<JsonPropertyName("lists")>]
              Lists: AppBskyGraph.Defs.ListViewBasic list option
              [<JsonPropertyName("record")>]
              Record: JsonElement option
              [<JsonPropertyName("uri")>]
              Uri: string option }

        /// Metadata about the requesting account's relationship with the subject content. Only has meaningful content for authed requests.
        type ViewerState =
            { [<JsonPropertyName("bookmarked")>]
              Bookmarked: bool option
              [<JsonPropertyName("embeddingDisabled")>]
              EmbeddingDisabled: bool option
              [<JsonPropertyName("like")>]
              Like: string option
              [<JsonPropertyName("pinned")>]
              Pinned: bool option
              [<JsonPropertyName("replyDisabled")>]
              ReplyDisabled: bool option
              [<JsonPropertyName("repost")>]
              Repost: string option
              [<JsonPropertyName("threadMuted")>]
              ThreadMuted: bool option }

    module DescribeFeedGenerator =
        [<Literal>]
        let TypeId = "app.bsky.feed.describeFeedGenerator"

        type Output =
            { [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("feeds")>]
              Feeds: DescribeFeedGenerator.Feed list
              [<JsonPropertyName("links")>]
              Links: DescribeFeedGenerator.Links option }

        type Feed =
            { [<JsonPropertyName("uri")>]
              Uri: string }

        type Links =
            { [<JsonPropertyName("privacyPolicy")>]
              PrivacyPolicy: string option
              [<JsonPropertyName("termsOfService")>]
              TermsOfService: string option }

    module Generator =
        [<Literal>]
        let TypeId = "app.bsky.feed.generator"

        /// Record declaring of the existence of a feed generator, and containing metadata about it. The record can exist in any repository.
        type Generator =
            { [<JsonPropertyName("acceptsInteractions")>]
              AcceptsInteractions: bool option
              [<JsonPropertyName("avatar")>]
              Avatar: JsonElement option
              [<JsonPropertyName("contentMode")>]
              ContentMode: string option
              [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("description")>]
              Description: string option
              [<JsonPropertyName("descriptionFacets")>]
              DescriptionFacets: AppBskyRichtext.Facet.Facet list option
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("displayName")>]
              DisplayName: string
              [<JsonPropertyName("labels")>]
              Labels: JsonElement option }

    module GetActorFeeds =
        [<Literal>]
        let TypeId = "app.bsky.feed.getActorFeeds"

        type Params =
            { [<JsonPropertyName("actor")>]
              Actor: string
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("feeds")>]
              Feeds: Defs.GeneratorView list }

    module GetActorLikes =
        [<Literal>]
        let TypeId = "app.bsky.feed.getActorLikes"

        type Params =
            { [<JsonPropertyName("actor")>]
              Actor: string
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("feed")>]
              Feed: Defs.FeedViewPost list }

        module Errors =
            [<Literal>]
            let BlockedActor = "BlockedActor"

            [<Literal>]
            let BlockedByActor = "BlockedByActor"

    module GetAuthorFeed =
        [<Literal>]
        let TypeId = "app.bsky.feed.getAuthorFeed"

        type Params =
            { [<JsonPropertyName("actor")>]
              Actor: string
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("filter")>]
              Filter: string option
              [<JsonPropertyName("includePins")>]
              IncludePins: bool option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("feed")>]
              Feed: Defs.FeedViewPost list }

        module Errors =
            [<Literal>]
            let BlockedActor = "BlockedActor"

            [<Literal>]
            let BlockedByActor = "BlockedByActor"

    module GetFeed =
        [<Literal>]
        let TypeId = "app.bsky.feed.getFeed"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("feed")>]
              Feed: string
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("feed")>]
              Feed: Defs.FeedViewPost list }

        module Errors =
            [<Literal>]
            let UnknownFeed = "UnknownFeed"

    module GetFeedGenerator =
        [<Literal>]
        let TypeId = "app.bsky.feed.getFeedGenerator"

        type Params =
            { [<JsonPropertyName("feed")>]
              Feed: string }

        type Output =
            { [<JsonPropertyName("isOnline")>]
              IsOnline: bool
              [<JsonPropertyName("isValid")>]
              IsValid: bool
              [<JsonPropertyName("view")>]
              View: Defs.GeneratorView }

    module GetFeedGenerators =
        [<Literal>]
        let TypeId = "app.bsky.feed.getFeedGenerators"

        type Params =
            { [<JsonPropertyName("feeds")>]
              Feeds: string list }

        type Output =
            { [<JsonPropertyName("feeds")>]
              Feeds: Defs.GeneratorView list }

    module GetFeedSkeleton =
        [<Literal>]
        let TypeId = "app.bsky.feed.getFeedSkeleton"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("feed")>]
              Feed: string
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("feed")>]
              Feed: Defs.SkeletonFeedPost list
              [<JsonPropertyName("reqId")>]
              ReqId: string option }

        module Errors =
            [<Literal>]
            let UnknownFeed = "UnknownFeed"

    module GetLikes =
        [<Literal>]
        let TypeId = "app.bsky.feed.getLikes"

        type Params =
            { [<JsonPropertyName("cid")>]
              Cid: string option
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("uri")>]
              Uri: string }

        type Output =
            { [<JsonPropertyName("cid")>]
              Cid: string option
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("likes")>]
              Likes: GetLikes.Like list
              [<JsonPropertyName("uri")>]
              Uri: string }

        type Like =
            { [<JsonPropertyName("actor")>]
              Actor: AppBskyActor.Defs.ProfileView
              [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("indexedAt")>]
              IndexedAt: string }

    module GetListFeed =
        [<Literal>]
        let TypeId = "app.bsky.feed.getListFeed"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("list")>]
              List: string }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("feed")>]
              Feed: Defs.FeedViewPost list }

        module Errors =
            [<Literal>]
            let UnknownList = "UnknownList"

    module GetPostThread =
        [<Literal>]
        let TypeId = "app.bsky.feed.getPostThread"

        type Params =
            { [<JsonPropertyName("depth")>]
              Depth: int64 option
              [<JsonPropertyName("parentHeight")>]
              ParentHeight: int64 option
              [<JsonPropertyName("uri")>]
              Uri: string }

        type Output =
            { [<JsonPropertyName("thread")>]
              Thread: JsonElement
              [<JsonPropertyName("threadgate")>]
              Threadgate: Defs.ThreadgateView option }

        module Errors =
            [<Literal>]
            let NotFound = "NotFound"

    module GetPosts =
        [<Literal>]
        let TypeId = "app.bsky.feed.getPosts"

        type Params =
            { [<JsonPropertyName("uris")>]
              Uris: string list }

        type Output =
            { [<JsonPropertyName("posts")>]
              Posts: Defs.PostView list }

    module GetQuotes =
        [<Literal>]
        let TypeId = "app.bsky.feed.getQuotes"

        type Params =
            { [<JsonPropertyName("cid")>]
              Cid: string option
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("uri")>]
              Uri: string }

        type Output =
            { [<JsonPropertyName("cid")>]
              Cid: string option
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("posts")>]
              Posts: Defs.PostView list
              [<JsonPropertyName("uri")>]
              Uri: string }

    module GetRepostedBy =
        [<Literal>]
        let TypeId = "app.bsky.feed.getRepostedBy"

        type Params =
            { [<JsonPropertyName("cid")>]
              Cid: string option
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("uri")>]
              Uri: string }

        type Output =
            { [<JsonPropertyName("cid")>]
              Cid: string option
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("repostedBy")>]
              RepostedBy: AppBskyActor.Defs.ProfileView list
              [<JsonPropertyName("uri")>]
              Uri: string }

    module GetSuggestedFeeds =
        [<Literal>]
        let TypeId = "app.bsky.feed.getSuggestedFeeds"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("feeds")>]
              Feeds: Defs.GeneratorView list }

    module GetTimeline =
        [<Literal>]
        let TypeId = "app.bsky.feed.getTimeline"

        type Params =
            { [<JsonPropertyName("algorithm")>]
              Algorithm: string option
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("feed")>]
              Feed: Defs.FeedViewPost list }

    module Like =
        [<Literal>]
        let TypeId = "app.bsky.feed.like"

        /// Record declaring a 'like' of a piece of subject content.
        type Like =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("subject")>]
              Subject: ComAtprotoRepo.StrongRef.StrongRef
              [<JsonPropertyName("via")>]
              Via: ComAtprotoRepo.StrongRef.StrongRef option }

    module Post =
        [<Literal>]
        let TypeId = "app.bsky.feed.post"

        /// Record containing a Bluesky post.
        type Post =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("embed")>]
              Embed: JsonElement option
              [<JsonPropertyName("entities")>]
              Entities: Post.Entity list option
              [<JsonPropertyName("facets")>]
              Facets: AppBskyRichtext.Facet.Facet list option
              [<JsonPropertyName("labels")>]
              Labels: JsonElement option
              [<JsonPropertyName("langs")>]
              Langs: string list option
              [<JsonPropertyName("reply")>]
              Reply: Post.ReplyRef option
              [<JsonPropertyName("tags")>]
              Tags: string list option
              [<JsonPropertyName("text")>]
              Text: string }

        /// Deprecated: use facets instead.
        type Entity =
            { [<JsonPropertyName("index")>]
              Index: Post.TextSlice
              [<JsonPropertyName("type")>]
              Type: string
              [<JsonPropertyName("value")>]
              Value: string }

        type ReplyRef =
            { [<JsonPropertyName("parent")>]
              Parent: ComAtprotoRepo.StrongRef.StrongRef
              [<JsonPropertyName("root")>]
              Root: ComAtprotoRepo.StrongRef.StrongRef }

        /// Deprecated. Use app.bsky.richtext instead -- A text segment. Start is inclusive, end is exclusive. Indices are for utf16-encoded strings.
        type TextSlice =
            { [<JsonPropertyName("end")>]
              End: int64
              [<JsonPropertyName("start")>]
              Start: int64 }

    module Postgate =
        [<Literal>]
        let TypeId = "app.bsky.feed.postgate"

        /// Record defining interaction rules for a post. The record key (rkey) of the postgate record must match the record key of the post, and that record must be in the same repository.
        type Postgate =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("detachedEmbeddingUris")>]
              DetachedEmbeddingUris: string list option
              [<JsonPropertyName("embeddingRules")>]
              EmbeddingRules: JsonElement list option
              [<JsonPropertyName("post")>]
              Post: string }

    module Repost =
        [<Literal>]
        let TypeId = "app.bsky.feed.repost"

        /// Record representing a 'repost' of an existing Bluesky post.
        type Repost =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("subject")>]
              Subject: ComAtprotoRepo.StrongRef.StrongRef
              [<JsonPropertyName("via")>]
              Via: ComAtprotoRepo.StrongRef.StrongRef option }

    module SearchPosts =
        [<Literal>]
        let TypeId = "app.bsky.feed.searchPosts"

        type Params =
            { [<JsonPropertyName("author")>]
              Author: string option
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("domain")>]
              Domain: string option
              [<JsonPropertyName("lang")>]
              Lang: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("mentions")>]
              Mentions: string option
              [<JsonPropertyName("q")>]
              Q: string
              [<JsonPropertyName("since")>]
              Since: string option
              [<JsonPropertyName("sort")>]
              Sort: string option
              [<JsonPropertyName("tag")>]
              Tag: string list option
              [<JsonPropertyName("until")>]
              Until: string option
              [<JsonPropertyName("url")>]
              Url: string option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("hitsTotal")>]
              HitsTotal: int64 option
              [<JsonPropertyName("posts")>]
              Posts: Defs.PostView list }

        module Errors =
            [<Literal>]
            let BadQueryString = "BadQueryString"

    module SendInteractions =
        [<Literal>]
        let TypeId = "app.bsky.feed.sendInteractions"

        type Input =
            { [<JsonPropertyName("interactions")>]
              Interactions: Defs.Interaction list }

    module Threadgate =
        [<Literal>]
        let TypeId = "app.bsky.feed.threadgate"

        /// Record defining interaction gating rules for a thread (aka, reply controls). The record key (rkey) of the threadgate record must match the record key of the thread's root post, and that record must be in the same repository.
        type Threadgate =
            { [<JsonPropertyName("allow")>]
              Allow: JsonElement list option
              [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("hiddenReplies")>]
              HiddenReplies: string list option
              [<JsonPropertyName("post")>]
              Post: string }

        /// Allow replies from actors on a list.
        type ListRule =
            { [<JsonPropertyName("list")>]
              List: string }

module ComAtprotoServer =
    module ActivateAccount =
        [<Literal>]
        let TypeId = "com.atproto.server.activateAccount"

    module CheckAccountStatus =
        [<Literal>]
        let TypeId = "com.atproto.server.checkAccountStatus"

        type Output =
            { [<JsonPropertyName("activated")>]
              Activated: bool
              [<JsonPropertyName("expectedBlobs")>]
              ExpectedBlobs: int64
              [<JsonPropertyName("importedBlobs")>]
              ImportedBlobs: int64
              [<JsonPropertyName("indexedRecords")>]
              IndexedRecords: int64
              [<JsonPropertyName("privateStateValues")>]
              PrivateStateValues: int64
              [<JsonPropertyName("repoBlocks")>]
              RepoBlocks: int64
              [<JsonPropertyName("repoCommit")>]
              RepoCommit: string
              [<JsonPropertyName("repoRev")>]
              RepoRev: string
              [<JsonPropertyName("validDid")>]
              ValidDid: bool }

    module ConfirmEmail =
        [<Literal>]
        let TypeId = "com.atproto.server.confirmEmail"

        type Input =
            { [<JsonPropertyName("email")>]
              Email: string
              [<JsonPropertyName("token")>]
              Token: string }

        module Errors =
            [<Literal>]
            let AccountNotFound = "AccountNotFound"

            [<Literal>]
            let ExpiredToken = "ExpiredToken"

            [<Literal>]
            let InvalidToken = "InvalidToken"

            [<Literal>]
            let InvalidEmail = "InvalidEmail"

    module CreateAccount =
        [<Literal>]
        let TypeId = "com.atproto.server.createAccount"

        type Input =
            { [<JsonPropertyName("did")>]
              Did: string option
              [<JsonPropertyName("email")>]
              Email: string option
              [<JsonPropertyName("handle")>]
              Handle: string
              [<JsonPropertyName("inviteCode")>]
              InviteCode: string option
              [<JsonPropertyName("password")>]
              Password: string option
              [<JsonPropertyName("plcOp")>]
              PlcOp: JsonElement option
              [<JsonPropertyName("recoveryKey")>]
              RecoveryKey: string option
              [<JsonPropertyName("verificationCode")>]
              VerificationCode: string option
              [<JsonPropertyName("verificationPhone")>]
              VerificationPhone: string option }

        type Output =
            { [<JsonPropertyName("accessJwt")>]
              AccessJwt: string
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("didDoc")>]
              DidDoc: JsonElement option
              [<JsonPropertyName("handle")>]
              Handle: string
              [<JsonPropertyName("refreshJwt")>]
              RefreshJwt: string }

        module Errors =
            [<Literal>]
            let InvalidHandle = "InvalidHandle"

            [<Literal>]
            let InvalidPassword = "InvalidPassword"

            [<Literal>]
            let InvalidInviteCode = "InvalidInviteCode"

            [<Literal>]
            let HandleNotAvailable = "HandleNotAvailable"

            [<Literal>]
            let UnsupportedDomain = "UnsupportedDomain"

            [<Literal>]
            let UnresolvableDid = "UnresolvableDid"

            [<Literal>]
            let IncompatibleDidDoc = "IncompatibleDidDoc"

    module CreateAppPassword =
        [<Literal>]
        let TypeId = "com.atproto.server.createAppPassword"

        type Input =
            { [<JsonPropertyName("name")>]
              Name: string
              [<JsonPropertyName("privileged")>]
              Privileged: bool option }

        module Errors =
            [<Literal>]
            let AccountTakedown = "AccountTakedown"

        type AppPassword =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("name")>]
              Name: string
              [<JsonPropertyName("password")>]
              Password: string
              [<JsonPropertyName("privileged")>]
              Privileged: bool option }

    module CreateInviteCode =
        [<Literal>]
        let TypeId = "com.atproto.server.createInviteCode"

        type Input =
            { [<JsonPropertyName("forAccount")>]
              ForAccount: string option
              [<JsonPropertyName("useCount")>]
              UseCount: int64 }

        type Output =
            { [<JsonPropertyName("code")>]
              Code: string }

    module CreateInviteCodes =
        [<Literal>]
        let TypeId = "com.atproto.server.createInviteCodes"

        type Input =
            { [<JsonPropertyName("codeCount")>]
              CodeCount: int64
              [<JsonPropertyName("forAccounts")>]
              ForAccounts: string list option
              [<JsonPropertyName("useCount")>]
              UseCount: int64 }

        type Output =
            { [<JsonPropertyName("codes")>]
              Codes: CreateInviteCodes.AccountCodes list }

        type AccountCodes =
            { [<JsonPropertyName("account")>]
              Account: string
              [<JsonPropertyName("codes")>]
              Codes: string list }

    module CreateSession =
        [<Literal>]
        let TypeId = "com.atproto.server.createSession"

        type Input =
            { [<JsonPropertyName("allowTakendown")>]
              AllowTakendown: bool option
              [<JsonPropertyName("authFactorToken")>]
              AuthFactorToken: string option
              [<JsonPropertyName("identifier")>]
              Identifier: string
              [<JsonPropertyName("password")>]
              Password: string }

        type Output =
            { [<JsonPropertyName("accessJwt")>]
              AccessJwt: string
              [<JsonPropertyName("active")>]
              Active: bool option
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("didDoc")>]
              DidDoc: JsonElement option
              [<JsonPropertyName("email")>]
              Email: string option
              [<JsonPropertyName("emailAuthFactor")>]
              EmailAuthFactor: bool option
              [<JsonPropertyName("emailConfirmed")>]
              EmailConfirmed: bool option
              [<JsonPropertyName("handle")>]
              Handle: string
              [<JsonPropertyName("refreshJwt")>]
              RefreshJwt: string
              [<JsonPropertyName("status")>]
              Status: string option }

        module Errors =
            [<Literal>]
            let AccountTakedown = "AccountTakedown"

            [<Literal>]
            let AuthFactorTokenRequired = "AuthFactorTokenRequired"

    module DeactivateAccount =
        [<Literal>]
        let TypeId = "com.atproto.server.deactivateAccount"

        type Input =
            { [<JsonPropertyName("deleteAfter")>]
              DeleteAfter: string option }

    module Defs =
        type InviteCode =
            { [<JsonPropertyName("available")>]
              Available: int64
              [<JsonPropertyName("code")>]
              Code: string
              [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("createdBy")>]
              CreatedBy: string
              [<JsonPropertyName("disabled")>]
              Disabled: bool
              [<JsonPropertyName("forAccount")>]
              ForAccount: string
              [<JsonPropertyName("uses")>]
              Uses: Defs.InviteCodeUse list }

        type InviteCodeUse =
            { [<JsonPropertyName("usedAt")>]
              UsedAt: string
              [<JsonPropertyName("usedBy")>]
              UsedBy: string }

    module DeleteAccount =
        [<Literal>]
        let TypeId = "com.atproto.server.deleteAccount"

        type Input =
            { [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("password")>]
              Password: string
              [<JsonPropertyName("token")>]
              Token: string }

        module Errors =
            [<Literal>]
            let ExpiredToken = "ExpiredToken"

            [<Literal>]
            let InvalidToken = "InvalidToken"

    module DeleteSession =
        [<Literal>]
        let TypeId = "com.atproto.server.deleteSession"

        module Errors =
            [<Literal>]
            let InvalidToken = "InvalidToken"

            [<Literal>]
            let ExpiredToken = "ExpiredToken"

    module DescribeServer =
        [<Literal>]
        let TypeId = "com.atproto.server.describeServer"

        type Output =
            { [<JsonPropertyName("availableUserDomains")>]
              AvailableUserDomains: string list
              [<JsonPropertyName("contact")>]
              Contact: DescribeServer.Contact option
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("inviteCodeRequired")>]
              InviteCodeRequired: bool option
              [<JsonPropertyName("links")>]
              Links: DescribeServer.Links option
              [<JsonPropertyName("phoneVerificationRequired")>]
              PhoneVerificationRequired: bool option }

        type Contact =
            { [<JsonPropertyName("email")>]
              Email: string option }

        type Links =
            { [<JsonPropertyName("privacyPolicy")>]
              PrivacyPolicy: string option
              [<JsonPropertyName("termsOfService")>]
              TermsOfService: string option }

    module GetAccountInviteCodes =
        [<Literal>]
        let TypeId = "com.atproto.server.getAccountInviteCodes"

        type Params =
            { [<JsonPropertyName("createAvailable")>]
              CreateAvailable: bool option
              [<JsonPropertyName("includeUsed")>]
              IncludeUsed: bool option }

        type Output =
            { [<JsonPropertyName("codes")>]
              Codes: Defs.InviteCode list }

        module Errors =
            [<Literal>]
            let DuplicateCreate = "DuplicateCreate"

    module GetServiceAuth =
        [<Literal>]
        let TypeId = "com.atproto.server.getServiceAuth"

        type Params =
            { [<JsonPropertyName("aud")>]
              Aud: string
              [<JsonPropertyName("exp")>]
              Exp: int64 option
              [<JsonPropertyName("lxm")>]
              Lxm: string option }

        type Output =
            { [<JsonPropertyName("token")>]
              Token: string }

        module Errors =
            [<Literal>]
            let BadExpiration = "BadExpiration"

    module GetSession =
        [<Literal>]
        let TypeId = "com.atproto.server.getSession"

        type Output =
            { [<JsonPropertyName("active")>]
              Active: bool option
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("didDoc")>]
              DidDoc: JsonElement option
              [<JsonPropertyName("email")>]
              Email: string option
              [<JsonPropertyName("emailAuthFactor")>]
              EmailAuthFactor: bool option
              [<JsonPropertyName("emailConfirmed")>]
              EmailConfirmed: bool option
              [<JsonPropertyName("handle")>]
              Handle: string
              [<JsonPropertyName("status")>]
              Status: string option }

    module ListAppPasswords =
        [<Literal>]
        let TypeId = "com.atproto.server.listAppPasswords"

        type Output =
            { [<JsonPropertyName("passwords")>]
              Passwords: ListAppPasswords.AppPassword list }

        module Errors =
            [<Literal>]
            let AccountTakedown = "AccountTakedown"

        type AppPassword =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("name")>]
              Name: string
              [<JsonPropertyName("privileged")>]
              Privileged: bool option }

    module RefreshSession =
        [<Literal>]
        let TypeId = "com.atproto.server.refreshSession"

        type Output =
            { [<JsonPropertyName("accessJwt")>]
              AccessJwt: string
              [<JsonPropertyName("active")>]
              Active: bool option
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("didDoc")>]
              DidDoc: JsonElement option
              [<JsonPropertyName("email")>]
              Email: string option
              [<JsonPropertyName("emailAuthFactor")>]
              EmailAuthFactor: bool option
              [<JsonPropertyName("emailConfirmed")>]
              EmailConfirmed: bool option
              [<JsonPropertyName("handle")>]
              Handle: string
              [<JsonPropertyName("refreshJwt")>]
              RefreshJwt: string
              [<JsonPropertyName("status")>]
              Status: string option }

        module Errors =
            [<Literal>]
            let AccountTakedown = "AccountTakedown"

            [<Literal>]
            let InvalidToken = "InvalidToken"

            [<Literal>]
            let ExpiredToken = "ExpiredToken"

    module RequestAccountDelete =
        [<Literal>]
        let TypeId = "com.atproto.server.requestAccountDelete"

    module RequestEmailConfirmation =
        [<Literal>]
        let TypeId = "com.atproto.server.requestEmailConfirmation"

    module RequestEmailUpdate =
        [<Literal>]
        let TypeId = "com.atproto.server.requestEmailUpdate"

        type Output =
            { [<JsonPropertyName("tokenRequired")>]
              TokenRequired: bool }

    module RequestPasswordReset =
        [<Literal>]
        let TypeId = "com.atproto.server.requestPasswordReset"

        type Input =
            { [<JsonPropertyName("email")>]
              Email: string }

    module ReserveSigningKey =
        [<Literal>]
        let TypeId = "com.atproto.server.reserveSigningKey"

        type Input =
            { [<JsonPropertyName("did")>]
              Did: string option }

        type Output =
            { [<JsonPropertyName("signingKey")>]
              SigningKey: string }

    module ResetPassword =
        [<Literal>]
        let TypeId = "com.atproto.server.resetPassword"

        type Input =
            { [<JsonPropertyName("password")>]
              Password: string
              [<JsonPropertyName("token")>]
              Token: string }

        module Errors =
            [<Literal>]
            let ExpiredToken = "ExpiredToken"

            [<Literal>]
            let InvalidToken = "InvalidToken"

    module RevokeAppPassword =
        [<Literal>]
        let TypeId = "com.atproto.server.revokeAppPassword"

        type Input =
            { [<JsonPropertyName("name")>]
              Name: string }

    module UpdateEmail =
        [<Literal>]
        let TypeId = "com.atproto.server.updateEmail"

        type Input =
            { [<JsonPropertyName("email")>]
              Email: string
              [<JsonPropertyName("emailAuthFactor")>]
              EmailAuthFactor: bool option
              [<JsonPropertyName("token")>]
              Token: string option }

        module Errors =
            [<Literal>]
            let ExpiredToken = "ExpiredToken"

            [<Literal>]
            let InvalidToken = "InvalidToken"

            [<Literal>]
            let TokenRequired = "TokenRequired"

module ComAtprotoAdmin =
    module Defs =
        type AccountView =
            { [<JsonPropertyName("deactivatedAt")>]
              DeactivatedAt: string option
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("email")>]
              Email: string option
              [<JsonPropertyName("emailConfirmedAt")>]
              EmailConfirmedAt: string option
              [<JsonPropertyName("handle")>]
              Handle: string
              [<JsonPropertyName("indexedAt")>]
              IndexedAt: string
              [<JsonPropertyName("inviteNote")>]
              InviteNote: string option
              [<JsonPropertyName("invitedBy")>]
              InvitedBy: ComAtprotoServer.Defs.InviteCode option
              [<JsonPropertyName("invites")>]
              Invites: ComAtprotoServer.Defs.InviteCode list option
              [<JsonPropertyName("invitesDisabled")>]
              InvitesDisabled: bool option
              [<JsonPropertyName("relatedRecords")>]
              RelatedRecords: JsonElement list option
              [<JsonPropertyName("threatSignatures")>]
              ThreatSignatures: Defs.ThreatSignature list option }

        type RepoBlobRef =
            { [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("recordUri")>]
              RecordUri: string option }

        type RepoRef =
            { [<JsonPropertyName("did")>]
              Did: string }

        type StatusAttr =
            { [<JsonPropertyName("applied")>]
              Applied: bool
              [<JsonPropertyName("ref")>]
              Ref: string option }

        type ThreatSignature =
            { [<JsonPropertyName("property")>]
              Property: string
              [<JsonPropertyName("value")>]
              Value: string }

    module DeleteAccount =
        [<Literal>]
        let TypeId = "com.atproto.admin.deleteAccount"

        type Input =
            { [<JsonPropertyName("did")>]
              Did: string }

    module DisableAccountInvites =
        [<Literal>]
        let TypeId = "com.atproto.admin.disableAccountInvites"

        type Input =
            { [<JsonPropertyName("account")>]
              Account: string
              [<JsonPropertyName("note")>]
              Note: string option }

    module DisableInviteCodes =
        [<Literal>]
        let TypeId = "com.atproto.admin.disableInviteCodes"

        type Input =
            { [<JsonPropertyName("accounts")>]
              Accounts: string list option
              [<JsonPropertyName("codes")>]
              Codes: string list option }

    module EnableAccountInvites =
        [<Literal>]
        let TypeId = "com.atproto.admin.enableAccountInvites"

        type Input =
            { [<JsonPropertyName("account")>]
              Account: string
              [<JsonPropertyName("note")>]
              Note: string option }

    module GetAccountInfo =
        [<Literal>]
        let TypeId = "com.atproto.admin.getAccountInfo"

        type Params =
            { [<JsonPropertyName("did")>]
              Did: string }

    module GetAccountInfos =
        [<Literal>]
        let TypeId = "com.atproto.admin.getAccountInfos"

        type Params =
            { [<JsonPropertyName("dids")>]
              Dids: string list }

        type Output =
            { [<JsonPropertyName("infos")>]
              Infos: Defs.AccountView list }

    module GetInviteCodes =
        [<Literal>]
        let TypeId = "com.atproto.admin.getInviteCodes"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("sort")>]
              Sort: string option }

        type Output =
            { [<JsonPropertyName("codes")>]
              Codes: ComAtprotoServer.Defs.InviteCode list
              [<JsonPropertyName("cursor")>]
              Cursor: string option }

    module GetSubjectStatus =
        [<Literal>]
        let TypeId = "com.atproto.admin.getSubjectStatus"

        type Params =
            { [<JsonPropertyName("blob")>]
              Blob: string option
              [<JsonPropertyName("did")>]
              Did: string option
              [<JsonPropertyName("uri")>]
              Uri: string option }

        type Output =
            { [<JsonPropertyName("deactivated")>]
              Deactivated: Defs.StatusAttr option
              [<JsonPropertyName("subject")>]
              Subject: JsonElement
              [<JsonPropertyName("takedown")>]
              Takedown: Defs.StatusAttr option }

    module SearchAccounts =
        [<Literal>]
        let TypeId = "com.atproto.admin.searchAccounts"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("email")>]
              Email: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("accounts")>]
              Accounts: Defs.AccountView list
              [<JsonPropertyName("cursor")>]
              Cursor: string option }

    module SendEmail =
        [<Literal>]
        let TypeId = "com.atproto.admin.sendEmail"

        type Input =
            { [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("content")>]
              Content: string
              [<JsonPropertyName("recipientDid")>]
              RecipientDid: string
              [<JsonPropertyName("senderDid")>]
              SenderDid: string
              [<JsonPropertyName("subject")>]
              Subject: string option }

        type Output =
            { [<JsonPropertyName("sent")>]
              Sent: bool }

    module UpdateAccountEmail =
        [<Literal>]
        let TypeId = "com.atproto.admin.updateAccountEmail"

        type Input =
            { [<JsonPropertyName("account")>]
              Account: string
              [<JsonPropertyName("email")>]
              Email: string }

    module UpdateAccountHandle =
        [<Literal>]
        let TypeId = "com.atproto.admin.updateAccountHandle"

        type Input =
            { [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("handle")>]
              Handle: string }

    module UpdateAccountPassword =
        [<Literal>]
        let TypeId = "com.atproto.admin.updateAccountPassword"

        type Input =
            { [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("password")>]
              Password: string }

    module UpdateAccountSigningKey =
        [<Literal>]
        let TypeId = "com.atproto.admin.updateAccountSigningKey"

        type Input =
            { [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("signingKey")>]
              SigningKey: string }

    module UpdateSubjectStatus =
        [<Literal>]
        let TypeId = "com.atproto.admin.updateSubjectStatus"

        type Input =
            { [<JsonPropertyName("deactivated")>]
              Deactivated: Defs.StatusAttr option
              [<JsonPropertyName("subject")>]
              Subject: JsonElement
              [<JsonPropertyName("takedown")>]
              Takedown: Defs.StatusAttr option }

        type Output =
            { [<JsonPropertyName("subject")>]
              Subject: JsonElement
              [<JsonPropertyName("takedown")>]
              Takedown: Defs.StatusAttr option }

module ComAtprotoModeration =
    module CreateReport =
        [<Literal>]
        let TypeId = "com.atproto.moderation.createReport"

        type Input =
            { [<JsonPropertyName("modTool")>]
              ModTool: CreateReport.ModTool option
              [<JsonPropertyName("reason")>]
              Reason: string option
              [<JsonPropertyName("reasonType")>]
              ReasonType: Defs.ReasonType
              [<JsonPropertyName("subject")>]
              Subject: JsonElement }

        type Output =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("id")>]
              Id: int64
              [<JsonPropertyName("reason")>]
              Reason: string option
              [<JsonPropertyName("reasonType")>]
              ReasonType: Defs.ReasonType
              [<JsonPropertyName("reportedBy")>]
              ReportedBy: string
              [<JsonPropertyName("subject")>]
              Subject: JsonElement }

        /// Moderation tool information for tracing the source of the action
        type ModTool =
            { [<JsonPropertyName("meta")>]
              Meta: JsonElement option
              [<JsonPropertyName("name")>]
              Name: string }

    module Defs =
        [<Literal>]
        let ReasonAppeal = "com.atproto.moderation.defs#reasonAppeal"

        [<Literal>]
        let ReasonMisleading = "com.atproto.moderation.defs#reasonMisleading"

        [<Literal>]
        let ReasonOther = "com.atproto.moderation.defs#reasonOther"

        [<Literal>]
        let ReasonRude = "com.atproto.moderation.defs#reasonRude"

        [<Literal>]
        let ReasonSexual = "com.atproto.moderation.defs#reasonSexual"

        [<Literal>]
        let ReasonSpam = "com.atproto.moderation.defs#reasonSpam"

        type ReasonType = string

        [<Literal>]
        let ReasonViolation = "com.atproto.moderation.defs#reasonViolation"

        type SubjectType = string

module AppBskyLabeler =
    module Defs =
        type LabelerPolicies =
            { [<JsonPropertyName("labelValueDefinitions")>]
              LabelValueDefinitions: ComAtprotoLabel.Defs.LabelValueDefinition list option
              [<JsonPropertyName("labelValues")>]
              LabelValues: ComAtprotoLabel.Defs.LabelValue list }

        type LabelerView =
            { [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("creator")>]
              Creator: AppBskyActor.Defs.ProfileView
              [<JsonPropertyName("indexedAt")>]
              IndexedAt: string
              [<JsonPropertyName("labels")>]
              Labels: ComAtprotoLabel.Defs.Label list option
              [<JsonPropertyName("likeCount")>]
              LikeCount: int64 option
              [<JsonPropertyName("uri")>]
              Uri: string
              [<JsonPropertyName("viewer")>]
              Viewer: Defs.LabelerViewerState option }

        type LabelerViewDetailed =
            { [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("creator")>]
              Creator: AppBskyActor.Defs.ProfileView
              [<JsonPropertyName("indexedAt")>]
              IndexedAt: string
              [<JsonPropertyName("labels")>]
              Labels: ComAtprotoLabel.Defs.Label list option
              [<JsonPropertyName("likeCount")>]
              LikeCount: int64 option
              [<JsonPropertyName("policies")>]
              Policies: Defs.LabelerPolicies
              [<JsonPropertyName("reasonTypes")>]
              ReasonTypes: ComAtprotoModeration.Defs.ReasonType list option
              [<JsonPropertyName("subjectCollections")>]
              SubjectCollections: string list option
              [<JsonPropertyName("subjectTypes")>]
              SubjectTypes: ComAtprotoModeration.Defs.SubjectType list option
              [<JsonPropertyName("uri")>]
              Uri: string
              [<JsonPropertyName("viewer")>]
              Viewer: Defs.LabelerViewerState option }

        type LabelerViewerState =
            { [<JsonPropertyName("like")>]
              Like: string option }

    module GetServices =
        [<Literal>]
        let TypeId = "app.bsky.labeler.getServices"

        type Params =
            { [<JsonPropertyName("detailed")>]
              Detailed: bool option
              [<JsonPropertyName("dids")>]
              Dids: string list }

        type Output =
            { [<JsonPropertyName("views")>]
              Views: JsonElement list }

    module Service =
        [<Literal>]
        let TypeId = "app.bsky.labeler.service"

        /// A declaration of the existence of labeler service.
        type Service =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("labels")>]
              Labels: JsonElement option
              [<JsonPropertyName("policies")>]
              Policies: Defs.LabelerPolicies
              [<JsonPropertyName("reasonTypes")>]
              ReasonTypes: ComAtprotoModeration.Defs.ReasonType list option
              [<JsonPropertyName("subjectCollections")>]
              SubjectCollections: string list option
              [<JsonPropertyName("subjectTypes")>]
              SubjectTypes: ComAtprotoModeration.Defs.SubjectType list option }

module AppBskyEmbed =
    module Defs =
        /// width:height represents an aspect ratio. It may be approximate, and may not correspond to absolute dimensions in any given unit.
        type AspectRatio =
            { [<JsonPropertyName("height")>]
              Height: int64
              [<JsonPropertyName("width")>]
              Width: int64 }

    module External =
        /// A representation of some externally linked content (eg, a URL and 'card'), embedded in a Bluesky record (eg, a post).
        type External =
            { [<JsonPropertyName("external")>]
              External: External.ExternalDef }

        type ExternalDef =
            { [<JsonPropertyName("description")>]
              Description: string
              [<JsonPropertyName("thumb")>]
              Thumb: JsonElement option
              [<JsonPropertyName("title")>]
              Title: string
              [<JsonPropertyName("uri")>]
              Uri: string }

        type View =
            { [<JsonPropertyName("external")>]
              External: External.ViewExternal }

        type ViewExternal =
            { [<JsonPropertyName("description")>]
              Description: string
              [<JsonPropertyName("thumb")>]
              Thumb: string option
              [<JsonPropertyName("title")>]
              Title: string
              [<JsonPropertyName("uri")>]
              Uri: string }

    module Images =
        type Images =
            { [<JsonPropertyName("images")>]
              Images: Images.Image list }

        type Image =
            { [<JsonPropertyName("alt")>]
              Alt: string
              [<JsonPropertyName("aspectRatio")>]
              AspectRatio: Defs.AspectRatio option
              [<JsonPropertyName("image")>]
              Image: JsonElement }

        type View =
            { [<JsonPropertyName("images")>]
              Images: Images.ViewImage list }

        type ViewImage =
            { [<JsonPropertyName("alt")>]
              Alt: string
              [<JsonPropertyName("aspectRatio")>]
              AspectRatio: Defs.AspectRatio option
              [<JsonPropertyName("fullsize")>]
              Fullsize: string
              [<JsonPropertyName("thumb")>]
              Thumb: string }

    module Record =
        type Record =
            { [<JsonPropertyName("record")>]
              Record: ComAtprotoRepo.StrongRef.StrongRef }

        type View =
            { [<JsonPropertyName("record")>]
              Record: JsonElement }

        type ViewBlocked =
            { [<JsonPropertyName("author")>]
              Author: AppBskyFeed.Defs.BlockedAuthor
              [<JsonPropertyName("blocked")>]
              Blocked: bool
              [<JsonPropertyName("uri")>]
              Uri: string }

        type ViewDetached =
            { [<JsonPropertyName("detached")>]
              Detached: bool
              [<JsonPropertyName("uri")>]
              Uri: string }

        type ViewNotFound =
            { [<JsonPropertyName("notFound")>]
              NotFound: bool
              [<JsonPropertyName("uri")>]
              Uri: string }

        type ViewRecord =
            { [<JsonPropertyName("author")>]
              Author: AppBskyActor.Defs.ProfileViewBasic
              [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("embeds")>]
              Embeds: JsonElement list option
              [<JsonPropertyName("indexedAt")>]
              IndexedAt: string
              [<JsonPropertyName("labels")>]
              Labels: ComAtprotoLabel.Defs.Label list option
              [<JsonPropertyName("likeCount")>]
              LikeCount: int64 option
              [<JsonPropertyName("quoteCount")>]
              QuoteCount: int64 option
              [<JsonPropertyName("replyCount")>]
              ReplyCount: int64 option
              [<JsonPropertyName("repostCount")>]
              RepostCount: int64 option
              [<JsonPropertyName("uri")>]
              Uri: string
              [<JsonPropertyName("value")>]
              Value: JsonElement }

    module RecordWithMedia =
        type RecordWithMedia =
            { [<JsonPropertyName("media")>]
              Media: JsonElement
              [<JsonPropertyName("record")>]
              Record: Record.Record }

        type View =
            { [<JsonPropertyName("media")>]
              Media: JsonElement
              [<JsonPropertyName("record")>]
              Record: Record.View }

    module Video =
        type Video =
            { [<JsonPropertyName("alt")>]
              Alt: string option
              [<JsonPropertyName("aspectRatio")>]
              AspectRatio: Defs.AspectRatio option
              [<JsonPropertyName("captions")>]
              Captions: Video.Caption list option
              [<JsonPropertyName("presentation")>]
              Presentation: string option
              [<JsonPropertyName("video")>]
              Video: JsonElement }

        type Caption =
            { [<JsonPropertyName("file")>]
              File: JsonElement
              [<JsonPropertyName("lang")>]
              Lang: string }

        type View =
            { [<JsonPropertyName("alt")>]
              Alt: string option
              [<JsonPropertyName("aspectRatio")>]
              AspectRatio: Defs.AspectRatio option
              [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("playlist")>]
              Playlist: string
              [<JsonPropertyName("presentation")>]
              Presentation: string option
              [<JsonPropertyName("thumbnail")>]
              Thumbnail: string option }

module AppBskyNotification =
    module Declaration =
        [<Literal>]
        let TypeId = "app.bsky.notification.declaration"

        /// A declaration of the user's choices related to notifications that can be produced by them.
        type Declaration =
            { [<JsonPropertyName("allowSubscriptions")>]
              AllowSubscriptions: string }

    module Defs =
        type ActivitySubscription =
            { [<JsonPropertyName("post")>]
              Post: bool
              [<JsonPropertyName("reply")>]
              Reply: bool }

        type ChatPreference =
            { [<JsonPropertyName("include")>]
              Include: string
              [<JsonPropertyName("push")>]
              Push: bool }

        type FilterablePreference =
            { [<JsonPropertyName("include")>]
              Include: string
              [<JsonPropertyName("list")>]
              List: bool
              [<JsonPropertyName("push")>]
              Push: bool }

        type Preference =
            { [<JsonPropertyName("list")>]
              List: bool
              [<JsonPropertyName("push")>]
              Push: bool }

        type Preferences =
            { [<JsonPropertyName("chat")>]
              Chat: Defs.ChatPreference
              [<JsonPropertyName("follow")>]
              Follow: Defs.FilterablePreference
              [<JsonPropertyName("like")>]
              Like: Defs.FilterablePreference
              [<JsonPropertyName("likeViaRepost")>]
              LikeViaRepost: Defs.FilterablePreference
              [<JsonPropertyName("mention")>]
              Mention: Defs.FilterablePreference
              [<JsonPropertyName("quote")>]
              Quote: Defs.FilterablePreference
              [<JsonPropertyName("reply")>]
              Reply: Defs.FilterablePreference
              [<JsonPropertyName("repost")>]
              Repost: Defs.FilterablePreference
              [<JsonPropertyName("repostViaRepost")>]
              RepostViaRepost: Defs.FilterablePreference
              [<JsonPropertyName("starterpackJoined")>]
              StarterpackJoined: Defs.Preference
              [<JsonPropertyName("subscribedPost")>]
              SubscribedPost: Defs.Preference
              [<JsonPropertyName("unverified")>]
              Unverified: Defs.Preference
              [<JsonPropertyName("verified")>]
              Verified: Defs.Preference }

        /// Object used to store activity subscription data in stash.
        type SubjectActivitySubscription =
            { [<JsonPropertyName("activitySubscription")>]
              ActivitySubscription: Defs.ActivitySubscription
              [<JsonPropertyName("subject")>]
              Subject: string }

    module GetPreferences =
        [<Literal>]
        let TypeId = "app.bsky.notification.getPreferences"

        type Output =
            { [<JsonPropertyName("preferences")>]
              Preferences: Defs.Preferences }

    module GetUnreadCount =
        [<Literal>]
        let TypeId = "app.bsky.notification.getUnreadCount"

        type Params =
            { [<JsonPropertyName("priority")>]
              Priority: bool option
              [<JsonPropertyName("seenAt")>]
              SeenAt: string option }

        type Output =
            { [<JsonPropertyName("count")>]
              Count: int64 }

    module ListActivitySubscriptions =
        [<Literal>]
        let TypeId = "app.bsky.notification.listActivitySubscriptions"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("subscriptions")>]
              Subscriptions: AppBskyActor.Defs.ProfileView list }

    module ListNotifications =
        [<Literal>]
        let TypeId = "app.bsky.notification.listNotifications"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("priority")>]
              Priority: bool option
              [<JsonPropertyName("reasons")>]
              Reasons: string list option
              [<JsonPropertyName("seenAt")>]
              SeenAt: string option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("notifications")>]
              Notifications: ListNotifications.Notification list
              [<JsonPropertyName("priority")>]
              Priority: bool option
              [<JsonPropertyName("seenAt")>]
              SeenAt: string option }

        type Notification =
            { [<JsonPropertyName("author")>]
              Author: AppBskyActor.Defs.ProfileView
              [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("indexedAt")>]
              IndexedAt: string
              [<JsonPropertyName("isRead")>]
              IsRead: bool
              [<JsonPropertyName("labels")>]
              Labels: ComAtprotoLabel.Defs.Label list option
              [<JsonPropertyName("reason")>]
              Reason: string
              [<JsonPropertyName("reasonSubject")>]
              ReasonSubject: string option
              [<JsonPropertyName("record")>]
              Record: JsonElement
              [<JsonPropertyName("uri")>]
              Uri: string }

    module PutActivitySubscription =
        [<Literal>]
        let TypeId = "app.bsky.notification.putActivitySubscription"

        type Input =
            { [<JsonPropertyName("activitySubscription")>]
              ActivitySubscription: Defs.ActivitySubscription
              [<JsonPropertyName("subject")>]
              Subject: string }

        type Output =
            { [<JsonPropertyName("activitySubscription")>]
              ActivitySubscription: Defs.ActivitySubscription option
              [<JsonPropertyName("subject")>]
              Subject: string }

    module PutPreferences =
        [<Literal>]
        let TypeId = "app.bsky.notification.putPreferences"

        type Input =
            { [<JsonPropertyName("priority")>]
              Priority: bool }

    module PutPreferencesV2 =
        [<Literal>]
        let TypeId = "app.bsky.notification.putPreferencesV2"

        type Input =
            { [<JsonPropertyName("chat")>]
              Chat: Defs.ChatPreference option
              [<JsonPropertyName("follow")>]
              Follow: Defs.FilterablePreference option
              [<JsonPropertyName("like")>]
              Like: Defs.FilterablePreference option
              [<JsonPropertyName("likeViaRepost")>]
              LikeViaRepost: Defs.FilterablePreference option
              [<JsonPropertyName("mention")>]
              Mention: Defs.FilterablePreference option
              [<JsonPropertyName("quote")>]
              Quote: Defs.FilterablePreference option
              [<JsonPropertyName("reply")>]
              Reply: Defs.FilterablePreference option
              [<JsonPropertyName("repost")>]
              Repost: Defs.FilterablePreference option
              [<JsonPropertyName("repostViaRepost")>]
              RepostViaRepost: Defs.FilterablePreference option
              [<JsonPropertyName("starterpackJoined")>]
              StarterpackJoined: Defs.Preference option
              [<JsonPropertyName("subscribedPost")>]
              SubscribedPost: Defs.Preference option
              [<JsonPropertyName("unverified")>]
              Unverified: Defs.Preference option
              [<JsonPropertyName("verified")>]
              Verified: Defs.Preference option }

        type Output =
            { [<JsonPropertyName("preferences")>]
              Preferences: Defs.Preferences }

    module RegisterPush =
        [<Literal>]
        let TypeId = "app.bsky.notification.registerPush"

        type Input =
            { [<JsonPropertyName("ageRestricted")>]
              AgeRestricted: bool option
              [<JsonPropertyName("appId")>]
              AppId: string
              [<JsonPropertyName("platform")>]
              Platform: string
              [<JsonPropertyName("serviceDid")>]
              ServiceDid: string
              [<JsonPropertyName("token")>]
              Token: string }

    module UnregisterPush =
        [<Literal>]
        let TypeId = "app.bsky.notification.unregisterPush"

        type Input =
            { [<JsonPropertyName("appId")>]
              AppId: string
              [<JsonPropertyName("platform")>]
              Platform: string
              [<JsonPropertyName("serviceDid")>]
              ServiceDid: string
              [<JsonPropertyName("token")>]
              Token: string }

    module UpdateSeen =
        [<Literal>]
        let TypeId = "app.bsky.notification.updateSeen"

        type Input =
            { [<JsonPropertyName("seenAt")>]
              SeenAt: string }

module AppBskyActor =
    module Defs =
        type AdultContentPref =
            { [<JsonPropertyName("enabled")>]
              Enabled: bool }

        /// If set, an active progress guide. Once completed, can be set to undefined. Should have unspecced fields tracking progress.
        type BskyAppProgressGuide =
            { [<JsonPropertyName("guide")>]
              Guide: string }

        /// A grab bag of state that's specific to the bsky.app program. Third-party apps shouldn't use this.
        type BskyAppStatePref =
            { [<JsonPropertyName("activeProgressGuide")>]
              ActiveProgressGuide: Defs.BskyAppProgressGuide option
              [<JsonPropertyName("nuxs")>]
              Nuxs: Defs.Nux list option
              [<JsonPropertyName("queuedNudges")>]
              QueuedNudges: string list option }

        type ContentLabelPref =
            { [<JsonPropertyName("label")>]
              Label: string
              [<JsonPropertyName("labelerDid")>]
              LabelerDid: string option
              [<JsonPropertyName("visibility")>]
              Visibility: string }

        /// Read-only preference containing value(s) inferred from the user's declared birthdate. Absence of this preference object in the response indicates that the user has not made a declaration.
        type DeclaredAgePref =
            { [<JsonPropertyName("isOverAge13")>]
              IsOverAge13: bool option
              [<JsonPropertyName("isOverAge16")>]
              IsOverAge16: bool option
              [<JsonPropertyName("isOverAge18")>]
              IsOverAge18: bool option }

        type FeedViewPref =
            { [<JsonPropertyName("feed")>]
              Feed: string
              [<JsonPropertyName("hideQuotePosts")>]
              HideQuotePosts: bool option
              [<JsonPropertyName("hideReplies")>]
              HideReplies: bool option
              [<JsonPropertyName("hideRepliesByLikeCount")>]
              HideRepliesByLikeCount: int64 option
              [<JsonPropertyName("hideRepliesByUnfollowed")>]
              HideRepliesByUnfollowed: bool option
              [<JsonPropertyName("hideReposts")>]
              HideReposts: bool option }

        type HiddenPostsPref =
            { [<JsonPropertyName("items")>]
              Items: string list }

        type InterestsPref =
            { [<JsonPropertyName("tags")>]
              Tags: string list }

        /// The subject's followers whom you also follow
        type KnownFollowers =
            { [<JsonPropertyName("count")>]
              Count: int64
              [<JsonPropertyName("followers")>]
              Followers: Defs.ProfileViewBasic list }

        type LabelerPrefItem =
            { [<JsonPropertyName("did")>]
              Did: string }

        type LabelersPref =
            { [<JsonPropertyName("labelers")>]
              Labelers: Defs.LabelerPrefItem list }

        /// Preferences for live events.
        type LiveEventPreferences =
            { [<JsonPropertyName("hiddenFeedIds")>]
              HiddenFeedIds: string list option
              [<JsonPropertyName("hideAllFeeds")>]
              HideAllFeeds: bool option }

        /// A word that the account owner has muted.
        type MutedWord =
            { [<JsonPropertyName("actorTarget")>]
              ActorTarget: string option
              [<JsonPropertyName("expiresAt")>]
              ExpiresAt: string option
              [<JsonPropertyName("id")>]
              Id: string option
              [<JsonPropertyName("targets")>]
              Targets: Defs.MutedWordTarget list
              [<JsonPropertyName("value")>]
              Value: string }

        type MutedWordTarget = string

        type MutedWordsPref =
            { [<JsonPropertyName("items")>]
              Items: Defs.MutedWord list }

        /// A new user experiences (NUX) storage object
        type Nux =
            { [<JsonPropertyName("completed")>]
              Completed: bool
              [<JsonPropertyName("data")>]
              Data: string option
              [<JsonPropertyName("expiresAt")>]
              ExpiresAt: string option
              [<JsonPropertyName("id")>]
              Id: string }

        type PersonalDetailsPref =
            { [<JsonPropertyName("birthDate")>]
              BirthDate: string option }

        /// Default post interaction settings for the account. These values should be applied as default values when creating new posts. These refs should mirror the threadgate and postgate records exactly.
        type PostInteractionSettingsPref =
            { [<JsonPropertyName("postgateEmbeddingRules")>]
              PostgateEmbeddingRules: JsonElement list option
              [<JsonPropertyName("threadgateAllowRules")>]
              ThreadgateAllowRules: JsonElement list option }

        type Preferences = JsonElement list

        type ProfileAssociated =
            { [<JsonPropertyName("activitySubscription")>]
              ActivitySubscription: Defs.ProfileAssociatedActivitySubscription option
              [<JsonPropertyName("chat")>]
              Chat: Defs.ProfileAssociatedChat option
              [<JsonPropertyName("feedgens")>]
              Feedgens: int64 option
              [<JsonPropertyName("germ")>]
              Germ: Defs.ProfileAssociatedGerm option
              [<JsonPropertyName("labeler")>]
              Labeler: bool option
              [<JsonPropertyName("lists")>]
              Lists: int64 option
              [<JsonPropertyName("starterPacks")>]
              StarterPacks: int64 option }

        type ProfileAssociatedActivitySubscription =
            { [<JsonPropertyName("allowSubscriptions")>]
              AllowSubscriptions: string }

        type ProfileAssociatedChat =
            { [<JsonPropertyName("allowIncoming")>]
              AllowIncoming: string }

        type ProfileAssociatedGerm =
            { [<JsonPropertyName("messageMeUrl")>]
              MessageMeUrl: string
              [<JsonPropertyName("showButtonTo")>]
              ShowButtonTo: string }

        type ProfileView =
            { [<JsonPropertyName("associated")>]
              Associated: Defs.ProfileAssociated option
              [<JsonPropertyName("avatar")>]
              Avatar: string option
              [<JsonPropertyName("createdAt")>]
              CreatedAt: string option
              [<JsonPropertyName("debug")>]
              Debug: JsonElement option
              [<JsonPropertyName("description")>]
              Description: string option
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("displayName")>]
              DisplayName: string option
              [<JsonPropertyName("handle")>]
              Handle: string
              [<JsonPropertyName("indexedAt")>]
              IndexedAt: string option
              [<JsonPropertyName("labels")>]
              Labels: ComAtprotoLabel.Defs.Label list option
              [<JsonPropertyName("pronouns")>]
              Pronouns: string option
              [<JsonPropertyName("status")>]
              Status: Defs.StatusView option
              [<JsonPropertyName("verification")>]
              Verification: Defs.VerificationState option
              [<JsonPropertyName("viewer")>]
              Viewer: Defs.ViewerState option }

        type ProfileViewBasic =
            { [<JsonPropertyName("associated")>]
              Associated: Defs.ProfileAssociated option
              [<JsonPropertyName("avatar")>]
              Avatar: string option
              [<JsonPropertyName("createdAt")>]
              CreatedAt: string option
              [<JsonPropertyName("debug")>]
              Debug: JsonElement option
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("displayName")>]
              DisplayName: string option
              [<JsonPropertyName("handle")>]
              Handle: string
              [<JsonPropertyName("labels")>]
              Labels: ComAtprotoLabel.Defs.Label list option
              [<JsonPropertyName("pronouns")>]
              Pronouns: string option
              [<JsonPropertyName("status")>]
              Status: Defs.StatusView option
              [<JsonPropertyName("verification")>]
              Verification: Defs.VerificationState option
              [<JsonPropertyName("viewer")>]
              Viewer: Defs.ViewerState option }

        type ProfileViewDetailed =
            { [<JsonPropertyName("associated")>]
              Associated: Defs.ProfileAssociated option
              [<JsonPropertyName("avatar")>]
              Avatar: string option
              [<JsonPropertyName("banner")>]
              Banner: string option
              [<JsonPropertyName("createdAt")>]
              CreatedAt: string option
              [<JsonPropertyName("debug")>]
              Debug: JsonElement option
              [<JsonPropertyName("description")>]
              Description: string option
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("displayName")>]
              DisplayName: string option
              [<JsonPropertyName("followersCount")>]
              FollowersCount: int64 option
              [<JsonPropertyName("followsCount")>]
              FollowsCount: int64 option
              [<JsonPropertyName("handle")>]
              Handle: string
              [<JsonPropertyName("indexedAt")>]
              IndexedAt: string option
              [<JsonPropertyName("joinedViaStarterPack")>]
              JoinedViaStarterPack: AppBskyGraph.Defs.StarterPackViewBasic option
              [<JsonPropertyName("labels")>]
              Labels: ComAtprotoLabel.Defs.Label list option
              [<JsonPropertyName("pinnedPost")>]
              PinnedPost: ComAtprotoRepo.StrongRef.StrongRef option
              [<JsonPropertyName("postsCount")>]
              PostsCount: int64 option
              [<JsonPropertyName("pronouns")>]
              Pronouns: string option
              [<JsonPropertyName("status")>]
              Status: Defs.StatusView option
              [<JsonPropertyName("verification")>]
              Verification: Defs.VerificationState option
              [<JsonPropertyName("viewer")>]
              Viewer: Defs.ViewerState option
              [<JsonPropertyName("website")>]
              Website: string option }

        type SavedFeed =
            { [<JsonPropertyName("id")>]
              Id: string
              [<JsonPropertyName("pinned")>]
              Pinned: bool
              [<JsonPropertyName("type")>]
              Type: string
              [<JsonPropertyName("value")>]
              Value: string }

        type SavedFeedsPref =
            { [<JsonPropertyName("pinned")>]
              Pinned: string list
              [<JsonPropertyName("saved")>]
              Saved: string list
              [<JsonPropertyName("timelineIndex")>]
              TimelineIndex: int64 option }

        type SavedFeedsPrefV2 =
            { [<JsonPropertyName("items")>]
              Items: Defs.SavedFeed list }

        type StatusView =
            { [<JsonPropertyName("cid")>]
              Cid: string option
              [<JsonPropertyName("embed")>]
              Embed: JsonElement option
              [<JsonPropertyName("expiresAt")>]
              ExpiresAt: string option
              [<JsonPropertyName("isActive")>]
              IsActive: bool option
              [<JsonPropertyName("isDisabled")>]
              IsDisabled: bool option
              [<JsonPropertyName("record")>]
              Record: JsonElement
              [<JsonPropertyName("status")>]
              Status: string
              [<JsonPropertyName("uri")>]
              Uri: string option }

        type ThreadViewPref =
            { [<JsonPropertyName("sort")>]
              Sort: string option }

        /// Preferences for how verified accounts appear in the app.
        type VerificationPrefs =
            { [<JsonPropertyName("hideBadges")>]
              HideBadges: bool option }

        /// Represents the verification information about the user this object is attached to.
        type VerificationState =
            { [<JsonPropertyName("trustedVerifierStatus")>]
              TrustedVerifierStatus: string
              [<JsonPropertyName("verifications")>]
              Verifications: Defs.VerificationView list
              [<JsonPropertyName("verifiedStatus")>]
              VerifiedStatus: string }

        /// An individual verification for an associated subject.
        type VerificationView =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("isValid")>]
              IsValid: bool
              [<JsonPropertyName("issuer")>]
              Issuer: string
              [<JsonPropertyName("uri")>]
              Uri: string }

        /// Metadata about the requesting account's relationship with the subject account. Only has meaningful content for authed requests.
        type ViewerState =
            { [<JsonPropertyName("activitySubscription")>]
              ActivitySubscription: AppBskyNotification.Defs.ActivitySubscription option
              [<JsonPropertyName("blockedBy")>]
              BlockedBy: bool option
              [<JsonPropertyName("blocking")>]
              Blocking: string option
              [<JsonPropertyName("blockingByList")>]
              BlockingByList: AppBskyGraph.Defs.ListViewBasic option
              [<JsonPropertyName("followedBy")>]
              FollowedBy: string option
              [<JsonPropertyName("following")>]
              Following: string option
              [<JsonPropertyName("knownFollowers")>]
              KnownFollowers: Defs.KnownFollowers option
              [<JsonPropertyName("muted")>]
              Muted: bool option
              [<JsonPropertyName("mutedByList")>]
              MutedByList: AppBskyGraph.Defs.ListViewBasic option }

    module GetPreferences =
        [<Literal>]
        let TypeId = "app.bsky.actor.getPreferences"

        type Output =
            { [<JsonPropertyName("preferences")>]
              Preferences: Defs.Preferences }

    module GetProfile =
        [<Literal>]
        let TypeId = "app.bsky.actor.getProfile"

        type Params =
            { [<JsonPropertyName("actor")>]
              Actor: string }

    module GetProfiles =
        [<Literal>]
        let TypeId = "app.bsky.actor.getProfiles"

        type Params =
            { [<JsonPropertyName("actors")>]
              Actors: string list }

        type Output =
            { [<JsonPropertyName("profiles")>]
              Profiles: Defs.ProfileViewDetailed list }

    module GetSuggestions =
        [<Literal>]
        let TypeId = "app.bsky.actor.getSuggestions"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("actors")>]
              Actors: Defs.ProfileView list
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("recId")>]
              RecId: int64 option }

    module Profile =
        [<Literal>]
        let TypeId = "app.bsky.actor.profile"

        /// A declaration of a Bluesky account profile.
        type Profile =
            { [<JsonPropertyName("avatar")>]
              Avatar: JsonElement option
              [<JsonPropertyName("banner")>]
              Banner: JsonElement option
              [<JsonPropertyName("createdAt")>]
              CreatedAt: string option
              [<JsonPropertyName("description")>]
              Description: string option
              [<JsonPropertyName("displayName")>]
              DisplayName: string option
              [<JsonPropertyName("joinedViaStarterPack")>]
              JoinedViaStarterPack: ComAtprotoRepo.StrongRef.StrongRef option
              [<JsonPropertyName("labels")>]
              Labels: JsonElement option
              [<JsonPropertyName("pinnedPost")>]
              PinnedPost: ComAtprotoRepo.StrongRef.StrongRef option
              [<JsonPropertyName("pronouns")>]
              Pronouns: string option
              [<JsonPropertyName("website")>]
              Website: string option }

    module PutPreferences =
        [<Literal>]
        let TypeId = "app.bsky.actor.putPreferences"

        type Input =
            { [<JsonPropertyName("preferences")>]
              Preferences: Defs.Preferences }

    module SearchActors =
        [<Literal>]
        let TypeId = "app.bsky.actor.searchActors"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("q")>]
              Q: string option
              [<JsonPropertyName("term")>]
              Term: string option }

        type Output =
            { [<JsonPropertyName("actors")>]
              Actors: Defs.ProfileView list
              [<JsonPropertyName("cursor")>]
              Cursor: string option }

    module SearchActorsTypeahead =
        [<Literal>]
        let TypeId = "app.bsky.actor.searchActorsTypeahead"

        type Params =
            { [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("q")>]
              Q: string option
              [<JsonPropertyName("term")>]
              Term: string option }

        type Output =
            { [<JsonPropertyName("actors")>]
              Actors: Defs.ProfileViewBasic list }

    module Status =
        [<Literal>]
        let TypeId = "app.bsky.actor.status"

        /// A declaration of a Bluesky account status.
        type Status =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("durationMinutes")>]
              DurationMinutes: int64 option
              [<JsonPropertyName("embed")>]
              Embed: JsonElement option
              [<JsonPropertyName("status")>]
              Status: string }

        [<Literal>]
        let Live = "app.bsky.actor.status#live"

module AppBskyAgeassurance =
    module Begin =
        [<Literal>]
        let TypeId = "app.bsky.ageassurance.begin"

        type Input =
            { [<JsonPropertyName("countryCode")>]
              CountryCode: string
              [<JsonPropertyName("email")>]
              Email: string
              [<JsonPropertyName("language")>]
              Language: string
              [<JsonPropertyName("regionCode")>]
              RegionCode: string option }

        module Errors =
            [<Literal>]
            let InvalidEmail = "InvalidEmail"

            [<Literal>]
            let DidTooLong = "DidTooLong"

            [<Literal>]
            let InvalidInitiation = "InvalidInitiation"

            [<Literal>]
            let RegionNotSupported = "RegionNotSupported"

    module Defs =
        type Access = string

        ///
        type Config =
            { [<JsonPropertyName("regions")>]
              Regions: Defs.ConfigRegion list }

        /// The Age Assurance configuration for a specific region.
        type ConfigRegion =
            { [<JsonPropertyName("countryCode")>]
              CountryCode: string
              [<JsonPropertyName("minAccessAge")>]
              MinAccessAge: int64
              [<JsonPropertyName("regionCode")>]
              RegionCode: string option
              [<JsonPropertyName("rules")>]
              Rules: JsonElement list }

        /// Age Assurance rule that applies by default.
        type ConfigRegionRuleDefault =
            { [<JsonPropertyName("access")>]
              Access: Defs.Access }

        /// Age Assurance rule that applies if the account is equal-to or newer than a certain date.
        type ConfigRegionRuleIfAccountNewerThan =
            { [<JsonPropertyName("access")>]
              Access: Defs.Access
              [<JsonPropertyName("date")>]
              Date: string }

        /// Age Assurance rule that applies if the account is older than a certain date.
        type ConfigRegionRuleIfAccountOlderThan =
            { [<JsonPropertyName("access")>]
              Access: Defs.Access
              [<JsonPropertyName("date")>]
              Date: string }

        /// Age Assurance rule that applies if the user has been assured to be equal-to or over a certain age.
        type ConfigRegionRuleIfAssuredOverAge =
            { [<JsonPropertyName("access")>]
              Access: Defs.Access
              [<JsonPropertyName("age")>]
              Age: int64 }

        /// Age Assurance rule that applies if the user has been assured to be under a certain age.
        type ConfigRegionRuleIfAssuredUnderAge =
            { [<JsonPropertyName("access")>]
              Access: Defs.Access
              [<JsonPropertyName("age")>]
              Age: int64 }

        /// Age Assurance rule that applies if the user has declared themselves equal-to or over a certain age.
        type ConfigRegionRuleIfDeclaredOverAge =
            { [<JsonPropertyName("access")>]
              Access: Defs.Access
              [<JsonPropertyName("age")>]
              Age: int64 }

        /// Age Assurance rule that applies if the user has declared themselves under a certain age.
        type ConfigRegionRuleIfDeclaredUnderAge =
            { [<JsonPropertyName("access")>]
              Access: Defs.Access
              [<JsonPropertyName("age")>]
              Age: int64 }

        /// Object used to store Age Assurance data in stash.
        type Event =
            { [<JsonPropertyName("access")>]
              Access: string
              [<JsonPropertyName("attemptId")>]
              AttemptId: string
              [<JsonPropertyName("completeIp")>]
              CompleteIp: string option
              [<JsonPropertyName("completeUa")>]
              CompleteUa: string option
              [<JsonPropertyName("countryCode")>]
              CountryCode: string
              [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("email")>]
              Email: string option
              [<JsonPropertyName("initIp")>]
              InitIp: string option
              [<JsonPropertyName("initUa")>]
              InitUa: string option
              [<JsonPropertyName("regionCode")>]
              RegionCode: string option
              [<JsonPropertyName("status")>]
              Status: string }

        /// The user's computed Age Assurance state.
        type State =
            { [<JsonPropertyName("access")>]
              Access: Defs.Access
              [<JsonPropertyName("lastInitiatedAt")>]
              LastInitiatedAt: string option
              [<JsonPropertyName("status")>]
              Status: Defs.Status }

        /// Additional metadata needed to compute Age Assurance state client-side.
        type StateMetadata =
            { [<JsonPropertyName("accountCreatedAt")>]
              AccountCreatedAt: string option }

        type Status = string

    module GetConfig =
        [<Literal>]
        let TypeId = "app.bsky.ageassurance.getConfig"

    module GetState =
        [<Literal>]
        let TypeId = "app.bsky.ageassurance.getState"

        type Params =
            { [<JsonPropertyName("countryCode")>]
              CountryCode: string
              [<JsonPropertyName("regionCode")>]
              RegionCode: string option }

        type Output =
            { [<JsonPropertyName("metadata")>]
              Metadata: Defs.StateMetadata
              [<JsonPropertyName("state")>]
              State: Defs.State }

module AppBskyBookmark =
    module CreateBookmark =
        [<Literal>]
        let TypeId = "app.bsky.bookmark.createBookmark"

        type Input =
            { [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("uri")>]
              Uri: string }

        module Errors =
            [<Literal>]
            let UnsupportedCollection = "UnsupportedCollection"

    module Defs =
        /// Object used to store bookmark data in stash.
        type Bookmark =
            { [<JsonPropertyName("subject")>]
              Subject: ComAtprotoRepo.StrongRef.StrongRef }

        type BookmarkView =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string option
              [<JsonPropertyName("item")>]
              Item: JsonElement
              [<JsonPropertyName("subject")>]
              Subject: ComAtprotoRepo.StrongRef.StrongRef }

    module DeleteBookmark =
        [<Literal>]
        let TypeId = "app.bsky.bookmark.deleteBookmark"

        type Input =
            { [<JsonPropertyName("uri")>]
              Uri: string }

        module Errors =
            [<Literal>]
            let UnsupportedCollection = "UnsupportedCollection"

    module GetBookmarks =
        [<Literal>]
        let TypeId = "app.bsky.bookmark.getBookmarks"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("bookmarks")>]
              Bookmarks: Defs.BookmarkView list
              [<JsonPropertyName("cursor")>]
              Cursor: string option }

module AppBskyContact =
    module Defs =
        /// Associates a profile with the positional index of the contact import input in the call to `app.bsky.contact.importContacts`, so clients can know which phone caused a particular match.
        type MatchAndContactIndex =
            { [<JsonPropertyName("contactIndex")>]
              ContactIndex: int64
              [<JsonPropertyName("match")>]
              Match: AppBskyActor.Defs.ProfileView }

        /// A stash object to be sent via bsync representing a notification to be created.
        type Notification =
            { [<JsonPropertyName("from")>]
              From: string
              [<JsonPropertyName("to")>]
              To: string }

        type SyncStatus =
            { [<JsonPropertyName("matchesCount")>]
              MatchesCount: int64
              [<JsonPropertyName("syncedAt")>]
              SyncedAt: string }

    module DismissMatch =
        [<Literal>]
        let TypeId = "app.bsky.contact.dismissMatch"

        type Input =
            { [<JsonPropertyName("subject")>]
              Subject: string }

        module Errors =
            [<Literal>]
            let InvalidDid = "InvalidDid"

            [<Literal>]
            let InternalError = "InternalError"

    module GetMatches =
        [<Literal>]
        let TypeId = "app.bsky.contact.getMatches"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("matches")>]
              Matches: AppBskyActor.Defs.ProfileView list }

        module Errors =
            [<Literal>]
            let InvalidDid = "InvalidDid"

            [<Literal>]
            let InvalidLimit = "InvalidLimit"

            [<Literal>]
            let InvalidCursor = "InvalidCursor"

            [<Literal>]
            let InternalError = "InternalError"

    module GetSyncStatus =
        [<Literal>]
        let TypeId = "app.bsky.contact.getSyncStatus"

        type Output =
            { [<JsonPropertyName("syncStatus")>]
              SyncStatus: Defs.SyncStatus option }

        module Errors =
            [<Literal>]
            let InvalidDid = "InvalidDid"

            [<Literal>]
            let InternalError = "InternalError"

    module ImportContacts =
        [<Literal>]
        let TypeId = "app.bsky.contact.importContacts"

        type Input =
            { [<JsonPropertyName("contacts")>]
              Contacts: string list
              [<JsonPropertyName("token")>]
              Token: string }

        type Output =
            { [<JsonPropertyName("matchesAndContactIndexes")>]
              MatchesAndContactIndexes: Defs.MatchAndContactIndex list }

        module Errors =
            [<Literal>]
            let InvalidDid = "InvalidDid"

            [<Literal>]
            let InvalidContacts = "InvalidContacts"

            [<Literal>]
            let TooManyContacts = "TooManyContacts"

            [<Literal>]
            let InvalidToken = "InvalidToken"

            [<Literal>]
            let InternalError = "InternalError"

    module RemoveData =
        [<Literal>]
        let TypeId = "app.bsky.contact.removeData"

        module Errors =
            [<Literal>]
            let InvalidDid = "InvalidDid"

            [<Literal>]
            let InternalError = "InternalError"

    module SendNotification =
        [<Literal>]
        let TypeId = "app.bsky.contact.sendNotification"

        type Input =
            { [<JsonPropertyName("from")>]
              From: string
              [<JsonPropertyName("to")>]
              To: string }

    module StartPhoneVerification =
        [<Literal>]
        let TypeId = "app.bsky.contact.startPhoneVerification"

        type Input =
            { [<JsonPropertyName("phone")>]
              Phone: string }

        module Errors =
            [<Literal>]
            let RateLimitExceeded = "RateLimitExceeded"

            [<Literal>]
            let InvalidDid = "InvalidDid"

            [<Literal>]
            let InvalidPhone = "InvalidPhone"

            [<Literal>]
            let InternalError = "InternalError"

    module VerifyPhone =
        [<Literal>]
        let TypeId = "app.bsky.contact.verifyPhone"

        type Input =
            { [<JsonPropertyName("code")>]
              Code: string
              [<JsonPropertyName("phone")>]
              Phone: string }

        type Output =
            { [<JsonPropertyName("token")>]
              Token: string }

        module Errors =
            [<Literal>]
            let RateLimitExceeded = "RateLimitExceeded"

            [<Literal>]
            let InvalidDid = "InvalidDid"

            [<Literal>]
            let InvalidPhone = "InvalidPhone"

            [<Literal>]
            let InvalidCode = "InvalidCode"

            [<Literal>]
            let InternalError = "InternalError"

module AppBskyDraft =
    module CreateDraft =
        [<Literal>]
        let TypeId = "app.bsky.draft.createDraft"

        type Input =
            { [<JsonPropertyName("draft")>]
              Draft: Defs.Draft }

        type Output =
            { [<JsonPropertyName("id")>]
              Id: string }

        module Errors =
            [<Literal>]
            let DraftLimitReached = "DraftLimitReached"

    module Defs =
        /// A draft containing an array of draft posts.
        type Draft =
            { [<JsonPropertyName("deviceId")>]
              DeviceId: string option
              [<JsonPropertyName("deviceName")>]
              DeviceName: string option
              [<JsonPropertyName("langs")>]
              Langs: string list option
              [<JsonPropertyName("postgateEmbeddingRules")>]
              PostgateEmbeddingRules: JsonElement list option
              [<JsonPropertyName("posts")>]
              Posts: Defs.DraftPost list
              [<JsonPropertyName("threadgateAllow")>]
              ThreadgateAllow: JsonElement list option }

        type DraftEmbedCaption =
            { [<JsonPropertyName("content")>]
              Content: string
              [<JsonPropertyName("lang")>]
              Lang: string }

        type DraftEmbedExternal =
            { [<JsonPropertyName("uri")>]
              Uri: string }

        type DraftEmbedImage =
            { [<JsonPropertyName("alt")>]
              Alt: string option
              [<JsonPropertyName("localRef")>]
              LocalRef: Defs.DraftEmbedLocalRef }

        type DraftEmbedLocalRef =
            { [<JsonPropertyName("path")>]
              Path: string }

        type DraftEmbedRecord =
            { [<JsonPropertyName("record")>]
              Record: ComAtprotoRepo.StrongRef.StrongRef }

        type DraftEmbedVideo =
            { [<JsonPropertyName("alt")>]
              Alt: string option
              [<JsonPropertyName("captions")>]
              Captions: Defs.DraftEmbedCaption list option
              [<JsonPropertyName("localRef")>]
              LocalRef: Defs.DraftEmbedLocalRef }

        /// One of the posts that compose a draft.
        type DraftPost =
            { [<JsonPropertyName("embedExternals")>]
              EmbedExternals: Defs.DraftEmbedExternal list option
              [<JsonPropertyName("embedImages")>]
              EmbedImages: Defs.DraftEmbedImage list option
              [<JsonPropertyName("embedRecords")>]
              EmbedRecords: Defs.DraftEmbedRecord list option
              [<JsonPropertyName("embedVideos")>]
              EmbedVideos: Defs.DraftEmbedVideo list option
              [<JsonPropertyName("labels")>]
              Labels: JsonElement option
              [<JsonPropertyName("text")>]
              Text: string }

        /// View to present drafts data to users.
        type DraftView =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("draft")>]
              Draft: Defs.Draft
              [<JsonPropertyName("id")>]
              Id: string
              [<JsonPropertyName("updatedAt")>]
              UpdatedAt: string }

        /// A draft with an identifier, used to store drafts in private storage (stash).
        type DraftWithId =
            { [<JsonPropertyName("draft")>]
              Draft: Defs.Draft
              [<JsonPropertyName("id")>]
              Id: string }

    module DeleteDraft =
        [<Literal>]
        let TypeId = "app.bsky.draft.deleteDraft"

        type Input =
            { [<JsonPropertyName("id")>]
              Id: string }

    module GetDrafts =
        [<Literal>]
        let TypeId = "app.bsky.draft.getDrafts"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("drafts")>]
              Drafts: Defs.DraftView list }

    module UpdateDraft =
        [<Literal>]
        let TypeId = "app.bsky.draft.updateDraft"

        type Input =
            { [<JsonPropertyName("draft")>]
              Draft: Defs.DraftWithId }

module AppBskyUnspecced =
    module Defs =
        /// Object used to store age assurance data in stash.
        type AgeAssuranceEvent =
            { [<JsonPropertyName("attemptId")>]
              AttemptId: string
              [<JsonPropertyName("completeIp")>]
              CompleteIp: string option
              [<JsonPropertyName("completeUa")>]
              CompleteUa: string option
              [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("email")>]
              Email: string option
              [<JsonPropertyName("initIp")>]
              InitIp: string option
              [<JsonPropertyName("initUa")>]
              InitUa: string option
              [<JsonPropertyName("status")>]
              Status: string }

        /// The computed state of the age assurance process, returned to the user in question on certain authenticated requests.
        type AgeAssuranceState =
            { [<JsonPropertyName("lastInitiatedAt")>]
              LastInitiatedAt: string option
              [<JsonPropertyName("status")>]
              Status: string }

        type SkeletonSearchActor =
            { [<JsonPropertyName("did")>]
              Did: string }

        type SkeletonSearchPost =
            { [<JsonPropertyName("uri")>]
              Uri: string }

        type SkeletonSearchStarterPack =
            { [<JsonPropertyName("uri")>]
              Uri: string }

        type SkeletonTrend =
            { [<JsonPropertyName("category")>]
              Category: string option
              [<JsonPropertyName("dids")>]
              Dids: string list
              [<JsonPropertyName("displayName")>]
              DisplayName: string
              [<JsonPropertyName("link")>]
              Link: string
              [<JsonPropertyName("postCount")>]
              PostCount: int64
              [<JsonPropertyName("startedAt")>]
              StartedAt: string
              [<JsonPropertyName("status")>]
              Status: string option
              [<JsonPropertyName("topic")>]
              Topic: string }

        type ThreadItemBlocked =
            { [<JsonPropertyName("author")>]
              Author: AppBskyFeed.Defs.BlockedAuthor }

        type ThreadItemPost =
            { [<JsonPropertyName("hiddenByThreadgate")>]
              HiddenByThreadgate: bool
              [<JsonPropertyName("moreParents")>]
              MoreParents: bool
              [<JsonPropertyName("moreReplies")>]
              MoreReplies: int64
              [<JsonPropertyName("mutedByViewer")>]
              MutedByViewer: bool
              [<JsonPropertyName("opThread")>]
              OpThread: bool
              [<JsonPropertyName("post")>]
              Post: AppBskyFeed.Defs.PostView }

        type TrendView =
            { [<JsonPropertyName("actors")>]
              Actors: AppBskyActor.Defs.ProfileViewBasic list
              [<JsonPropertyName("category")>]
              Category: string option
              [<JsonPropertyName("displayName")>]
              DisplayName: string
              [<JsonPropertyName("link")>]
              Link: string
              [<JsonPropertyName("postCount")>]
              PostCount: int64
              [<JsonPropertyName("startedAt")>]
              StartedAt: string
              [<JsonPropertyName("status")>]
              Status: string option
              [<JsonPropertyName("topic")>]
              Topic: string }

        type TrendingTopic =
            { [<JsonPropertyName("description")>]
              Description: string option
              [<JsonPropertyName("displayName")>]
              DisplayName: string option
              [<JsonPropertyName("link")>]
              Link: string
              [<JsonPropertyName("topic")>]
              Topic: string }

    module GetAgeAssuranceState =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.getAgeAssuranceState"

    module GetConfig =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.getConfig"

        type Output =
            { [<JsonPropertyName("checkEmailConfirmed")>]
              CheckEmailConfirmed: bool option
              [<JsonPropertyName("liveNow")>]
              LiveNow: GetConfig.LiveNowConfig list option }

        type LiveNowConfig =
            { [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("domains")>]
              Domains: string list }

    module GetOnboardingSuggestedStarterPacks =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.getOnboardingSuggestedStarterPacks"

        type Params =
            { [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("starterPacks")>]
              StarterPacks: AppBskyGraph.Defs.StarterPackView list }

    module GetOnboardingSuggestedStarterPacksSkeleton =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.getOnboardingSuggestedStarterPacksSkeleton"

        type Params =
            { [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("viewer")>]
              Viewer: string option }

        type Output =
            { [<JsonPropertyName("starterPacks")>]
              StarterPacks: string list }

    module GetOnboardingSuggestedUsersSkeleton =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.getOnboardingSuggestedUsersSkeleton"

        type Params =
            { [<JsonPropertyName("category")>]
              Category: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("viewer")>]
              Viewer: string option }

        type Output =
            { [<JsonPropertyName("dids")>]
              Dids: string list
              [<JsonPropertyName("recId")>]
              RecId: string option }

    module GetPopularFeedGenerators =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.getPopularFeedGenerators"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("query")>]
              Query: string option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("feeds")>]
              Feeds: AppBskyFeed.Defs.GeneratorView list }

    module GetPostThreadOtherV2 =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.getPostThreadOtherV2"

        type Params =
            { [<JsonPropertyName("anchor")>]
              Anchor: string }

        type Output =
            { [<JsonPropertyName("thread")>]
              Thread: GetPostThreadOtherV2.ThreadItem list }

        type ThreadItem =
            { [<JsonPropertyName("depth")>]
              Depth: int64
              [<JsonPropertyName("uri")>]
              Uri: string
              [<JsonPropertyName("value")>]
              Value: JsonElement }

    module GetPostThreadV2 =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.getPostThreadV2"

        type Params =
            { [<JsonPropertyName("above")>]
              Above: bool option
              [<JsonPropertyName("anchor")>]
              Anchor: string
              [<JsonPropertyName("below")>]
              Below: int64 option
              [<JsonPropertyName("branchingFactor")>]
              BranchingFactor: int64 option
              [<JsonPropertyName("sort")>]
              Sort: string option }

        type Output =
            { [<JsonPropertyName("hasOtherReplies")>]
              HasOtherReplies: bool
              [<JsonPropertyName("thread")>]
              Thread: GetPostThreadV2.ThreadItem list
              [<JsonPropertyName("threadgate")>]
              Threadgate: AppBskyFeed.Defs.ThreadgateView option }

        type ThreadItem =
            { [<JsonPropertyName("depth")>]
              Depth: int64
              [<JsonPropertyName("uri")>]
              Uri: string
              [<JsonPropertyName("value")>]
              Value: JsonElement }

    module GetSuggestedFeeds =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.getSuggestedFeeds"

        type Params =
            { [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("feeds")>]
              Feeds: AppBskyFeed.Defs.GeneratorView list }

    module GetSuggestedFeedsSkeleton =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.getSuggestedFeedsSkeleton"

        type Params =
            { [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("viewer")>]
              Viewer: string option }

        type Output =
            { [<JsonPropertyName("feeds")>]
              Feeds: string list }

    module GetSuggestedOnboardingUsers =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.getSuggestedOnboardingUsers"

        type Params =
            { [<JsonPropertyName("category")>]
              Category: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("actors")>]
              Actors: AppBskyActor.Defs.ProfileView list
              [<JsonPropertyName("recId")>]
              RecId: string option }

    module GetSuggestedStarterPacks =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.getSuggestedStarterPacks"

        type Params =
            { [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("starterPacks")>]
              StarterPacks: AppBskyGraph.Defs.StarterPackView list }

    module GetSuggestedStarterPacksSkeleton =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.getSuggestedStarterPacksSkeleton"

        type Params =
            { [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("viewer")>]
              Viewer: string option }

        type Output =
            { [<JsonPropertyName("starterPacks")>]
              StarterPacks: string list }

    module GetSuggestedUsers =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.getSuggestedUsers"

        type Params =
            { [<JsonPropertyName("category")>]
              Category: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("actors")>]
              Actors: AppBskyActor.Defs.ProfileView list
              [<JsonPropertyName("recId")>]
              RecId: string option }

    module GetSuggestedUsersSkeleton =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.getSuggestedUsersSkeleton"

        type Params =
            { [<JsonPropertyName("category")>]
              Category: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("viewer")>]
              Viewer: string option }

        type Output =
            { [<JsonPropertyName("dids")>]
              Dids: string list
              [<JsonPropertyName("recId")>]
              RecId: string option }

    module GetSuggestionsSkeleton =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.getSuggestionsSkeleton"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("relativeToDid")>]
              RelativeToDid: string option
              [<JsonPropertyName("viewer")>]
              Viewer: string option }

        type Output =
            { [<JsonPropertyName("actors")>]
              Actors: Defs.SkeletonSearchActor list
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("recId")>]
              RecId: int64 option
              [<JsonPropertyName("recIdStr")>]
              RecIdStr: string option
              [<JsonPropertyName("relativeToDid")>]
              RelativeToDid: string option }

    module GetTaggedSuggestions =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.getTaggedSuggestions"

        type Output =
            { [<JsonPropertyName("suggestions")>]
              Suggestions: GetTaggedSuggestions.Suggestion list }

        type Suggestion =
            { [<JsonPropertyName("subject")>]
              Subject: string
              [<JsonPropertyName("subjectType")>]
              SubjectType: string
              [<JsonPropertyName("tag")>]
              Tag: string }

    module GetTrendingTopics =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.getTrendingTopics"

        type Params =
            { [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("viewer")>]
              Viewer: string option }

        type Output =
            { [<JsonPropertyName("suggested")>]
              Suggested: Defs.TrendingTopic list
              [<JsonPropertyName("topics")>]
              Topics: Defs.TrendingTopic list }

    module GetTrends =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.getTrends"

        type Params =
            { [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("trends")>]
              Trends: Defs.TrendView list }

    module GetTrendsSkeleton =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.getTrendsSkeleton"

        type Params =
            { [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("viewer")>]
              Viewer: string option }

        type Output =
            { [<JsonPropertyName("trends")>]
              Trends: Defs.SkeletonTrend list }

    module InitAgeAssurance =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.initAgeAssurance"

        type Input =
            { [<JsonPropertyName("countryCode")>]
              CountryCode: string
              [<JsonPropertyName("email")>]
              Email: string
              [<JsonPropertyName("language")>]
              Language: string }

        module Errors =
            [<Literal>]
            let InvalidEmail = "InvalidEmail"

            [<Literal>]
            let DidTooLong = "DidTooLong"

            [<Literal>]
            let InvalidInitiation = "InvalidInitiation"

    module SearchActorsSkeleton =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.searchActorsSkeleton"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("q")>]
              Q: string
              [<JsonPropertyName("typeahead")>]
              Typeahead: bool option
              [<JsonPropertyName("viewer")>]
              Viewer: string option }

        type Output =
            { [<JsonPropertyName("actors")>]
              Actors: Defs.SkeletonSearchActor list
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("hitsTotal")>]
              HitsTotal: int64 option }

        module Errors =
            [<Literal>]
            let BadQueryString = "BadQueryString"

    module SearchPostsSkeleton =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.searchPostsSkeleton"

        type Params =
            { [<JsonPropertyName("author")>]
              Author: string option
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("domain")>]
              Domain: string option
              [<JsonPropertyName("lang")>]
              Lang: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("mentions")>]
              Mentions: string option
              [<JsonPropertyName("q")>]
              Q: string
              [<JsonPropertyName("since")>]
              Since: string option
              [<JsonPropertyName("sort")>]
              Sort: string option
              [<JsonPropertyName("tag")>]
              Tag: string list option
              [<JsonPropertyName("until")>]
              Until: string option
              [<JsonPropertyName("url")>]
              Url: string option
              [<JsonPropertyName("viewer")>]
              Viewer: string option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("hitsTotal")>]
              HitsTotal: int64 option
              [<JsonPropertyName("posts")>]
              Posts: Defs.SkeletonSearchPost list }

        module Errors =
            [<Literal>]
            let BadQueryString = "BadQueryString"

    module SearchStarterPacksSkeleton =
        [<Literal>]
        let TypeId = "app.bsky.unspecced.searchStarterPacksSkeleton"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("q")>]
              Q: string
              [<JsonPropertyName("viewer")>]
              Viewer: string option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("hitsTotal")>]
              HitsTotal: int64 option
              [<JsonPropertyName("starterPacks")>]
              StarterPacks: Defs.SkeletonSearchStarterPack list }

        module Errors =
            [<Literal>]
            let BadQueryString = "BadQueryString"

module AppBskyVideo =
    module Defs =
        type JobStatus =
            { [<JsonPropertyName("blob")>]
              Blob: JsonElement option
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("error")>]
              Error: string option
              [<JsonPropertyName("jobId")>]
              JobId: string
              [<JsonPropertyName("message")>]
              Message: string option
              [<JsonPropertyName("progress")>]
              Progress: int64 option
              [<JsonPropertyName("state")>]
              State: string }

    module GetJobStatus =
        [<Literal>]
        let TypeId = "app.bsky.video.getJobStatus"

        type Params =
            { [<JsonPropertyName("jobId")>]
              JobId: string }

        type Output =
            { [<JsonPropertyName("jobStatus")>]
              JobStatus: Defs.JobStatus }

    module GetUploadLimits =
        [<Literal>]
        let TypeId = "app.bsky.video.getUploadLimits"

        type Output =
            { [<JsonPropertyName("canUpload")>]
              CanUpload: bool
              [<JsonPropertyName("error")>]
              Error: string option
              [<JsonPropertyName("message")>]
              Message: string option
              [<JsonPropertyName("remainingDailyBytes")>]
              RemainingDailyBytes: int64 option
              [<JsonPropertyName("remainingDailyVideos")>]
              RemainingDailyVideos: int64 option }

    module UploadVideo =
        [<Literal>]
        let TypeId = "app.bsky.video.uploadVideo"

        type Output =
            { [<JsonPropertyName("jobStatus")>]
              JobStatus: Defs.JobStatus }

module ChatBsky =
    begin end

module ChatBskyActor =
    module Declaration =
        [<Literal>]
        let TypeId = "chat.bsky.actor.declaration"

        /// A declaration of a Bluesky chat account.
        type Declaration =
            { [<JsonPropertyName("allowIncoming")>]
              AllowIncoming: string }

    module Defs =
        type ProfileViewBasic =
            { [<JsonPropertyName("associated")>]
              Associated: AppBskyActor.Defs.ProfileAssociated option
              [<JsonPropertyName("avatar")>]
              Avatar: string option
              [<JsonPropertyName("chatDisabled")>]
              ChatDisabled: bool option
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("displayName")>]
              DisplayName: string option
              [<JsonPropertyName("handle")>]
              Handle: string
              [<JsonPropertyName("labels")>]
              Labels: ComAtprotoLabel.Defs.Label list option
              [<JsonPropertyName("verification")>]
              Verification: AppBskyActor.Defs.VerificationState option
              [<JsonPropertyName("viewer")>]
              Viewer: AppBskyActor.Defs.ViewerState option }

    module DeleteAccount =
        [<Literal>]
        let TypeId = "chat.bsky.actor.deleteAccount"

    module ExportAccountData =
        [<Literal>]
        let TypeId = "chat.bsky.actor.exportAccountData"

module ChatBskyConvo =
    module AcceptConvo =
        [<Literal>]
        let TypeId = "chat.bsky.convo.acceptConvo"

        type Input =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string }

        type Output =
            { [<JsonPropertyName("rev")>]
              Rev: string option }

    module AddReaction =
        [<Literal>]
        let TypeId = "chat.bsky.convo.addReaction"

        type Input =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string
              [<JsonPropertyName("messageId")>]
              MessageId: string
              [<JsonPropertyName("value")>]
              Value: string }

        type Output =
            { [<JsonPropertyName("message")>]
              Message: Defs.MessageView }

        module Errors =
            [<Literal>]
            let ReactionMessageDeleted = "ReactionMessageDeleted"

            [<Literal>]
            let ReactionLimitReached = "ReactionLimitReached"

            [<Literal>]
            let ReactionInvalidValue = "ReactionInvalidValue"

    module Defs =
        type ConvoView =
            { [<JsonPropertyName("id")>]
              Id: string
              [<JsonPropertyName("lastMessage")>]
              LastMessage: JsonElement option
              [<JsonPropertyName("lastReaction")>]
              LastReaction: JsonElement option
              [<JsonPropertyName("members")>]
              Members: ChatBskyActor.Defs.ProfileViewBasic list
              [<JsonPropertyName("muted")>]
              Muted: bool
              [<JsonPropertyName("rev")>]
              Rev: string
              [<JsonPropertyName("status")>]
              Status: string option
              [<JsonPropertyName("unreadCount")>]
              UnreadCount: int64 }

        type DeletedMessageView =
            { [<JsonPropertyName("id")>]
              Id: string
              [<JsonPropertyName("rev")>]
              Rev: string
              [<JsonPropertyName("sender")>]
              Sender: Defs.MessageViewSender
              [<JsonPropertyName("sentAt")>]
              SentAt: string }

        type LogAcceptConvo =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string
              [<JsonPropertyName("rev")>]
              Rev: string }

        type LogAddReaction =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string
              [<JsonPropertyName("message")>]
              Message: JsonElement
              [<JsonPropertyName("reaction")>]
              Reaction: Defs.ReactionView
              [<JsonPropertyName("rev")>]
              Rev: string }

        type LogBeginConvo =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string
              [<JsonPropertyName("rev")>]
              Rev: string }

        type LogCreateMessage =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string
              [<JsonPropertyName("message")>]
              Message: JsonElement
              [<JsonPropertyName("rev")>]
              Rev: string }

        type LogDeleteMessage =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string
              [<JsonPropertyName("message")>]
              Message: JsonElement
              [<JsonPropertyName("rev")>]
              Rev: string }

        type LogLeaveConvo =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string
              [<JsonPropertyName("rev")>]
              Rev: string }

        type LogMuteConvo =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string
              [<JsonPropertyName("rev")>]
              Rev: string }

        type LogReadMessage =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string
              [<JsonPropertyName("message")>]
              Message: JsonElement
              [<JsonPropertyName("rev")>]
              Rev: string }

        type LogRemoveReaction =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string
              [<JsonPropertyName("message")>]
              Message: JsonElement
              [<JsonPropertyName("reaction")>]
              Reaction: Defs.ReactionView
              [<JsonPropertyName("rev")>]
              Rev: string }

        type LogUnmuteConvo =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string
              [<JsonPropertyName("rev")>]
              Rev: string }

        type MessageAndReactionView =
            { [<JsonPropertyName("message")>]
              Message: Defs.MessageView
              [<JsonPropertyName("reaction")>]
              Reaction: Defs.ReactionView }

        type MessageInput =
            { [<JsonPropertyName("embed")>]
              Embed: JsonElement option
              [<JsonPropertyName("facets")>]
              Facets: AppBskyRichtext.Facet.Facet list option
              [<JsonPropertyName("text")>]
              Text: string }

        type MessageRef =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("messageId")>]
              MessageId: string }

        type MessageView =
            { [<JsonPropertyName("embed")>]
              Embed: JsonElement option
              [<JsonPropertyName("facets")>]
              Facets: AppBskyRichtext.Facet.Facet list option
              [<JsonPropertyName("id")>]
              Id: string
              [<JsonPropertyName("reactions")>]
              Reactions: Defs.ReactionView list option
              [<JsonPropertyName("rev")>]
              Rev: string
              [<JsonPropertyName("sender")>]
              Sender: Defs.MessageViewSender
              [<JsonPropertyName("sentAt")>]
              SentAt: string
              [<JsonPropertyName("text")>]
              Text: string }

        type MessageViewSender =
            { [<JsonPropertyName("did")>]
              Did: string }

        type ReactionView =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("sender")>]
              Sender: Defs.ReactionViewSender
              [<JsonPropertyName("value")>]
              Value: string }

        type ReactionViewSender =
            { [<JsonPropertyName("did")>]
              Did: string }

    module DeleteMessageForSelf =
        [<Literal>]
        let TypeId = "chat.bsky.convo.deleteMessageForSelf"

        type Input =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string
              [<JsonPropertyName("messageId")>]
              MessageId: string }

    module GetConvo =
        [<Literal>]
        let TypeId = "chat.bsky.convo.getConvo"

        type Params =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string }

        type Output =
            { [<JsonPropertyName("convo")>]
              Convo: Defs.ConvoView }

    module GetConvoAvailability =
        [<Literal>]
        let TypeId = "chat.bsky.convo.getConvoAvailability"

        type Params =
            { [<JsonPropertyName("members")>]
              Members: string list }

        type Output =
            { [<JsonPropertyName("canChat")>]
              CanChat: bool
              [<JsonPropertyName("convo")>]
              Convo: Defs.ConvoView option }

    module GetConvoForMembers =
        [<Literal>]
        let TypeId = "chat.bsky.convo.getConvoForMembers"

        type Params =
            { [<JsonPropertyName("members")>]
              Members: string list }

        type Output =
            { [<JsonPropertyName("convo")>]
              Convo: Defs.ConvoView }

    module GetLog =
        [<Literal>]
        let TypeId = "chat.bsky.convo.getLog"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("logs")>]
              Logs: JsonElement list }

    module GetMessages =
        [<Literal>]
        let TypeId = "chat.bsky.convo.getMessages"

        type Params =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("messages")>]
              Messages: JsonElement list }

    module LeaveConvo =
        [<Literal>]
        let TypeId = "chat.bsky.convo.leaveConvo"

        type Input =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string }

        type Output =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string
              [<JsonPropertyName("rev")>]
              Rev: string }

    module ListConvos =
        [<Literal>]
        let TypeId = "chat.bsky.convo.listConvos"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("readState")>]
              ReadState: string option
              [<JsonPropertyName("status")>]
              Status: string option }

        type Output =
            { [<JsonPropertyName("convos")>]
              Convos: Defs.ConvoView list
              [<JsonPropertyName("cursor")>]
              Cursor: string option }

    module MuteConvo =
        [<Literal>]
        let TypeId = "chat.bsky.convo.muteConvo"

        type Input =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string }

        type Output =
            { [<JsonPropertyName("convo")>]
              Convo: Defs.ConvoView }

    module RemoveReaction =
        [<Literal>]
        let TypeId = "chat.bsky.convo.removeReaction"

        type Input =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string
              [<JsonPropertyName("messageId")>]
              MessageId: string
              [<JsonPropertyName("value")>]
              Value: string }

        type Output =
            { [<JsonPropertyName("message")>]
              Message: Defs.MessageView }

        module Errors =
            [<Literal>]
            let ReactionMessageDeleted = "ReactionMessageDeleted"

            [<Literal>]
            let ReactionInvalidValue = "ReactionInvalidValue"

    module SendMessage =
        [<Literal>]
        let TypeId = "chat.bsky.convo.sendMessage"

        type Input =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string
              [<JsonPropertyName("message")>]
              Message: Defs.MessageInput }

    module SendMessageBatch =
        [<Literal>]
        let TypeId = "chat.bsky.convo.sendMessageBatch"

        type Input =
            { [<JsonPropertyName("items")>]
              Items: SendMessageBatch.BatchItem list }

        type Output =
            { [<JsonPropertyName("items")>]
              Items: Defs.MessageView list }

        type BatchItem =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string
              [<JsonPropertyName("message")>]
              Message: Defs.MessageInput }

    module UnmuteConvo =
        [<Literal>]
        let TypeId = "chat.bsky.convo.unmuteConvo"

        type Input =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string }

        type Output =
            { [<JsonPropertyName("convo")>]
              Convo: Defs.ConvoView }

    module UpdateAllRead =
        [<Literal>]
        let TypeId = "chat.bsky.convo.updateAllRead"

        type Input =
            { [<JsonPropertyName("status")>]
              Status: string option }

        type Output =
            { [<JsonPropertyName("updatedCount")>]
              UpdatedCount: int64 }

    module UpdateRead =
        [<Literal>]
        let TypeId = "chat.bsky.convo.updateRead"

        type Input =
            { [<JsonPropertyName("convoId")>]
              ConvoId: string
              [<JsonPropertyName("messageId")>]
              MessageId: string option }

        type Output =
            { [<JsonPropertyName("convo")>]
              Convo: Defs.ConvoView }

module ChatBskyModeration =
    module GetActorMetadata =
        [<Literal>]
        let TypeId = "chat.bsky.moderation.getActorMetadata"

        type Params =
            { [<JsonPropertyName("actor")>]
              Actor: string }

        type Output =
            { [<JsonPropertyName("all")>]
              All: GetActorMetadata.Metadata
              [<JsonPropertyName("day")>]
              Day: GetActorMetadata.Metadata
              [<JsonPropertyName("month")>]
              Month: GetActorMetadata.Metadata }

        type Metadata =
            { [<JsonPropertyName("convos")>]
              Convos: int64
              [<JsonPropertyName("convosStarted")>]
              ConvosStarted: int64
              [<JsonPropertyName("messagesReceived")>]
              MessagesReceived: int64
              [<JsonPropertyName("messagesSent")>]
              MessagesSent: int64 }

    module GetMessageContext =
        [<Literal>]
        let TypeId = "chat.bsky.moderation.getMessageContext"

        type Params =
            { [<JsonPropertyName("after")>]
              After: int64 option
              [<JsonPropertyName("before")>]
              Before: int64 option
              [<JsonPropertyName("convoId")>]
              ConvoId: string option
              [<JsonPropertyName("messageId")>]
              MessageId: string }

        type Output =
            { [<JsonPropertyName("messages")>]
              Messages: JsonElement list }

    module UpdateActorAccess =
        [<Literal>]
        let TypeId = "chat.bsky.moderation.updateActorAccess"

        type Input =
            { [<JsonPropertyName("actor")>]
              Actor: string
              [<JsonPropertyName("allowAccess")>]
              AllowAccess: bool
              [<JsonPropertyName("ref")>]
              Ref: string option }

module ComAtprotoIdentity =
    module Defs =
        type IdentityInfo =
            { [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("didDoc")>]
              DidDoc: JsonElement
              [<JsonPropertyName("handle")>]
              Handle: string }

    module GetRecommendedDidCredentials =
        [<Literal>]
        let TypeId = "com.atproto.identity.getRecommendedDidCredentials"

        type Output =
            { [<JsonPropertyName("alsoKnownAs")>]
              AlsoKnownAs: string list option
              [<JsonPropertyName("rotationKeys")>]
              RotationKeys: string list option
              [<JsonPropertyName("services")>]
              Services: JsonElement option
              [<JsonPropertyName("verificationMethods")>]
              VerificationMethods: JsonElement option }

    module RefreshIdentity =
        [<Literal>]
        let TypeId = "com.atproto.identity.refreshIdentity"

        type Input =
            { [<JsonPropertyName("identifier")>]
              Identifier: string }

        module Errors =
            [<Literal>]
            let HandleNotFound = "HandleNotFound"

            [<Literal>]
            let DidNotFound = "DidNotFound"

            [<Literal>]
            let DidDeactivated = "DidDeactivated"

    module RequestPlcOperationSignature =
        [<Literal>]
        let TypeId = "com.atproto.identity.requestPlcOperationSignature"

    module ResolveDid =
        [<Literal>]
        let TypeId = "com.atproto.identity.resolveDid"

        type Params =
            { [<JsonPropertyName("did")>]
              Did: string }

        type Output =
            { [<JsonPropertyName("didDoc")>]
              DidDoc: JsonElement }

        module Errors =
            [<Literal>]
            let DidNotFound = "DidNotFound"

            [<Literal>]
            let DidDeactivated = "DidDeactivated"

    module ResolveHandle =
        [<Literal>]
        let TypeId = "com.atproto.identity.resolveHandle"

        type Params =
            { [<JsonPropertyName("handle")>]
              Handle: string }

        type Output =
            { [<JsonPropertyName("did")>]
              Did: string }

        module Errors =
            [<Literal>]
            let HandleNotFound = "HandleNotFound"

    module ResolveIdentity =
        [<Literal>]
        let TypeId = "com.atproto.identity.resolveIdentity"

        type Params =
            { [<JsonPropertyName("identifier")>]
              Identifier: string }

        module Errors =
            [<Literal>]
            let HandleNotFound = "HandleNotFound"

            [<Literal>]
            let DidNotFound = "DidNotFound"

            [<Literal>]
            let DidDeactivated = "DidDeactivated"

    module SignPlcOperation =
        [<Literal>]
        let TypeId = "com.atproto.identity.signPlcOperation"

        type Input =
            { [<JsonPropertyName("alsoKnownAs")>]
              AlsoKnownAs: string list option
              [<JsonPropertyName("rotationKeys")>]
              RotationKeys: string list option
              [<JsonPropertyName("services")>]
              Services: JsonElement option
              [<JsonPropertyName("token")>]
              Token: string option
              [<JsonPropertyName("verificationMethods")>]
              VerificationMethods: JsonElement option }

        type Output =
            { [<JsonPropertyName("operation")>]
              Operation: JsonElement }

    module SubmitPlcOperation =
        [<Literal>]
        let TypeId = "com.atproto.identity.submitPlcOperation"

        type Input =
            { [<JsonPropertyName("operation")>]
              Operation: JsonElement }

    module UpdateHandle =
        [<Literal>]
        let TypeId = "com.atproto.identity.updateHandle"

        type Input =
            { [<JsonPropertyName("handle")>]
              Handle: string }

module ComAtprotoLexicon =
    module ResolveLexicon =
        [<Literal>]
        let TypeId = "com.atproto.lexicon.resolveLexicon"

        type Params =
            { [<JsonPropertyName("nsid")>]
              Nsid: string }

        type Output =
            { [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("schema")>]
              Schema: Schema.Schema
              [<JsonPropertyName("uri")>]
              Uri: string }

        module Errors =
            [<Literal>]
            let LexiconNotFound = "LexiconNotFound"

    module Schema =
        [<Literal>]
        let TypeId = "com.atproto.lexicon.schema"

        /// Representation of Lexicon schemas themselves, when published as atproto records. Note that the schema language is not defined in Lexicon; this meta schema currently only includes a single version field ('lexicon'). See the atproto specifications for description of the other expected top-level fields ('id', 'defs', etc).
        type Schema =
            { [<JsonPropertyName("lexicon")>]
              Lexicon: int64 }

module ComAtprotoSync =
    module Defs =
        type HostStatus = string

    module GetBlob =
        [<Literal>]
        let TypeId = "com.atproto.sync.getBlob"

        type Params =
            { [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("did")>]
              Did: string }

        module Errors =
            [<Literal>]
            let BlobNotFound = "BlobNotFound"

            [<Literal>]
            let RepoNotFound = "RepoNotFound"

            [<Literal>]
            let RepoTakendown = "RepoTakendown"

            [<Literal>]
            let RepoSuspended = "RepoSuspended"

            [<Literal>]
            let RepoDeactivated = "RepoDeactivated"

    module GetBlocks =
        [<Literal>]
        let TypeId = "com.atproto.sync.getBlocks"

        type Params =
            { [<JsonPropertyName("cids")>]
              Cids: string list
              [<JsonPropertyName("did")>]
              Did: string }

        module Errors =
            [<Literal>]
            let BlockNotFound = "BlockNotFound"

            [<Literal>]
            let RepoNotFound = "RepoNotFound"

            [<Literal>]
            let RepoTakendown = "RepoTakendown"

            [<Literal>]
            let RepoSuspended = "RepoSuspended"

            [<Literal>]
            let RepoDeactivated = "RepoDeactivated"

    module GetCheckout =
        [<Literal>]
        let TypeId = "com.atproto.sync.getCheckout"

        type Params =
            { [<JsonPropertyName("did")>]
              Did: string }

    module GetHead =
        [<Literal>]
        let TypeId = "com.atproto.sync.getHead"

        type Params =
            { [<JsonPropertyName("did")>]
              Did: string }

        type Output =
            { [<JsonPropertyName("root")>]
              Root: string }

        module Errors =
            [<Literal>]
            let HeadNotFound = "HeadNotFound"

    module GetHostStatus =
        [<Literal>]
        let TypeId = "com.atproto.sync.getHostStatus"

        type Params =
            { [<JsonPropertyName("hostname")>]
              Hostname: string }

        type Output =
            { [<JsonPropertyName("accountCount")>]
              AccountCount: int64 option
              [<JsonPropertyName("hostname")>]
              Hostname: string
              [<JsonPropertyName("seq")>]
              Seq: int64 option
              [<JsonPropertyName("status")>]
              Status: Defs.HostStatus option }

        module Errors =
            [<Literal>]
            let HostNotFound = "HostNotFound"

    module GetLatestCommit =
        [<Literal>]
        let TypeId = "com.atproto.sync.getLatestCommit"

        type Params =
            { [<JsonPropertyName("did")>]
              Did: string }

        type Output =
            { [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("rev")>]
              Rev: string }

        module Errors =
            [<Literal>]
            let RepoNotFound = "RepoNotFound"

            [<Literal>]
            let RepoTakendown = "RepoTakendown"

            [<Literal>]
            let RepoSuspended = "RepoSuspended"

            [<Literal>]
            let RepoDeactivated = "RepoDeactivated"

    module GetRecord =
        [<Literal>]
        let TypeId = "com.atproto.sync.getRecord"

        type Params =
            { [<JsonPropertyName("collection")>]
              Collection: string
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("rkey")>]
              Rkey: string }

        module Errors =
            [<Literal>]
            let RecordNotFound = "RecordNotFound"

            [<Literal>]
            let RepoNotFound = "RepoNotFound"

            [<Literal>]
            let RepoTakendown = "RepoTakendown"

            [<Literal>]
            let RepoSuspended = "RepoSuspended"

            [<Literal>]
            let RepoDeactivated = "RepoDeactivated"

    module GetRepo =
        [<Literal>]
        let TypeId = "com.atproto.sync.getRepo"

        type Params =
            { [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("since")>]
              Since: string option }

        module Errors =
            [<Literal>]
            let RepoNotFound = "RepoNotFound"

            [<Literal>]
            let RepoTakendown = "RepoTakendown"

            [<Literal>]
            let RepoSuspended = "RepoSuspended"

            [<Literal>]
            let RepoDeactivated = "RepoDeactivated"

    module GetRepoStatus =
        [<Literal>]
        let TypeId = "com.atproto.sync.getRepoStatus"

        type Params =
            { [<JsonPropertyName("did")>]
              Did: string }

        type Output =
            { [<JsonPropertyName("active")>]
              Active: bool
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("rev")>]
              Rev: string option
              [<JsonPropertyName("status")>]
              Status: string option }

        module Errors =
            [<Literal>]
            let RepoNotFound = "RepoNotFound"

    module ListBlobs =
        [<Literal>]
        let TypeId = "com.atproto.sync.listBlobs"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("since")>]
              Since: string option }

        type Output =
            { [<JsonPropertyName("cids")>]
              Cids: string list
              [<JsonPropertyName("cursor")>]
              Cursor: string option }

        module Errors =
            [<Literal>]
            let RepoNotFound = "RepoNotFound"

            [<Literal>]
            let RepoTakendown = "RepoTakendown"

            [<Literal>]
            let RepoSuspended = "RepoSuspended"

            [<Literal>]
            let RepoDeactivated = "RepoDeactivated"

    module ListHosts =
        [<Literal>]
        let TypeId = "com.atproto.sync.listHosts"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("hosts")>]
              Hosts: ListHosts.Host list }

        type Host =
            { [<JsonPropertyName("accountCount")>]
              AccountCount: int64 option
              [<JsonPropertyName("hostname")>]
              Hostname: string
              [<JsonPropertyName("seq")>]
              Seq: int64 option
              [<JsonPropertyName("status")>]
              Status: Defs.HostStatus option }

    module ListRepos =
        [<Literal>]
        let TypeId = "com.atproto.sync.listRepos"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("repos")>]
              Repos: ListRepos.Repo list }

        type Repo =
            { [<JsonPropertyName("active")>]
              Active: bool option
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("head")>]
              Head: string
              [<JsonPropertyName("rev")>]
              Rev: string
              [<JsonPropertyName("status")>]
              Status: string option }

    module ListReposByCollection =
        [<Literal>]
        let TypeId = "com.atproto.sync.listReposByCollection"

        type Params =
            { [<JsonPropertyName("collection")>]
              Collection: string
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("repos")>]
              Repos: ListReposByCollection.Repo list }

        type Repo =
            { [<JsonPropertyName("did")>]
              Did: string }

    module NotifyOfUpdate =
        [<Literal>]
        let TypeId = "com.atproto.sync.notifyOfUpdate"

        type Input =
            { [<JsonPropertyName("hostname")>]
              Hostname: string }

    module RequestCrawl =
        [<Literal>]
        let TypeId = "com.atproto.sync.requestCrawl"

        type Input =
            { [<JsonPropertyName("hostname")>]
              Hostname: string }

        module Errors =
            [<Literal>]
            let HostBanned = "HostBanned"

    module SubscribeRepos =
        [<Literal>]
        let TypeId = "com.atproto.sync.subscribeRepos"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: int64 option }

        [<JsonFSharpConverter(JsonUnionEncoding.InternalTag ||| JsonUnionEncoding.UnwrapSingleFieldCases, unionTagName = "$type")>]
        type Message =
            | [<JsonName("com.atproto.sync.subscribeRepos#commit")>] Commit of SubscribeRepos.Commit
            | [<JsonName("com.atproto.sync.subscribeRepos#sync")>] Sync of SubscribeRepos.Sync
            | [<JsonName("com.atproto.sync.subscribeRepos#identity")>] Identity of SubscribeRepos.Identity
            | [<JsonName("com.atproto.sync.subscribeRepos#account")>] Account of SubscribeRepos.Account
            | [<JsonName("com.atproto.sync.subscribeRepos#info")>] Info of SubscribeRepos.Info
            | Unknown of string * System.Text.Json.JsonElement

        module Errors =
            [<Literal>]
            let FutureCursor = "FutureCursor"

            [<Literal>]
            let ConsumerTooSlow = "ConsumerTooSlow"

        /// Represents a change to an account's status on a host (eg, PDS or Relay). The semantics of this event are that the status is at the host which emitted the event, not necessarily that at the currently active PDS. Eg, a Relay takedown would emit a takedown with active=false, even if the PDS is still active.
        type Account =
            { [<JsonPropertyName("active")>]
              Active: bool
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("seq")>]
              Seq: int64
              [<JsonPropertyName("status")>]
              Status: string option
              [<JsonPropertyName("time")>]
              Time: string }

        /// Represents an update of repository state. Note that empty commits are allowed, which include no repo data changes, but an update to rev and signature.
        type Commit =
            { [<JsonPropertyName("blobs")>]
              Blobs: string list
              [<JsonPropertyName("blocks")>]
              Blocks: byte[]
              [<JsonPropertyName("commit")>]
              Commit: string
              [<JsonPropertyName("ops")>]
              Ops: SubscribeRepos.RepoOp list
              [<JsonPropertyName("prevData")>]
              PrevData: string option
              [<JsonPropertyName("rebase")>]
              Rebase: bool
              [<JsonPropertyName("repo")>]
              Repo: string
              [<JsonPropertyName("rev")>]
              Rev: string
              [<JsonPropertyName("seq")>]
              Seq: int64
              [<JsonPropertyName("since")>]
              Since: string option
              [<JsonPropertyName("time")>]
              Time: string
              [<JsonPropertyName("tooBig")>]
              TooBig: bool }

        /// Represents a change to an account's identity. Could be an updated handle, signing key, or pds hosting endpoint. Serves as a prod to all downstream services to refresh their identity cache.
        type Identity =
            { [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("handle")>]
              Handle: string option
              [<JsonPropertyName("seq")>]
              Seq: int64
              [<JsonPropertyName("time")>]
              Time: string }

        type Info =
            { [<JsonPropertyName("message")>]
              Message: string option
              [<JsonPropertyName("name")>]
              Name: string }

        /// A repo operation, ie a mutation of a single record.
        type RepoOp =
            { [<JsonPropertyName("action")>]
              Action: string
              [<JsonPropertyName("cid")>]
              Cid: string option
              [<JsonPropertyName("path")>]
              Path: string
              [<JsonPropertyName("prev")>]
              Prev: string option }

        /// Updates the repo to a new state, without necessarily including that state on the firehose. Used to recover from broken commit streams, data loss incidents, or in situations where upstream host does not know recent state of the repository.
        type Sync =
            { [<JsonPropertyName("blocks")>]
              Blocks: byte[]
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("rev")>]
              Rev: string
              [<JsonPropertyName("seq")>]
              Seq: int64
              [<JsonPropertyName("time")>]
              Time: string }

module ComAtprotoTemp =
    module AddReservedHandle =
        [<Literal>]
        let TypeId = "com.atproto.temp.addReservedHandle"

        type Input =
            { [<JsonPropertyName("handle")>]
              Handle: string }

    module CheckHandleAvailability =
        [<Literal>]
        let TypeId = "com.atproto.temp.checkHandleAvailability"

        type Params =
            { [<JsonPropertyName("birthDate")>]
              BirthDate: string option
              [<JsonPropertyName("email")>]
              Email: string option
              [<JsonPropertyName("handle")>]
              Handle: string }

        type Output =
            { [<JsonPropertyName("handle")>]
              Handle: string
              [<JsonPropertyName("result")>]
              Result: JsonElement }

        module Errors =
            [<Literal>]
            let InvalidEmail = "InvalidEmail"

        /// Indicates the provided handle is unavailable and gives suggestions of available handles.
        type ResultUnavailable =
            { [<JsonPropertyName("suggestions")>]
              Suggestions: CheckHandleAvailability.Suggestion list }

        type Suggestion =
            { [<JsonPropertyName("handle")>]
              Handle: string
              [<JsonPropertyName("method")>]
              Method: string }

    module CheckSignupQueue =
        [<Literal>]
        let TypeId = "com.atproto.temp.checkSignupQueue"

        type Output =
            { [<JsonPropertyName("activated")>]
              Activated: bool
              [<JsonPropertyName("estimatedTimeMs")>]
              EstimatedTimeMs: int64 option
              [<JsonPropertyName("placeInQueue")>]
              PlaceInQueue: int64 option }

    module DereferenceScope =
        [<Literal>]
        let TypeId = "com.atproto.temp.dereferenceScope"

        type Params =
            { [<JsonPropertyName("scope")>]
              Scope: string }

        type Output =
            { [<JsonPropertyName("scope")>]
              Scope: string }

        module Errors =
            [<Literal>]
            let InvalidScopeReference = "InvalidScopeReference"

    module FetchLabels =
        [<Literal>]
        let TypeId = "com.atproto.temp.fetchLabels"

        type Params =
            { [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("since")>]
              Since: int64 option }

        type Output =
            { [<JsonPropertyName("labels")>]
              Labels: ComAtprotoLabel.Defs.Label list }

    module RequestPhoneVerification =
        [<Literal>]
        let TypeId = "com.atproto.temp.requestPhoneVerification"

        type Input =
            { [<JsonPropertyName("phoneNumber")>]
              PhoneNumber: string }

    module RevokeAccountCredentials =
        [<Literal>]
        let TypeId = "com.atproto.temp.revokeAccountCredentials"

        type Input =
            { [<JsonPropertyName("account")>]
              Account: string }

module ComGermnetwork =
    module Declaration =
        [<Literal>]
        let TypeId = "com.germnetwork.declaration"

        /// A declaration of a Germ Network account
        type Declaration =
            { [<JsonPropertyName("continuityProofs")>]
              ContinuityProofs: byte[] list option
              [<JsonPropertyName("currentKey")>]
              CurrentKey: byte[]
              [<JsonPropertyName("keyPackage")>]
              KeyPackage: byte[] option
              [<JsonPropertyName("messageMe")>]
              MessageMe: Declaration.MessageMe option
              [<JsonPropertyName("version")>]
              Version: string }

        type MessageMe =
            { [<JsonPropertyName("messageMeUrl")>]
              MessageMeUrl: string
              [<JsonPropertyName("showButtonTo")>]
              ShowButtonTo: string }

module ToolsOzoneCommunication =
    module CreateTemplate =
        [<Literal>]
        let TypeId = "tools.ozone.communication.createTemplate"

        type Input =
            { [<JsonPropertyName("contentMarkdown")>]
              ContentMarkdown: string
              [<JsonPropertyName("createdBy")>]
              CreatedBy: string option
              [<JsonPropertyName("lang")>]
              Lang: string option
              [<JsonPropertyName("name")>]
              Name: string
              [<JsonPropertyName("subject")>]
              Subject: string }

        module Errors =
            [<Literal>]
            let DuplicateTemplateName = "DuplicateTemplateName"

    module Defs =
        type TemplateView =
            { [<JsonPropertyName("contentMarkdown")>]
              ContentMarkdown: string
              [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("disabled")>]
              Disabled: bool
              [<JsonPropertyName("id")>]
              Id: string
              [<JsonPropertyName("lang")>]
              Lang: string option
              [<JsonPropertyName("lastUpdatedBy")>]
              LastUpdatedBy: string
              [<JsonPropertyName("name")>]
              Name: string
              [<JsonPropertyName("subject")>]
              Subject: string option
              [<JsonPropertyName("updatedAt")>]
              UpdatedAt: string }

    module DeleteTemplate =
        [<Literal>]
        let TypeId = "tools.ozone.communication.deleteTemplate"

        type Input =
            { [<JsonPropertyName("id")>]
              Id: string }

    module ListTemplates =
        [<Literal>]
        let TypeId = "tools.ozone.communication.listTemplates"

        type Output =
            { [<JsonPropertyName("communicationTemplates")>]
              CommunicationTemplates: Defs.TemplateView list }

    module UpdateTemplate =
        [<Literal>]
        let TypeId = "tools.ozone.communication.updateTemplate"

        type Input =
            { [<JsonPropertyName("contentMarkdown")>]
              ContentMarkdown: string option
              [<JsonPropertyName("disabled")>]
              Disabled: bool option
              [<JsonPropertyName("id")>]
              Id: string
              [<JsonPropertyName("lang")>]
              Lang: string option
              [<JsonPropertyName("name")>]
              Name: string option
              [<JsonPropertyName("subject")>]
              Subject: string option
              [<JsonPropertyName("updatedBy")>]
              UpdatedBy: string option }

        module Errors =
            [<Literal>]
            let DuplicateTemplateName = "DuplicateTemplateName"

module ToolsOzoneHosting =
    module GetAccountHistory =
        [<Literal>]
        let TypeId = "tools.ozone.hosting.getAccountHistory"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("events")>]
              Events: string list option
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("events")>]
              Events: GetAccountHistory.Event list }

        type AccountCreated =
            { [<JsonPropertyName("email")>]
              Email: string option
              [<JsonPropertyName("handle")>]
              Handle: string option }

        type EmailConfirmed =
            { [<JsonPropertyName("email")>]
              Email: string }

        type EmailUpdated =
            { [<JsonPropertyName("email")>]
              Email: string }

        type Event =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("createdBy")>]
              CreatedBy: string
              [<JsonPropertyName("details")>]
              Details: JsonElement }

        type HandleUpdated =
            { [<JsonPropertyName("handle")>]
              Handle: string }

module ToolsOzoneModeration =
    module CancelScheduledActions =
        [<Literal>]
        let TypeId = "tools.ozone.moderation.cancelScheduledActions"

        type Input =
            { [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("subjects")>]
              Subjects: string list }

        type CancellationResults =
            { [<JsonPropertyName("failed")>]
              Failed: CancelScheduledActions.FailedCancellation list
              [<JsonPropertyName("succeeded")>]
              Succeeded: string list }

        type FailedCancellation =
            { [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("error")>]
              Error: string
              [<JsonPropertyName("errorCode")>]
              ErrorCode: string option }

    module Defs =
        /// Logs account status related events on a repo subject. Normally captured by automod from the firehose and emitted to ozone for historical tracking.
        type AccountEvent =
            { [<JsonPropertyName("active")>]
              Active: bool
              [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("status")>]
              Status: string option
              [<JsonPropertyName("timestamp")>]
              Timestamp: string }

        type AccountHosting =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string option
              [<JsonPropertyName("deactivatedAt")>]
              DeactivatedAt: string option
              [<JsonPropertyName("deletedAt")>]
              DeletedAt: string option
              [<JsonPropertyName("reactivatedAt")>]
              ReactivatedAt: string option
              [<JsonPropertyName("status")>]
              Status: string
              [<JsonPropertyName("updatedAt")>]
              UpdatedAt: string option }

        /// Statistics about a particular account subject
        type AccountStats =
            { [<JsonPropertyName("appealCount")>]
              AppealCount: int64 option
              [<JsonPropertyName("escalateCount")>]
              EscalateCount: int64 option
              [<JsonPropertyName("reportCount")>]
              ReportCount: int64 option
              [<JsonPropertyName("suspendCount")>]
              SuspendCount: int64 option
              [<JsonPropertyName("takedownCount")>]
              TakedownCount: int64 option }

        /// Strike information for an account
        type AccountStrike =
            { [<JsonPropertyName("activeStrikeCount")>]
              ActiveStrikeCount: int64 option
              [<JsonPropertyName("firstStrikeAt")>]
              FirstStrikeAt: string option
              [<JsonPropertyName("lastStrikeAt")>]
              LastStrikeAt: string option
              [<JsonPropertyName("totalStrikeCount")>]
              TotalStrikeCount: int64 option }

        /// Age assurance info coming directly from users. Only works on DID subjects.
        type AgeAssuranceEvent =
            { [<JsonPropertyName("access")>]
              Access: AppBskyAgeassurance.Defs.Access option
              [<JsonPropertyName("attemptId")>]
              AttemptId: string
              [<JsonPropertyName("completeIp")>]
              CompleteIp: string option
              [<JsonPropertyName("completeUa")>]
              CompleteUa: string option
              [<JsonPropertyName("countryCode")>]
              CountryCode: string option
              [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("initIp")>]
              InitIp: string option
              [<JsonPropertyName("initUa")>]
              InitUa: string option
              [<JsonPropertyName("regionCode")>]
              RegionCode: string option
              [<JsonPropertyName("status")>]
              Status: string }

        /// Age assurance status override by moderators. Only works on DID subjects.
        type AgeAssuranceOverrideEvent =
            { [<JsonPropertyName("access")>]
              Access: AppBskyAgeassurance.Defs.Access option
              [<JsonPropertyName("comment")>]
              Comment: string
              [<JsonPropertyName("status")>]
              Status: string }

        type BlobView =
            { [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("details")>]
              Details: JsonElement option
              [<JsonPropertyName("mimeType")>]
              MimeType: string
              [<JsonPropertyName("moderation")>]
              Moderation: Defs.Moderation option
              [<JsonPropertyName("size")>]
              Size: int64 }

        /// Logs cancellation of a scheduled takedown action for an account.
        type CancelScheduledTakedownEvent =
            { [<JsonPropertyName("comment")>]
              Comment: string option }

        /// Logs identity related events on a repo subject. Normally captured by automod from the firehose and emitted to ozone for historical tracking.
        type IdentityEvent =
            { [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("handle")>]
              Handle: string option
              [<JsonPropertyName("pdsHost")>]
              PdsHost: string option
              [<JsonPropertyName("timestamp")>]
              Timestamp: string
              [<JsonPropertyName("tombstone")>]
              Tombstone: bool option }

        type ImageDetails =
            { [<JsonPropertyName("height")>]
              Height: int64
              [<JsonPropertyName("width")>]
              Width: int64 }

        type ModEventAcknowledge =
            { [<JsonPropertyName("acknowledgeAccountSubjects")>]
              AcknowledgeAccountSubjects: bool option
              [<JsonPropertyName("comment")>]
              Comment: string option }

        /// Add a comment to a subject. An empty comment will clear any previously set sticky comment.
        type ModEventComment =
            { [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("sticky")>]
              Sticky: bool option }

        /// Divert a record's blobs to a 3rd party service for further scanning/tagging
        type ModEventDivert =
            { [<JsonPropertyName("comment")>]
              Comment: string option }

        /// Keep a log of outgoing email to a user
        type ModEventEmail =
            { [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("content")>]
              Content: string option
              [<JsonPropertyName("isDelivered")>]
              IsDelivered: bool option
              [<JsonPropertyName("policies")>]
              Policies: string list option
              [<JsonPropertyName("severityLevel")>]
              SeverityLevel: string option
              [<JsonPropertyName("strikeCount")>]
              StrikeCount: int64 option
              [<JsonPropertyName("strikeExpiresAt")>]
              StrikeExpiresAt: string option
              [<JsonPropertyName("subjectLine")>]
              SubjectLine: string }

        type ModEventEscalate =
            { [<JsonPropertyName("comment")>]
              Comment: string option }

        /// Apply/Negate labels on a subject
        type ModEventLabel =
            { [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("createLabelVals")>]
              CreateLabelVals: string list
              [<JsonPropertyName("durationInHours")>]
              DurationInHours: int64 option
              [<JsonPropertyName("negateLabelVals")>]
              NegateLabelVals: string list }

        /// Mute incoming reports on a subject
        type ModEventMute =
            { [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("durationInHours")>]
              DurationInHours: int64 }

        /// Mute incoming reports from an account
        type ModEventMuteReporter =
            { [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("durationInHours")>]
              DurationInHours: int64 option }

        /// Set priority score of the subject. Higher score means higher priority.
        type ModEventPriorityScore =
            { [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("score")>]
              Score: int64 }

        /// Report a subject
        type ModEventReport =
            { [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("isReporterMuted")>]
              IsReporterMuted: bool option
              [<JsonPropertyName("reportType")>]
              ReportType: ComAtprotoModeration.Defs.ReasonType }

        /// Resolve appeal on a subject
        type ModEventResolveAppeal =
            { [<JsonPropertyName("comment")>]
              Comment: string option }

        /// Revert take down action on a subject
        type ModEventReverseTakedown =
            { [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("policies")>]
              Policies: string list option
              [<JsonPropertyName("severityLevel")>]
              SeverityLevel: string option
              [<JsonPropertyName("strikeCount")>]
              StrikeCount: int64 option }

        /// Add/Remove a tag on a subject
        type ModEventTag =
            { [<JsonPropertyName("add")>]
              Add: string list
              [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("remove")>]
              Remove: string list }

        /// Take down a subject permanently or temporarily
        type ModEventTakedown =
            { [<JsonPropertyName("acknowledgeAccountSubjects")>]
              AcknowledgeAccountSubjects: bool option
              [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("durationInHours")>]
              DurationInHours: int64 option
              [<JsonPropertyName("policies")>]
              Policies: string list option
              [<JsonPropertyName("severityLevel")>]
              SeverityLevel: string option
              [<JsonPropertyName("strikeCount")>]
              StrikeCount: int64 option
              [<JsonPropertyName("strikeExpiresAt")>]
              StrikeExpiresAt: string option
              [<JsonPropertyName("targetServices")>]
              TargetServices: string list option }

        /// Unmute action on a subject
        type ModEventUnmute =
            { [<JsonPropertyName("comment")>]
              Comment: string option }

        /// Unmute incoming reports from an account
        type ModEventUnmuteReporter =
            { [<JsonPropertyName("comment")>]
              Comment: string option }

        type ModEventView =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("createdBy")>]
              CreatedBy: string
              [<JsonPropertyName("creatorHandle")>]
              CreatorHandle: string option
              [<JsonPropertyName("event")>]
              Event: JsonElement
              [<JsonPropertyName("id")>]
              Id: int64
              [<JsonPropertyName("modTool")>]
              ModTool: Defs.ModTool option
              [<JsonPropertyName("subject")>]
              Subject: JsonElement
              [<JsonPropertyName("subjectBlobCids")>]
              SubjectBlobCids: string list
              [<JsonPropertyName("subjectHandle")>]
              SubjectHandle: string option }

        type ModEventViewDetail =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("createdBy")>]
              CreatedBy: string
              [<JsonPropertyName("event")>]
              Event: JsonElement
              [<JsonPropertyName("id")>]
              Id: int64
              [<JsonPropertyName("modTool")>]
              ModTool: Defs.ModTool option
              [<JsonPropertyName("subject")>]
              Subject: JsonElement
              [<JsonPropertyName("subjectBlobs")>]
              SubjectBlobs: Defs.BlobView list }

        /// Moderation tool information for tracing the source of the action
        type ModTool =
            { [<JsonPropertyName("meta")>]
              Meta: JsonElement option
              [<JsonPropertyName("name")>]
              Name: string }

        type Moderation =
            { [<JsonPropertyName("subjectStatus")>]
              SubjectStatus: Defs.SubjectStatusView option }

        type ModerationDetail =
            { [<JsonPropertyName("subjectStatus")>]
              SubjectStatus: Defs.SubjectStatusView option }

        /// Logs lifecycle event on a record subject. Normally captured by automod from the firehose and emitted to ozone for historical tracking.
        type RecordEvent =
            { [<JsonPropertyName("cid")>]
              Cid: string option
              [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("op")>]
              Op: string
              [<JsonPropertyName("timestamp")>]
              Timestamp: string }

        type RecordHosting =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string option
              [<JsonPropertyName("deletedAt")>]
              DeletedAt: string option
              [<JsonPropertyName("status")>]
              Status: string
              [<JsonPropertyName("updatedAt")>]
              UpdatedAt: string option }

        type RecordView =
            { [<JsonPropertyName("blobCids")>]
              BlobCids: string list
              [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("indexedAt")>]
              IndexedAt: string
              [<JsonPropertyName("moderation")>]
              Moderation: Defs.Moderation
              [<JsonPropertyName("repo")>]
              Repo: Defs.RepoView
              [<JsonPropertyName("uri")>]
              Uri: string
              [<JsonPropertyName("value")>]
              Value: JsonElement }

        type RecordViewDetail =
            { [<JsonPropertyName("blobs")>]
              Blobs: Defs.BlobView list
              [<JsonPropertyName("cid")>]
              Cid: string
              [<JsonPropertyName("indexedAt")>]
              IndexedAt: string
              [<JsonPropertyName("labels")>]
              Labels: ComAtprotoLabel.Defs.Label list option
              [<JsonPropertyName("moderation")>]
              Moderation: Defs.ModerationDetail
              [<JsonPropertyName("repo")>]
              Repo: Defs.RepoView
              [<JsonPropertyName("uri")>]
              Uri: string
              [<JsonPropertyName("value")>]
              Value: JsonElement }

        type RecordViewNotFound =
            { [<JsonPropertyName("uri")>]
              Uri: string }

        /// Statistics about a set of record subject items
        type RecordsStats =
            { [<JsonPropertyName("appealedCount")>]
              AppealedCount: int64 option
              [<JsonPropertyName("escalatedCount")>]
              EscalatedCount: int64 option
              [<JsonPropertyName("pendingCount")>]
              PendingCount: int64 option
              [<JsonPropertyName("processedCount")>]
              ProcessedCount: int64 option
              [<JsonPropertyName("reportedCount")>]
              ReportedCount: int64 option
              [<JsonPropertyName("subjectCount")>]
              SubjectCount: int64 option
              [<JsonPropertyName("takendownCount")>]
              TakendownCount: int64 option
              [<JsonPropertyName("totalReports")>]
              TotalReports: int64 option }

        type RepoView =
            { [<JsonPropertyName("deactivatedAt")>]
              DeactivatedAt: string option
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("email")>]
              Email: string option
              [<JsonPropertyName("handle")>]
              Handle: string
              [<JsonPropertyName("indexedAt")>]
              IndexedAt: string
              [<JsonPropertyName("inviteNote")>]
              InviteNote: string option
              [<JsonPropertyName("invitedBy")>]
              InvitedBy: ComAtprotoServer.Defs.InviteCode option
              [<JsonPropertyName("invitesDisabled")>]
              InvitesDisabled: bool option
              [<JsonPropertyName("moderation")>]
              Moderation: Defs.Moderation
              [<JsonPropertyName("relatedRecords")>]
              RelatedRecords: JsonElement list
              [<JsonPropertyName("threatSignatures")>]
              ThreatSignatures: ComAtprotoAdmin.Defs.ThreatSignature list option }

        type RepoViewDetail =
            { [<JsonPropertyName("deactivatedAt")>]
              DeactivatedAt: string option
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("email")>]
              Email: string option
              [<JsonPropertyName("emailConfirmedAt")>]
              EmailConfirmedAt: string option
              [<JsonPropertyName("handle")>]
              Handle: string
              [<JsonPropertyName("indexedAt")>]
              IndexedAt: string
              [<JsonPropertyName("inviteNote")>]
              InviteNote: string option
              [<JsonPropertyName("invitedBy")>]
              InvitedBy: ComAtprotoServer.Defs.InviteCode option
              [<JsonPropertyName("invites")>]
              Invites: ComAtprotoServer.Defs.InviteCode list option
              [<JsonPropertyName("invitesDisabled")>]
              InvitesDisabled: bool option
              [<JsonPropertyName("labels")>]
              Labels: ComAtprotoLabel.Defs.Label list option
              [<JsonPropertyName("moderation")>]
              Moderation: Defs.ModerationDetail
              [<JsonPropertyName("relatedRecords")>]
              RelatedRecords: JsonElement list
              [<JsonPropertyName("threatSignatures")>]
              ThreatSignatures: ComAtprotoAdmin.Defs.ThreatSignature list option }

        type RepoViewNotFound =
            { [<JsonPropertyName("did")>]
              Did: string }

        type ReporterStats =
            { [<JsonPropertyName("accountReportCount")>]
              AccountReportCount: int64
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("labeledAccountCount")>]
              LabeledAccountCount: int64
              [<JsonPropertyName("labeledRecordCount")>]
              LabeledRecordCount: int64
              [<JsonPropertyName("recordReportCount")>]
              RecordReportCount: int64
              [<JsonPropertyName("reportedAccountCount")>]
              ReportedAccountCount: int64
              [<JsonPropertyName("reportedRecordCount")>]
              ReportedRecordCount: int64
              [<JsonPropertyName("takendownAccountCount")>]
              TakendownAccountCount: int64
              [<JsonPropertyName("takendownRecordCount")>]
              TakendownRecordCount: int64 }

        [<Literal>]
        let ReviewClosed = "tools.ozone.moderation.defs#reviewClosed"

        [<Literal>]
        let ReviewEscalated = "tools.ozone.moderation.defs#reviewEscalated"

        [<Literal>]
        let ReviewNone = "tools.ozone.moderation.defs#reviewNone"

        [<Literal>]
        let ReviewOpen = "tools.ozone.moderation.defs#reviewOpen"

        /// Account credentials revocation by moderators. Only works on DID subjects.
        type RevokeAccountCredentialsEvent =
            { [<JsonPropertyName("comment")>]
              Comment: string }

        /// Logs a scheduled takedown action for an account.
        type ScheduleTakedownEvent =
            { [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("executeAfter")>]
              ExecuteAfter: string option
              [<JsonPropertyName("executeAt")>]
              ExecuteAt: string option
              [<JsonPropertyName("executeUntil")>]
              ExecuteUntil: string option }

        /// View of a scheduled moderation action
        type ScheduledActionView =
            { [<JsonPropertyName("action")>]
              Action: string
              [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("createdBy")>]
              CreatedBy: string
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("eventData")>]
              EventData: JsonElement option
              [<JsonPropertyName("executeAfter")>]
              ExecuteAfter: string option
              [<JsonPropertyName("executeAt")>]
              ExecuteAt: string option
              [<JsonPropertyName("executeUntil")>]
              ExecuteUntil: string option
              [<JsonPropertyName("executionEventId")>]
              ExecutionEventId: int64 option
              [<JsonPropertyName("id")>]
              Id: int64
              [<JsonPropertyName("lastExecutedAt")>]
              LastExecutedAt: string option
              [<JsonPropertyName("lastFailureReason")>]
              LastFailureReason: string option
              [<JsonPropertyName("randomizeExecution")>]
              RandomizeExecution: bool option
              [<JsonPropertyName("status")>]
              Status: string
              [<JsonPropertyName("updatedAt")>]
              UpdatedAt: string option }

        type SubjectReviewState = string

        type SubjectStatusView =
            { [<JsonPropertyName("accountStats")>]
              AccountStats: Defs.AccountStats option
              [<JsonPropertyName("accountStrike")>]
              AccountStrike: Defs.AccountStrike option
              [<JsonPropertyName("ageAssuranceState")>]
              AgeAssuranceState: string option
              [<JsonPropertyName("ageAssuranceUpdatedBy")>]
              AgeAssuranceUpdatedBy: string option
              [<JsonPropertyName("appealed")>]
              Appealed: bool option
              [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("hosting")>]
              Hosting: JsonElement option
              [<JsonPropertyName("id")>]
              Id: int64
              [<JsonPropertyName("lastAppealedAt")>]
              LastAppealedAt: string option
              [<JsonPropertyName("lastReportedAt")>]
              LastReportedAt: string option
              [<JsonPropertyName("lastReviewedAt")>]
              LastReviewedAt: string option
              [<JsonPropertyName("lastReviewedBy")>]
              LastReviewedBy: string option
              [<JsonPropertyName("muteReportingUntil")>]
              MuteReportingUntil: string option
              [<JsonPropertyName("muteUntil")>]
              MuteUntil: string option
              [<JsonPropertyName("priorityScore")>]
              PriorityScore: int64 option
              [<JsonPropertyName("recordsStats")>]
              RecordsStats: Defs.RecordsStats option
              [<JsonPropertyName("reviewState")>]
              ReviewState: Defs.SubjectReviewState
              [<JsonPropertyName("subject")>]
              Subject: JsonElement
              [<JsonPropertyName("subjectBlobCids")>]
              SubjectBlobCids: string list option
              [<JsonPropertyName("subjectRepoHandle")>]
              SubjectRepoHandle: string option
              [<JsonPropertyName("suspendUntil")>]
              SuspendUntil: string option
              [<JsonPropertyName("tags")>]
              Tags: string list option
              [<JsonPropertyName("takendown")>]
              Takendown: bool option
              [<JsonPropertyName("updatedAt")>]
              UpdatedAt: string }

        /// Detailed view of a subject. For record subjects, the author's repo and profile will be returned.
        type SubjectView =
            { [<JsonPropertyName("profile")>]
              Profile: JsonElement option
              [<JsonPropertyName("record")>]
              Record: Defs.RecordViewDetail option
              [<JsonPropertyName("repo")>]
              Repo: Defs.RepoViewDetail option
              [<JsonPropertyName("status")>]
              Status: Defs.SubjectStatusView option
              [<JsonPropertyName("subject")>]
              Subject: string
              [<JsonPropertyName("type")>]
              Type: ComAtprotoModeration.Defs.SubjectType }

        [<Literal>]
        let TimelineEventPlcCreate = "tools.ozone.moderation.defs#timelineEventPlcCreate"

        [<Literal>]
        let TimelineEventPlcOperation =
            "tools.ozone.moderation.defs#timelineEventPlcOperation"

        [<Literal>]
        let TimelineEventPlcTombstone =
            "tools.ozone.moderation.defs#timelineEventPlcTombstone"

        type VideoDetails =
            { [<JsonPropertyName("height")>]
              Height: int64
              [<JsonPropertyName("length")>]
              Length: int64
              [<JsonPropertyName("width")>]
              Width: int64 }

    module EmitEvent =
        [<Literal>]
        let TypeId = "tools.ozone.moderation.emitEvent"

        type Input =
            { [<JsonPropertyName("createdBy")>]
              CreatedBy: string
              [<JsonPropertyName("event")>]
              Event: JsonElement
              [<JsonPropertyName("externalId")>]
              ExternalId: string option
              [<JsonPropertyName("modTool")>]
              ModTool: Defs.ModTool option
              [<JsonPropertyName("subject")>]
              Subject: JsonElement
              [<JsonPropertyName("subjectBlobCids")>]
              SubjectBlobCids: string list option }

        module Errors =
            [<Literal>]
            let SubjectHasAction = "SubjectHasAction"

            [<Literal>]
            let DuplicateExternalId = "DuplicateExternalId"

    module GetAccountTimeline =
        [<Literal>]
        let TypeId = "tools.ozone.moderation.getAccountTimeline"

        type Params =
            { [<JsonPropertyName("did")>]
              Did: string }

        type Output =
            { [<JsonPropertyName("timeline")>]
              Timeline: GetAccountTimeline.TimelineItem list }

        module Errors =
            [<Literal>]
            let RepoNotFound = "RepoNotFound"

        type TimelineItem =
            { [<JsonPropertyName("day")>]
              Day: string
              [<JsonPropertyName("summary")>]
              Summary: GetAccountTimeline.TimelineItemSummary list }

        type TimelineItemSummary =
            { [<JsonPropertyName("count")>]
              Count: int64
              [<JsonPropertyName("eventSubjectType")>]
              EventSubjectType: string
              [<JsonPropertyName("eventType")>]
              EventType: string }

    module GetEvent =
        [<Literal>]
        let TypeId = "tools.ozone.moderation.getEvent"

        type Params =
            { [<JsonPropertyName("id")>]
              Id: int64 }

    module GetRecord =
        [<Literal>]
        let TypeId = "tools.ozone.moderation.getRecord"

        type Params =
            { [<JsonPropertyName("cid")>]
              Cid: string option
              [<JsonPropertyName("uri")>]
              Uri: string }

        module Errors =
            [<Literal>]
            let RecordNotFound = "RecordNotFound"

    module GetRecords =
        [<Literal>]
        let TypeId = "tools.ozone.moderation.getRecords"

        type Params =
            { [<JsonPropertyName("uris")>]
              Uris: string list }

        type Output =
            { [<JsonPropertyName("records")>]
              Records: JsonElement list }

    module GetRepo =
        [<Literal>]
        let TypeId = "tools.ozone.moderation.getRepo"

        type Params =
            { [<JsonPropertyName("did")>]
              Did: string }

        module Errors =
            [<Literal>]
            let RepoNotFound = "RepoNotFound"

    module GetReporterStats =
        [<Literal>]
        let TypeId = "tools.ozone.moderation.getReporterStats"

        type Params =
            { [<JsonPropertyName("dids")>]
              Dids: string list }

        type Output =
            { [<JsonPropertyName("stats")>]
              Stats: Defs.ReporterStats list }

    module GetRepos =
        [<Literal>]
        let TypeId = "tools.ozone.moderation.getRepos"

        type Params =
            { [<JsonPropertyName("dids")>]
              Dids: string list }

        type Output =
            { [<JsonPropertyName("repos")>]
              Repos: JsonElement list }

    module GetSubjects =
        [<Literal>]
        let TypeId = "tools.ozone.moderation.getSubjects"

        type Params =
            { [<JsonPropertyName("subjects")>]
              Subjects: string list }

        type Output =
            { [<JsonPropertyName("subjects")>]
              Subjects: Defs.SubjectView list }

    module ListScheduledActions =
        [<Literal>]
        let TypeId = "tools.ozone.moderation.listScheduledActions"

        type Input =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("endsBefore")>]
              EndsBefore: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("startsAfter")>]
              StartsAfter: string option
              [<JsonPropertyName("statuses")>]
              Statuses: string list
              [<JsonPropertyName("subjects")>]
              Subjects: string list option }

        type Output =
            { [<JsonPropertyName("actions")>]
              Actions: Defs.ScheduledActionView list
              [<JsonPropertyName("cursor")>]
              Cursor: string option }

    module QueryEvents =
        [<Literal>]
        let TypeId = "tools.ozone.moderation.queryEvents"

        type Params =
            { [<JsonPropertyName("addedLabels")>]
              AddedLabels: string list option
              [<JsonPropertyName("addedTags")>]
              AddedTags: string list option
              [<JsonPropertyName("ageAssuranceState")>]
              AgeAssuranceState: string option
              [<JsonPropertyName("batchId")>]
              BatchId: string option
              [<JsonPropertyName("collections")>]
              Collections: string list option
              [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("createdAfter")>]
              CreatedAfter: string option
              [<JsonPropertyName("createdBefore")>]
              CreatedBefore: string option
              [<JsonPropertyName("createdBy")>]
              CreatedBy: string option
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("hasComment")>]
              HasComment: bool option
              [<JsonPropertyName("includeAllUserRecords")>]
              IncludeAllUserRecords: bool option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("modTool")>]
              ModTool: string list option
              [<JsonPropertyName("policies")>]
              Policies: string list option
              [<JsonPropertyName("removedLabels")>]
              RemovedLabels: string list option
              [<JsonPropertyName("removedTags")>]
              RemovedTags: string list option
              [<JsonPropertyName("reportTypes")>]
              ReportTypes: string list option
              [<JsonPropertyName("sortDirection")>]
              SortDirection: string option
              [<JsonPropertyName("subject")>]
              Subject: string option
              [<JsonPropertyName("subjectType")>]
              SubjectType: string option
              [<JsonPropertyName("types")>]
              Types: string list option
              [<JsonPropertyName("withStrike")>]
              WithStrike: bool option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("events")>]
              Events: Defs.ModEventView list }

    module QueryStatuses =
        [<Literal>]
        let TypeId = "tools.ozone.moderation.queryStatuses"

        type Params =
            { [<JsonPropertyName("ageAssuranceState")>]
              AgeAssuranceState: string option
              [<JsonPropertyName("appealed")>]
              Appealed: bool option
              [<JsonPropertyName("collections")>]
              Collections: string list option
              [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("excludeTags")>]
              ExcludeTags: string list option
              [<JsonPropertyName("hostingDeletedAfter")>]
              HostingDeletedAfter: string option
              [<JsonPropertyName("hostingDeletedBefore")>]
              HostingDeletedBefore: string option
              [<JsonPropertyName("hostingStatuses")>]
              HostingStatuses: string list option
              [<JsonPropertyName("hostingUpdatedAfter")>]
              HostingUpdatedAfter: string option
              [<JsonPropertyName("hostingUpdatedBefore")>]
              HostingUpdatedBefore: string option
              [<JsonPropertyName("ignoreSubjects")>]
              IgnoreSubjects: string list option
              [<JsonPropertyName("includeAllUserRecords")>]
              IncludeAllUserRecords: bool option
              [<JsonPropertyName("includeMuted")>]
              IncludeMuted: bool option
              [<JsonPropertyName("lastReviewedBy")>]
              LastReviewedBy: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("minAccountSuspendCount")>]
              MinAccountSuspendCount: int64 option
              [<JsonPropertyName("minPriorityScore")>]
              MinPriorityScore: int64 option
              [<JsonPropertyName("minReportedRecordsCount")>]
              MinReportedRecordsCount: int64 option
              [<JsonPropertyName("minStrikeCount")>]
              MinStrikeCount: int64 option
              [<JsonPropertyName("minTakendownRecordsCount")>]
              MinTakendownRecordsCount: int64 option
              [<JsonPropertyName("onlyMuted")>]
              OnlyMuted: bool option
              [<JsonPropertyName("queueCount")>]
              QueueCount: int64 option
              [<JsonPropertyName("queueIndex")>]
              QueueIndex: int64 option
              [<JsonPropertyName("queueSeed")>]
              QueueSeed: string option
              [<JsonPropertyName("reportedAfter")>]
              ReportedAfter: string option
              [<JsonPropertyName("reportedBefore")>]
              ReportedBefore: string option
              [<JsonPropertyName("reviewState")>]
              ReviewState: string option
              [<JsonPropertyName("reviewedAfter")>]
              ReviewedAfter: string option
              [<JsonPropertyName("reviewedBefore")>]
              ReviewedBefore: string option
              [<JsonPropertyName("sortDirection")>]
              SortDirection: string option
              [<JsonPropertyName("sortField")>]
              SortField: string option
              [<JsonPropertyName("subject")>]
              Subject: string option
              [<JsonPropertyName("subjectType")>]
              SubjectType: string option
              [<JsonPropertyName("tags")>]
              Tags: string list option
              [<JsonPropertyName("takendown")>]
              Takendown: bool option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("subjectStatuses")>]
              SubjectStatuses: Defs.SubjectStatusView list }

    module ScheduleAction =
        [<Literal>]
        let TypeId = "tools.ozone.moderation.scheduleAction"

        type Input =
            { [<JsonPropertyName("action")>]
              Action: JsonElement
              [<JsonPropertyName("createdBy")>]
              CreatedBy: string
              [<JsonPropertyName("modTool")>]
              ModTool: Defs.ModTool option
              [<JsonPropertyName("scheduling")>]
              Scheduling: ScheduleAction.SchedulingConfig
              [<JsonPropertyName("subjects")>]
              Subjects: string list }

        type FailedScheduling =
            { [<JsonPropertyName("error")>]
              Error: string
              [<JsonPropertyName("errorCode")>]
              ErrorCode: string option
              [<JsonPropertyName("subject")>]
              Subject: string }

        type ScheduledActionResults =
            { [<JsonPropertyName("failed")>]
              Failed: ScheduleAction.FailedScheduling list
              [<JsonPropertyName("succeeded")>]
              Succeeded: string list }

        /// Configuration for when the action should be executed
        type SchedulingConfig =
            { [<JsonPropertyName("executeAfter")>]
              ExecuteAfter: string option
              [<JsonPropertyName("executeAt")>]
              ExecuteAt: string option
              [<JsonPropertyName("executeUntil")>]
              ExecuteUntil: string option }

        /// Schedule a takedown action
        type Takedown =
            { [<JsonPropertyName("acknowledgeAccountSubjects")>]
              AcknowledgeAccountSubjects: bool option
              [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("durationInHours")>]
              DurationInHours: int64 option
              [<JsonPropertyName("emailContent")>]
              EmailContent: string option
              [<JsonPropertyName("emailSubject")>]
              EmailSubject: string option
              [<JsonPropertyName("policies")>]
              Policies: string list option
              [<JsonPropertyName("severityLevel")>]
              SeverityLevel: string option
              [<JsonPropertyName("strikeCount")>]
              StrikeCount: int64 option
              [<JsonPropertyName("strikeExpiresAt")>]
              StrikeExpiresAt: string option }

    module SearchRepos =
        [<Literal>]
        let TypeId = "tools.ozone.moderation.searchRepos"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("q")>]
              Q: string option
              [<JsonPropertyName("term")>]
              Term: string option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("repos")>]
              Repos: Defs.RepoView list }

module ToolsOzoneReport =
    module Defs =
        [<Literal>]
        let ReasonAppeal = "tools.ozone.report.defs#reasonAppeal"

        [<Literal>]
        let ReasonChildSafetyCSAM = "tools.ozone.report.defs#reasonChildSafetyCSAM"

        [<Literal>]
        let ReasonChildSafetyGroom = "tools.ozone.report.defs#reasonChildSafetyGroom"

        [<Literal>]
        let ReasonChildSafetyHarassment =
            "tools.ozone.report.defs#reasonChildSafetyHarassment"

        [<Literal>]
        let ReasonChildSafetyOther = "tools.ozone.report.defs#reasonChildSafetyOther"

        [<Literal>]
        let ReasonChildSafetyPrivacy = "tools.ozone.report.defs#reasonChildSafetyPrivacy"

        [<Literal>]
        let ReasonHarassmentDoxxing = "tools.ozone.report.defs#reasonHarassmentDoxxing"

        [<Literal>]
        let ReasonHarassmentHateSpeech =
            "tools.ozone.report.defs#reasonHarassmentHateSpeech"

        [<Literal>]
        let ReasonHarassmentOther = "tools.ozone.report.defs#reasonHarassmentOther"

        [<Literal>]
        let ReasonHarassmentTargeted = "tools.ozone.report.defs#reasonHarassmentTargeted"

        [<Literal>]
        let ReasonHarassmentTroll = "tools.ozone.report.defs#reasonHarassmentTroll"

        [<Literal>]
        let ReasonMisleadingBot = "tools.ozone.report.defs#reasonMisleadingBot"

        [<Literal>]
        let ReasonMisleadingElections = "tools.ozone.report.defs#reasonMisleadingElections"

        [<Literal>]
        let ReasonMisleadingImpersonation =
            "tools.ozone.report.defs#reasonMisleadingImpersonation"

        [<Literal>]
        let ReasonMisleadingOther = "tools.ozone.report.defs#reasonMisleadingOther"

        [<Literal>]
        let ReasonMisleadingScam = "tools.ozone.report.defs#reasonMisleadingScam"

        [<Literal>]
        let ReasonMisleadingSpam = "tools.ozone.report.defs#reasonMisleadingSpam"

        [<Literal>]
        let ReasonOther = "tools.ozone.report.defs#reasonOther"

        [<Literal>]
        let ReasonRuleBanEvasion = "tools.ozone.report.defs#reasonRuleBanEvasion"

        [<Literal>]
        let ReasonRuleOther = "tools.ozone.report.defs#reasonRuleOther"

        [<Literal>]
        let ReasonRuleProhibitedSales = "tools.ozone.report.defs#reasonRuleProhibitedSales"

        [<Literal>]
        let ReasonRuleSiteSecurity = "tools.ozone.report.defs#reasonRuleSiteSecurity"

        [<Literal>]
        let ReasonSelfHarmContent = "tools.ozone.report.defs#reasonSelfHarmContent"

        [<Literal>]
        let ReasonSelfHarmED = "tools.ozone.report.defs#reasonSelfHarmED"

        [<Literal>]
        let ReasonSelfHarmOther = "tools.ozone.report.defs#reasonSelfHarmOther"

        [<Literal>]
        let ReasonSelfHarmStunts = "tools.ozone.report.defs#reasonSelfHarmStunts"

        [<Literal>]
        let ReasonSelfHarmSubstances = "tools.ozone.report.defs#reasonSelfHarmSubstances"

        [<Literal>]
        let ReasonSexualAbuseContent = "tools.ozone.report.defs#reasonSexualAbuseContent"

        [<Literal>]
        let ReasonSexualAnimal = "tools.ozone.report.defs#reasonSexualAnimal"

        [<Literal>]
        let ReasonSexualDeepfake = "tools.ozone.report.defs#reasonSexualDeepfake"

        [<Literal>]
        let ReasonSexualNCII = "tools.ozone.report.defs#reasonSexualNCII"

        [<Literal>]
        let ReasonSexualOther = "tools.ozone.report.defs#reasonSexualOther"

        [<Literal>]
        let ReasonSexualUnlabeled = "tools.ozone.report.defs#reasonSexualUnlabeled"

        type ReasonType = string

        [<Literal>]
        let ReasonViolenceAnimal = "tools.ozone.report.defs#reasonViolenceAnimal"

        [<Literal>]
        let ReasonViolenceExtremistContent =
            "tools.ozone.report.defs#reasonViolenceExtremistContent"

        [<Literal>]
        let ReasonViolenceGlorification =
            "tools.ozone.report.defs#reasonViolenceGlorification"

        [<Literal>]
        let ReasonViolenceGraphicContent =
            "tools.ozone.report.defs#reasonViolenceGraphicContent"

        [<Literal>]
        let ReasonViolenceOther = "tools.ozone.report.defs#reasonViolenceOther"

        [<Literal>]
        let ReasonViolenceThreats = "tools.ozone.report.defs#reasonViolenceThreats"

        [<Literal>]
        let ReasonViolenceTrafficking = "tools.ozone.report.defs#reasonViolenceTrafficking"

module ToolsOzoneSafelink =
    module AddRule =
        [<Literal>]
        let TypeId = "tools.ozone.safelink.addRule"

        type Input =
            { [<JsonPropertyName("action")>]
              Action: Defs.ActionType
              [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("createdBy")>]
              CreatedBy: string option
              [<JsonPropertyName("pattern")>]
              Pattern: Defs.PatternType
              [<JsonPropertyName("reason")>]
              Reason: Defs.ReasonType
              [<JsonPropertyName("url")>]
              Url: string }

        module Errors =
            [<Literal>]
            let InvalidUrl = "InvalidUrl"

            [<Literal>]
            let RuleAlreadyExists = "RuleAlreadyExists"

    module Defs =
        type ActionType = string

        /// An event for URL safety decisions
        type Event =
            { [<JsonPropertyName("action")>]
              Action: Defs.ActionType
              [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("createdBy")>]
              CreatedBy: string
              [<JsonPropertyName("eventType")>]
              EventType: Defs.EventType
              [<JsonPropertyName("id")>]
              Id: int64
              [<JsonPropertyName("pattern")>]
              Pattern: Defs.PatternType
              [<JsonPropertyName("reason")>]
              Reason: Defs.ReasonType
              [<JsonPropertyName("url")>]
              Url: string }

        type EventType = string
        type PatternType = string
        type ReasonType = string

        /// Input for creating a URL safety rule
        type UrlRule =
            { [<JsonPropertyName("action")>]
              Action: Defs.ActionType
              [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("createdBy")>]
              CreatedBy: string
              [<JsonPropertyName("pattern")>]
              Pattern: Defs.PatternType
              [<JsonPropertyName("reason")>]
              Reason: Defs.ReasonType
              [<JsonPropertyName("updatedAt")>]
              UpdatedAt: string
              [<JsonPropertyName("url")>]
              Url: string }

    module QueryEvents =
        [<Literal>]
        let TypeId = "tools.ozone.safelink.queryEvents"

        type Input =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("patternType")>]
              PatternType: string option
              [<JsonPropertyName("sortDirection")>]
              SortDirection: string option
              [<JsonPropertyName("urls")>]
              Urls: string list option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("events")>]
              Events: Defs.Event list }

    module QueryRules =
        [<Literal>]
        let TypeId = "tools.ozone.safelink.queryRules"

        type Input =
            { [<JsonPropertyName("actions")>]
              Actions: string list option
              [<JsonPropertyName("createdBy")>]
              CreatedBy: string option
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("patternType")>]
              PatternType: string option
              [<JsonPropertyName("reason")>]
              Reason: string option
              [<JsonPropertyName("sortDirection")>]
              SortDirection: string option
              [<JsonPropertyName("urls")>]
              Urls: string list option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("rules")>]
              Rules: Defs.UrlRule list }

    module RemoveRule =
        [<Literal>]
        let TypeId = "tools.ozone.safelink.removeRule"

        type Input =
            { [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("createdBy")>]
              CreatedBy: string option
              [<JsonPropertyName("pattern")>]
              Pattern: Defs.PatternType
              [<JsonPropertyName("url")>]
              Url: string }

        module Errors =
            [<Literal>]
            let RuleNotFound = "RuleNotFound"

    module UpdateRule =
        [<Literal>]
        let TypeId = "tools.ozone.safelink.updateRule"

        type Input =
            { [<JsonPropertyName("action")>]
              Action: Defs.ActionType
              [<JsonPropertyName("comment")>]
              Comment: string option
              [<JsonPropertyName("createdBy")>]
              CreatedBy: string option
              [<JsonPropertyName("pattern")>]
              Pattern: Defs.PatternType
              [<JsonPropertyName("reason")>]
              Reason: Defs.ReasonType
              [<JsonPropertyName("url")>]
              Url: string }

        module Errors =
            [<Literal>]
            let RuleNotFound = "RuleNotFound"

module ToolsOzoneServer =
    module GetConfig =
        [<Literal>]
        let TypeId = "tools.ozone.server.getConfig"

        type Output =
            { [<JsonPropertyName("appview")>]
              Appview: GetConfig.ServiceConfig option
              [<JsonPropertyName("blobDivert")>]
              BlobDivert: GetConfig.ServiceConfig option
              [<JsonPropertyName("chat")>]
              Chat: GetConfig.ServiceConfig option
              [<JsonPropertyName("pds")>]
              Pds: GetConfig.ServiceConfig option
              [<JsonPropertyName("verifierDid")>]
              VerifierDid: string option
              [<JsonPropertyName("viewer")>]
              Viewer: GetConfig.ViewerConfig option }

        type ServiceConfig =
            { [<JsonPropertyName("url")>]
              Url: string option }

        type ViewerConfig =
            { [<JsonPropertyName("role")>]
              Role: string option }

module ToolsOzoneSet =
    module AddValues =
        [<Literal>]
        let TypeId = "tools.ozone.set.addValues"

        type Input =
            { [<JsonPropertyName("name")>]
              Name: string
              [<JsonPropertyName("values")>]
              Values: string list }

    module Defs =
        type Set =
            { [<JsonPropertyName("description")>]
              Description: string option
              [<JsonPropertyName("name")>]
              Name: string }

        type SetView =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("description")>]
              Description: string option
              [<JsonPropertyName("name")>]
              Name: string
              [<JsonPropertyName("setSize")>]
              SetSize: int64
              [<JsonPropertyName("updatedAt")>]
              UpdatedAt: string }

    module DeleteSet =
        [<Literal>]
        let TypeId = "tools.ozone.set.deleteSet"

        type Input =
            { [<JsonPropertyName("name")>]
              Name: string }

        module Errors =
            [<Literal>]
            let SetNotFound = "SetNotFound"

    module DeleteValues =
        [<Literal>]
        let TypeId = "tools.ozone.set.deleteValues"

        type Input =
            { [<JsonPropertyName("name")>]
              Name: string
              [<JsonPropertyName("values")>]
              Values: string list }

        module Errors =
            [<Literal>]
            let SetNotFound = "SetNotFound"

    module GetValues =
        [<Literal>]
        let TypeId = "tools.ozone.set.getValues"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("name")>]
              Name: string }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("set")>]
              Set: Defs.SetView
              [<JsonPropertyName("values")>]
              Values: string list }

        module Errors =
            [<Literal>]
            let SetNotFound = "SetNotFound"

    module QuerySets =
        [<Literal>]
        let TypeId = "tools.ozone.set.querySets"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("namePrefix")>]
              NamePrefix: string option
              [<JsonPropertyName("sortBy")>]
              SortBy: string option
              [<JsonPropertyName("sortDirection")>]
              SortDirection: string option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("sets")>]
              Sets: Defs.SetView list }

    module UpsertSet =
        [<Literal>]
        let TypeId = "tools.ozone.set.upsertSet"

module ToolsOzoneSetting =
    module Defs =
        type Option =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string option
              [<JsonPropertyName("createdBy")>]
              CreatedBy: string
              [<JsonPropertyName("description")>]
              Description: string option
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("key")>]
              Key: string
              [<JsonPropertyName("lastUpdatedBy")>]
              LastUpdatedBy: string
              [<JsonPropertyName("managerRole")>]
              ManagerRole: string option
              [<JsonPropertyName("scope")>]
              Scope: string
              [<JsonPropertyName("updatedAt")>]
              UpdatedAt: string option
              [<JsonPropertyName("value")>]
              Value: JsonElement }

    module ListOptions =
        [<Literal>]
        let TypeId = "tools.ozone.setting.listOptions"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("keys")>]
              Keys: string list option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("prefix")>]
              Prefix: string option
              [<JsonPropertyName("scope")>]
              Scope: string option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("options")>]
              Options: Defs.Option list }

    module RemoveOptions =
        [<Literal>]
        let TypeId = "tools.ozone.setting.removeOptions"

        type Input =
            { [<JsonPropertyName("keys")>]
              Keys: string list
              [<JsonPropertyName("scope")>]
              Scope: string }

    module UpsertOption =
        [<Literal>]
        let TypeId = "tools.ozone.setting.upsertOption"

        type Input =
            { [<JsonPropertyName("description")>]
              Description: string option
              [<JsonPropertyName("key")>]
              Key: string
              [<JsonPropertyName("managerRole")>]
              ManagerRole: string option
              [<JsonPropertyName("scope")>]
              Scope: string
              [<JsonPropertyName("value")>]
              Value: JsonElement }

        type Output =
            { [<JsonPropertyName("option")>]
              Option: Defs.Option }

module ToolsOzoneSignature =
    module Defs =
        type SigDetail =
            { [<JsonPropertyName("property")>]
              Property: string
              [<JsonPropertyName("value")>]
              Value: string }

    module FindCorrelation =
        [<Literal>]
        let TypeId = "tools.ozone.signature.findCorrelation"

        type Params =
            { [<JsonPropertyName("dids")>]
              Dids: string list }

        type Output =
            { [<JsonPropertyName("details")>]
              Details: Defs.SigDetail list }

    module FindRelatedAccounts =
        [<Literal>]
        let TypeId = "tools.ozone.signature.findRelatedAccounts"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("limit")>]
              Limit: int64 option }

        type Output =
            { [<JsonPropertyName("accounts")>]
              Accounts: FindRelatedAccounts.RelatedAccount list
              [<JsonPropertyName("cursor")>]
              Cursor: string option }

        type RelatedAccount =
            { [<JsonPropertyName("account")>]
              Account: ComAtprotoAdmin.Defs.AccountView
              [<JsonPropertyName("similarities")>]
              Similarities: Defs.SigDetail list option }

    module SearchAccounts =
        [<Literal>]
        let TypeId = "tools.ozone.signature.searchAccounts"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("values")>]
              Values: string list }

        type Output =
            { [<JsonPropertyName("accounts")>]
              Accounts: ComAtprotoAdmin.Defs.AccountView list
              [<JsonPropertyName("cursor")>]
              Cursor: string option }

module ToolsOzoneTeam =
    module AddMember =
        [<Literal>]
        let TypeId = "tools.ozone.team.addMember"

        type Input =
            { [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("role")>]
              Role: string }

        module Errors =
            [<Literal>]
            let MemberAlreadyExists = "MemberAlreadyExists"

    module Defs =
        type Member =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string option
              [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("disabled")>]
              Disabled: bool option
              [<JsonPropertyName("lastUpdatedBy")>]
              LastUpdatedBy: string option
              [<JsonPropertyName("profile")>]
              Profile: AppBskyActor.Defs.ProfileViewDetailed option
              [<JsonPropertyName("role")>]
              Role: string
              [<JsonPropertyName("updatedAt")>]
              UpdatedAt: string option }

        [<Literal>]
        let RoleAdmin = "tools.ozone.team.defs#roleAdmin"

        [<Literal>]
        let RoleModerator = "tools.ozone.team.defs#roleModerator"

        [<Literal>]
        let RoleTriage = "tools.ozone.team.defs#roleTriage"

        [<Literal>]
        let RoleVerifier = "tools.ozone.team.defs#roleVerifier"

    module DeleteMember =
        [<Literal>]
        let TypeId = "tools.ozone.team.deleteMember"

        type Input =
            { [<JsonPropertyName("did")>]
              Did: string }

        module Errors =
            [<Literal>]
            let MemberNotFound = "MemberNotFound"

            [<Literal>]
            let CannotDeleteSelf = "CannotDeleteSelf"

    module ListMembers =
        [<Literal>]
        let TypeId = "tools.ozone.team.listMembers"

        type Params =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("disabled")>]
              Disabled: bool option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("q")>]
              Q: string option
              [<JsonPropertyName("roles")>]
              Roles: string list option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("members")>]
              Members: Defs.Member list }

    module UpdateMember =
        [<Literal>]
        let TypeId = "tools.ozone.team.updateMember"

        type Input =
            { [<JsonPropertyName("did")>]
              Did: string
              [<JsonPropertyName("disabled")>]
              Disabled: bool option
              [<JsonPropertyName("role")>]
              Role: string option }

        module Errors =
            [<Literal>]
            let MemberNotFound = "MemberNotFound"

module ToolsOzoneVerification =
    module Defs =
        /// Verification data for the associated subject.
        type VerificationView =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string
              [<JsonPropertyName("displayName")>]
              DisplayName: string
              [<JsonPropertyName("handle")>]
              Handle: string
              [<JsonPropertyName("issuer")>]
              Issuer: string
              [<JsonPropertyName("issuerProfile")>]
              IssuerProfile: JsonElement option
              [<JsonPropertyName("issuerRepo")>]
              IssuerRepo: JsonElement option
              [<JsonPropertyName("revokeReason")>]
              RevokeReason: string option
              [<JsonPropertyName("revokedAt")>]
              RevokedAt: string option
              [<JsonPropertyName("revokedBy")>]
              RevokedBy: string option
              [<JsonPropertyName("subject")>]
              Subject: string
              [<JsonPropertyName("subjectProfile")>]
              SubjectProfile: JsonElement option
              [<JsonPropertyName("subjectRepo")>]
              SubjectRepo: JsonElement option
              [<JsonPropertyName("uri")>]
              Uri: string }

    module GrantVerifications =
        [<Literal>]
        let TypeId = "tools.ozone.verification.grantVerifications"

        type Input =
            { [<JsonPropertyName("verifications")>]
              Verifications: GrantVerifications.VerificationInput list }

        type Output =
            { [<JsonPropertyName("failedVerifications")>]
              FailedVerifications: GrantVerifications.GrantError list
              [<JsonPropertyName("verifications")>]
              Verifications: Defs.VerificationView list }

        /// Error object for failed verifications.
        type GrantError =
            { [<JsonPropertyName("error")>]
              Error: string
              [<JsonPropertyName("subject")>]
              Subject: string }

        type VerificationInput =
            { [<JsonPropertyName("createdAt")>]
              CreatedAt: string option
              [<JsonPropertyName("displayName")>]
              DisplayName: string
              [<JsonPropertyName("handle")>]
              Handle: string
              [<JsonPropertyName("subject")>]
              Subject: string }

    module ListVerifications =
        [<Literal>]
        let TypeId = "tools.ozone.verification.listVerifications"

        type Params =
            { [<JsonPropertyName("createdAfter")>]
              CreatedAfter: string option
              [<JsonPropertyName("createdBefore")>]
              CreatedBefore: string option
              [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("isRevoked")>]
              IsRevoked: bool option
              [<JsonPropertyName("issuers")>]
              Issuers: string list option
              [<JsonPropertyName("limit")>]
              Limit: int64 option
              [<JsonPropertyName("sortDirection")>]
              SortDirection: string option
              [<JsonPropertyName("subjects")>]
              Subjects: string list option }

        type Output =
            { [<JsonPropertyName("cursor")>]
              Cursor: string option
              [<JsonPropertyName("verifications")>]
              Verifications: Defs.VerificationView list }

    module RevokeVerifications =
        [<Literal>]
        let TypeId = "tools.ozone.verification.revokeVerifications"

        type Input =
            { [<JsonPropertyName("revokeReason")>]
              RevokeReason: string option
              [<JsonPropertyName("uris")>]
              Uris: string list }

        type Output =
            { [<JsonPropertyName("failedRevocations")>]
              FailedRevocations: RevokeVerifications.RevokeError list
              [<JsonPropertyName("revokedVerifications")>]
              RevokedVerifications: string list }

        /// Error object for failed revocations
        type RevokeError =
            { [<JsonPropertyName("error")>]
              Error: string
              [<JsonPropertyName("uri")>]
              Uri: string }
