# AT Protocol Layers Reference

## Protocol Architecture (4 layers)

1. **Identity** -- DIDs (did:plc, did:web), Handles (DNS-verified), bidirectional verification
2. **Data** -- Repositories (signed commits, MST), Records (CBOR-encoded), Labels
3. **Network** -- XRPC (HTTP GET=query, POST=procedure), WebSocket subscriptions, PDS/Relay/AppView/Labeler roles
4. **Application** -- Lexicon-defined schemas (app.bsky.*, com.atproto.*, chat.bsky.*, tools.ozone.*)

## XRPC

- All endpoints: `/xrpc/{NSID}`
- Queries = GET (read-only, cacheable), Procedures = POST (mutating)
- Subscriptions = WebSocket event streams
- Auth: OAuth 2.1 + DPoP (modern) or legacy JWT bearer tokens (app passwords)
- Errors: `{"error": "ErrorType", "message": "..."}`
- Pagination: cursor-based

## Data Model (DRISL/CBOR)

- Binary encoding: DRISL (successor to DAG-CBOR) -- normalized CBOR subset
- NO FLOATS in ATProto (stricter than DRISL which allows 64-bit floats)
- Types: null, bool, int64, string (UTF-8), bytes, cid-link (Tag 42), array, object (sorted string keys), blob
- JSON conventions: `$link` for CIDs, `$bytes` for binary, `$type` for record type discrimination
- CIDs: CIDv1, SHA-256, codec 0x71 (DRISL) or 0x55 (raw), base32 string with `b` prefix

## Repositories

- Per-account container for public content, hosted on PDS
- Commit structure: signed (ECDSA, low-S), version 3, contains MST root CID
- MST: Merkle Search Tree, key = "collection/rkey", value = record CID
- Key depth: `countLeadingZeroPairs(SHA256(key))`, fanout of 4
- TIDs: 64-bit (53 bits timestamp + 10 bits clock ID), 13-char base32-sortable

## Event Streams (Firehose)

- Binary DRISL-CBOR over WebSockets
- Each frame: header CBOR + payload CBOR
- Events: #commit (new data), #identity (DID changes), #account (hosting status)
- Monotonically increasing sequence numbers, cursor-based resumption
- Data transferred in CAR (Content Addressable aRchive) format

## Cryptography

- Curves: P-256 (NIST) and K-256 (secp256k1)
- Signing: ECDSA with mandatory low-S normalization
- Keys: Multibase (base58btc, `z` prefix) + Multicodec

## Moderation / Labels

- Labels: self-authenticating signed annotations on accounts/content
- Structure: src (labeler DID), uri (subject), val (label string), sig (signature)
- Distribution: WebSocket subscription + HTTP query
- Labelers: independent services with their own DIDs and signing keys

## IETF Standardization

- Working group "Authenticated Transfer" (atproto) proposed
- Charter entered External Review Feb 14, 2026; IESG telechat March 5, 2026
- Will cover: repo data structure, sync protocol, `at:` URI scheme, identity resolution
