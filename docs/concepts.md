---
title: Concepts
category: Guides
categoryindex: 1
index: 3
description: AT Protocol concepts explained for newcomers
keywords: atproto, bluesky, did, handle, at-uri, cid, pds, xrpc, lexicon
---

# Concepts

A plain-language guide to the terms you will encounter when working with AT Protocol.

## Bluesky and AT Protocol

Bluesky is a social network. AT Protocol (atproto) is the open protocol it runs on. Think email: Bluesky is like Gmail, AT Protocol is like SMTP. Anyone can build on AT Protocol, and Bluesky is the most prominent app. FSharp.ATProto gives you access to both Bluesky-specific features and the underlying protocol.

## DID

A DID (Decentralized Identifier) is a user's permanent identity. It looks like `did:plc:z72i7hdynmk6r22z27h6tvur`. Unlike a username, a DID never changes -- even if the user switches handles or moves servers. When the API needs to identify a user unambiguously, it uses a DID.

## Handle

A Handle is the human-readable username, like `alice.bsky.social` or `alice.com`. Handles can change, but each one maps to exactly one DID. The library accepts handles wherever you need to identify a user.

## AT-URI

An AT-URI is an address for a specific piece of content. It looks like `at://did:plc:xxx/app.bsky.feed.post/3k2la3b`. The three parts are: who created it (a DID), what type it is (a collection), and a unique key. You will see AT-URIs when creating, fetching, or deleting content.

## CID

A CID (Content Identifier) is a cryptographic hash of a specific version of content -- a fingerprint. If the content changes, the CID changes. AT-URIs tell you *where* something is; CIDs tell you *which exact version*. Together, a URI and CID form an unambiguous reference -- this is what `PostRef` holds.

## PDS

A PDS (Personal Data Server) is where your data lives. Posts, follows, likes -- all stored on your PDS. Most Bluesky users are on `bsky.social`, but anyone can run their own. The library connects to a PDS when you call `Bluesky.login`.

## XRPC

XRPC is AT Protocol's RPC mechanism -- HTTP requests with a naming convention. You mostly do not need to think about it. The `Bluesky` and `Chat` modules wrap XRPC calls for you. If you ever need direct access, the `Xrpc` module is available.

## Lexicon

Lexicon is AT Protocol's schema system, defining the shape of every request and response using JSON schema files. FSharp.ATProto generates F# types from these schemas at build time. You never need to touch Lexicon files yourself.

## Records

In AT Protocol, all user-created data is stored as records. Posts, likes, follows -- all records. Each lives in a named collection (like `app.bsky.feed.post`) with a unique key. The library abstracts this away -- you work with `PostRef`, `LikeRef`, and `FollowRef` instead of thinking about records directly.

---

You do not need to understand all of this to use FSharp.ATProto. The library handles the protocol complexity for you. These concepts are here for when you encounter a term in the API and want to know what it means.

Ready to start? Head to the [Quickstart](quickstart.html).
