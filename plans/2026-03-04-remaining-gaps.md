# Remaining Gaps: Road to Reference-Implementation Parity

After completing all 9 phases of the feature gap plan, the library has 2,067 tests across 10 projects. This document lists every remaining gap versus the official AT Protocol reference implementations (TypeScript SDK + Go indigo).

## Current test counts (baseline)

| Project | Tests |
|---------|-------|
| Syntax | 758 |
| DRISL | 112 |
| Lexicon | 387 |
| CodeGen | 179 |
| Core | 50 |
| Bluesky | 336 |
| Streaming | 47 |
| Moderation | 127 |
| FeedGenerator | 16 |
| OAuth | 55 |
| **Total** | **2,067** |

---

## Phase 10: OAuth-AtpAgent Bridge

**Effort:** 1-2 days | **New project:** No | **Dependencies:** None

The OAuth package handles PKCE+DPoP+PAR+discovery but the resulting `OAuthSession` can't be used with `AtpAgent` or any of the 100+ convenience functions. Need to bridge them.

**Tasks:**
- Abstract session management: introduce a `SessionManager` concept (or extend `AtpAgent`) so it can hold either an app-password `AtpSession` or an `OAuthSession`
- Route XRPC requests through DPoP-authenticated HTTP when using OAuth (add DPoP proof header + `Authorization: DPoP <token>` instead of `Bearer`)
- Automatic token refresh via OAuth refresh token (parallel to existing app-password refresh)
- `AtpAgent.loginWithOAuth` or equivalent entry point
- Session persistence callback: fire on session create/update/expire so consumers can save sessions to disk/database

**Files:** `src/FSharp.ATProto.Core/Types.fs`, `src/FSharp.ATProto.Core/Xrpc.fs`, `src/FSharp.ATProto.Core/AtpAgent.fs`
**Tests:** Core.Tests

---

## Phase 11: Cryptography Package

**Effort:** 1-2 weeks | **New project:** `FSharp.ATProto.Crypto` | **Dependencies:** None

The protocol uses two elliptic curves: `p256` (NIST P-256) and `k256` (secp256k1). Need general-purpose key management, signing, and verification.

**Tasks:**
- Key pair generation for P-256 and secp256k1
- Sign / verify with both curves
- Multikey format encoding/decoding (`did:key` representations)
- DID key derivation (compress public key -> multicodec -> multibase)
- Repository commit signature creation and verification
- Move DPoP's ES256 operations to this shared package (OAuth references Crypto instead of duplicating)

**Files:**
- `src/FSharp.ATProto.Crypto/Keys.fs` (key gen, import/export)
- `src/FSharp.ATProto.Crypto/Signing.fs` (sign, verify)
- `src/FSharp.ATProto.Crypto/Multikey.fs` (multicodec, did:key)
**Tests:** Interop test vectors from `extern/atproto-interop-tests` if available, plus property-based tests

**Note:** .NET has built-in P-256 support (`ECDsa` with `ECCurve.NamedCurves.nistP256`). For secp256k1, either use `System.Security.Cryptography` if .NET 10 supports it, or use a NuGet package like `Secp256k1.Net` or implement manually.

---

## Phase 12: Repository Data Structures (MST)

**Effort:** 2-3 weeks | **New project:** `FSharp.ATProto.Repo` | **Dependencies:** Phase 11 (Crypto for commit signing)

Full Merkle Search Tree implementation for reading and writing AT Protocol repositories.

**Tasks:**
- MST node types: `MstNode`, `MstEntry` (prefix-compressed keys + CID links)
- MST read: parse MST nodes from DAG-CBOR blocks (from CAR files)
- MST write: create/update/delete entries, recompute tree structure
- MST diff: compare two MST roots to produce a list of record operations
- MST verification: validate tree structure invariants (fanout, key ordering, depth)
- Signed commit creation: build commit object with `did`, `version`, `data` (MST root CID), `rev`, `prev`, `sig`
- Signed commit verification: verify commit signature against DID document signing key
- Repository export: serialize a full repo to CAR format
- Repository import: deserialize CAR into an in-memory repo

**Files:**
- `src/FSharp.ATProto.Repo/Mst.fs` (MST data structure)
- `src/FSharp.ATProto.Repo/Commit.fs` (signed commits)
- `src/FSharp.ATProto.Repo/Repo.fs` (high-level repo operations)
**Tests:** Use firehose CAR data as test fixtures, plus synthetic MST construction tests

---

## Phase 13: Service Authentication

**Effort:** 3-5 days | **New project:** No (extends Core) | **Dependencies:** Phase 11 (Crypto)

Service-to-service JWT authentication for backend services (labelers, feed generators, etc.) that need to authenticate to a PDS.

**Tasks:**
- Service auth JWT creation: sign a JWT with the service's signing key, include `iss` (service DID), `aud` (target PDS DID), `exp`, `iat`, `lxm` (lexicon method being called)
- Service auth JWT validation: verify signature, check expiry, validate claims
- `AtpAgent.withServiceAuth` or similar: configure an agent to use service auth for requests
- Convenience wrapper for `com.atproto.server.getServiceAuth`

**Files:** `src/FSharp.ATProto.Core/ServiceAuth.fs`
**Tests:** JWT structure validation, signature verification, expiry checks

---

## Phase 14: Moderation Enhancements

**Effort:** 2-3 days | **New project:** No | **Dependencies:** None

**Tasks:**
- Custom labeler definition support: parse `com.atproto.label.defs#labelValueDefinition` into `LabelDefinition` objects, merge with built-in labels
- `interpretLabelValueDefinition` function (matching TS SDK)
- `moderateNotification`, `moderateFeedGenerator`, `moderateUserList` convenience functions
- Labeler configuration on agent: `configureLabelers` method that sets `atproto-accept-labelers` header
- `getLabelers` and `getLabelDefinitions` convenience wrappers in Bluesky module

**Files:** `src/FSharp.ATProto.Moderation/Labels.fs`, `src/FSharp.ATProto.Bluesky/Bluesky.fs`
**Tests:** Custom label parsing, labeler header generation

---

## Phase 15: Rich Text Enhancements

**Effort:** 1-2 days | **New project:** No | **Dependencies:** None

**Tasks:**
- `insert : int -> string -> RichText -> RichText` — insert text at byte index, shift facet indices
- `delete : int -> int -> RichText -> RichText` — delete byte range, shift/truncate facets
- `segments : RichText -> RichTextSegment list` — split into segments by facet boundaries for rendering (each segment has text + optional facet)
- `sanitize : RichText -> RichText` — clean excessive whitespace, trim, normalize newlines
- `truncate : int -> RichText -> RichText` — truncate to grapheme limit preserving facet integrity

**Files:** `src/FSharp.ATProto.Bluesky/RichText.fs`
**Tests:** Facet adjustment after insert/delete, segment boundary splitting, edge cases

---

## Phase 16: PLC Directory Operations

**Effort:** ~1 week | **New project:** No (extends Core or Bluesky) | **Dependencies:** Phase 11 (Crypto for signing)

**Tasks:**
- PLC operation types: `create`, `plcTombstone`, `updateHandle`, `updateAtpPds`, `updateRotationKeys`
- PLC operation signing (requires rotation key)
- PLC audit log querying: `GET /did:plc:xxx/log/audit`
- PLC export: `GET /export` with cursor-based pagination
- `resolveDidPlc` enhancement: return full PLC document, not just the endpoint

**Files:** `src/FSharp.ATProto.Core/Plc.fs` or similar
**Tests:** Operation serialization, audit log parsing

---

## Phase 17: XRPC Server Framework

**Effort:** 2-3 weeks | **New project:** `FSharp.ATProto.XrpcServer` | **Dependencies:** None

Generic XRPC server framework (the Feed Generator project demonstrates the pattern for one specific case).

**Tasks:**
- Middleware for XRPC method routing (NSID -> handler)
- Request validation against lexicon schemas
- Authentication middleware (verify bearer tokens, service auth JWTs)
- Rate limiting middleware
- Error response formatting (AT Protocol error body format)
- Health check endpoint
- Integrate with the existing Lexicon parser for schema validation

**Files:**
- `src/FSharp.ATProto.XrpcServer/Middleware.fs`
- `src/FSharp.ATProto.XrpcServer/Auth.fs`
- `src/FSharp.ATProto.XrpcServer/Server.fs`
**Tests:** Request routing, auth validation, error formatting

---

## Phase 18: Ozone Moderation Tooling

**Effort:** ~1 week | **New project:** No (extends Bluesky) | **Dependencies:** None

Convenience layer for `tools.ozone.*` endpoints (moderation admin, report management, queue management).

**Tasks:**
- Domain types: `ModerationEvent`, `ModerationReport`, `SubjectStatus`, `ModerationAction` (takedown, label, flag, acknowledge, etc.)
- Report management: `createReport`, `getReport`, `listReports`, `resolveReport`
- Subject management: `getSubjectStatus`, `updateSubjectStatus`, `searchSubjects`
- Moderation events: `emitModerationEvent` (takedown, reverse-takedown, label, flag, acknowledge, escalate, mute, unmute)
- Team management: `listMembers`, `addMember`, `removeMember`, `updateMember`
- Communication templates: `listTemplates`, `createTemplate`, `updateTemplate`, `deleteTemplate`
- Sets/rules: `listSets`, `upsertSet`, `deleteSet`

**Files:** `src/FSharp.ATProto.Bluesky/Ozone.fs` (new file in Bluesky project)
**Tests:** Mock-based tests following existing patterns

---

## Phase 19: OAuth Server

**Effort:** 3-4 weeks | **New project:** `FSharp.ATProto.OAuthServer` | **Dependencies:** Phase 11 (Crypto), Phase 17 (XRPC Server)

For building authorization servers (only the official TS and Go implementations have this).

**Tasks:**
- Authorization endpoint implementation
- Token endpoint implementation (authorization_code + refresh_token grants)
- PAR endpoint implementation (RFC 9126)
- DPoP validation (verify client DPoP proofs)
- Client metadata validation and fetching (loopback clients, web clients)
- PKCE validation
- Scope validation against AT Protocol permission sets
- Token storage interface (pluggable backend)
- Authorization consent UI interface
- Session management (login, consent, token lifecycle)

**Files:**
- `src/FSharp.ATProto.OAuthServer/Authorization.fs`
- `src/FSharp.ATProto.OAuthServer/Token.fs`
- `src/FSharp.ATProto.OAuthServer/DPoPValidator.fs`
- `src/FSharp.ATProto.OAuthServer/ClientMetadata.fs`
- `src/FSharp.ATProto.OAuthServer/Server.fs`
**Tests:** Full flow tests with mock clients

---

## Phase 20: Small Conveniences

**Effort:** 1 day | **New project:** No | **Dependencies:** None

Loose ends and small additions.

**Tasks:**
- `createAccount` convenience wrapper
- `via` attribution parameter on `like`, `repost`, `follow`
- Age assurance helpers (region-based age verification)
- `deleteAccount` convenience wrapper
- `requestAccountDelete` / `confirmAccountDelete` wrappers
- Test factory utilities for consumers (mock post/profile/label builders)

**Files:** `src/FSharp.ATProto.Bluesky/Bluesky.fs`
**Tests:** Basic parameter-passing tests

---

## Effort Summary

| Phase | Feature | Effort | Priority |
|-------|---------|--------|----------|
| 10 | OAuth-AtpAgent Bridge | 1-2 days | Critical |
| 11 | Cryptography Package | 1-2 weeks | Critical |
| 12 | Repository (MST) | 2-3 weeks | Critical |
| 13 | Service Authentication | 3-5 days | Critical |
| 14 | Moderation Enhancements | 2-3 days | Nice-to-have |
| 15 | Rich Text Enhancements | 1-2 days | Nice-to-have |
| 16 | PLC Directory Operations | ~1 week | Nice-to-have |
| 17 | XRPC Server Framework | 2-3 weeks | Stretch |
| 18 | Ozone Moderation Tooling | ~1 week | Stretch |
| 19 | OAuth Server | 3-4 weeks | Stretch |
| 20 | Small Conveniences | 1 day | Nice-to-have |
| | **Total** | **~12-16 weeks** | |
