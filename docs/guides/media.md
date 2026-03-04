---
title: Media
category: Advanced Guides
categoryindex: 3
index: 11
description: Upload images and attach media to Bluesky posts with FSharp.ATProto
keywords: images, media, upload, blob, alt text, bluesky, fsharp, atproto
---

# Media

All examples use `taskResult {}`. See the [Error Handling guide](error-handling.html) for details.

FSharp.ATProto provides convenience methods for uploading images and attaching them to posts. The high-level API handles blob upload, embed construction, and rich text detection in a single call.

> **Size limit:** Bluesky enforces a **1 MB maximum** per image blob. If your images may exceed this, resize them before uploading. The library sends bytes directly to the PDS without resizing, and oversized uploads will be rejected by the server.

> **Video:** See the [Video](#video) section below for video upload support.

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
```

## Posting with Images

`Bluesky.postWithImages` uploads each image, constructs the embed record, auto-detects [rich text](rich-text.html) facets, and creates the [post](posts.html) -- all in one call:

```fsharp
taskResult {
    let! agent = Bluesky.login "https://bsky.social" "handle.bsky.social" "app-password"

    let imageBytes = System.IO.File.ReadAllBytes "/path/to/photo.jpg"

    let images =
        [ { Data = imageBytes
            MimeType = Jpeg
            AltText = "A sunset over the mountains" } ]

    let! postRef = Bluesky.postWithImages agent "Evening view from the trail" images
    printfn "Posted: %s" (AtUri.value postRef.Uri)
    return postRef
}
```

Each image is described by an `ImageUpload` record:

```fsharp
type ImageUpload =
    { Data : byte[]       // raw image bytes
      MimeType : ImageMime // Jpeg, Png, Gif, Webp, or Custom of string
      AltText : string }   // accessibility text (required)
```

The `MimeType` field uses the `ImageMime` discriminated union for type-safe MIME type selection:

```fsharp
type ImageMime =
    | Png
    | Jpeg
    | Gif
    | Webp
    | Custom of string
```

Use DU cases directly -- `Jpeg`, not `"image/jpeg"`.

Alt text is required. It describes the image content for screen readers. Write it as if describing the image to someone who cannot see it.

## Multiple Images

Bluesky supports up to 4 images per post. Each gets its own `ImageUpload` with independent alt text:

```fsharp
taskResult {
    let! agent = Bluesky.login "https://bsky.social" "handle.bsky.social" "app-password"

    let! postRef =
        Bluesky.postWithImages agent "Before and after"
            [ { Data = System.IO.File.ReadAllBytes "before.png"
                MimeType = Png
                AltText = "The garden before planting, bare soil" }
              { Data = System.IO.File.ReadAllBytes "after.jpg"
                MimeType = Jpeg
                AltText = "The garden three months later, full of tomatoes" } ]

    return postRef
}
```

Images are uploaded sequentially. If any upload fails, the entire operation short-circuits and returns the error without creating the post.

## Rich Text with Images

`postWithImages` auto-detects mentions, links, and hashtags in the text, just like `Bluesky.post`. See the [Rich Text guide](rich-text.html) for how facet detection works.

```fsharp
taskResult {
    let! agent = Bluesky.login "https://bsky.social" "handle.bsky.social" "app-password"

    let images =
        [ { Data = System.IO.File.ReadAllBytes "screenshot.png"
            MimeType = Png
            AltText = "Screenshot of the FSharp.ATProto test suite passing" } ]

    let! postRef =
        Bluesky.postWithImages agent "All tests passing! @friend.bsky.social #fsharp" images

    return postRef
}
```

The `@friend.bsky.social` mention is resolved to a [DID](../concepts.html), the `#fsharp` hashtag becomes a clickable facet, and the image is attached as an embed.

## Uploading Blobs Directly

`Bluesky.uploadBlob` is the lower-level API for uploading binary data to the PDS. It returns a typed `BlobRef`:

```fsharp
taskResult {
    let! agent = Bluesky.login "https://bsky.social" "handle.bsky.social" "app-password"
    let data = System.IO.File.ReadAllBytes "diagram.png"
    let! blobRef = Bluesky.uploadBlob agent data Png
    printfn "Uploaded: CID=%s, size=%d bytes" (Cid.value blobRef.Ref) blobRef.Size
    return blobRef
}
```

The `BlobRef` type:

```fsharp
type BlobRef =
    { Json : JsonElement   // raw blob reference for embedding in custom records
      Ref : Cid            // content identifier
      MimeType : string    // e.g. "image/png"
      Size : int64 }       // size in bytes
```

This is useful when `postWithImages` is not flexible enough -- for example, uploading a blob once and referencing it in multiple records, or constructing a custom embed type via the [raw XRPC](raw-xrpc.html) layer.

## Video

Upload and post video content. Video processing is asynchronous -- the server transcodes the video after upload.

### Functions

| Function | Description |
|----------|-------------|
| `Bluesky.uploadVideo` | Upload video bytes, returns a `JobStatus` with processing state |
| `Bluesky.getVideoJobStatus` | Poll the processing status of an uploaded video |
| `Bluesky.awaitVideoProcessing` | Poll until processing completes (with configurable max attempts) |
| `Bluesky.postWithVideo` | Upload, wait for processing, and create a post -- all in one call |

### Quick Example

```fsharp
taskResult {
    let! agent = Bluesky.login "https://bsky.social" "handle" "app-password"
    let videoBytes = System.IO.File.ReadAllBytes("clip.mp4")
    let! post = Bluesky.postWithVideo agent "Check out this video!" videoBytes "video/mp4" "A short clip"
    printfn "Posted video: %s" (AtUri.value post.Uri)
}
```

### Step-by-Step

For more control over the upload process:

```fsharp
taskResult {
    let! agent = Bluesky.login "https://bsky.social" "handle" "app-password"
    let videoBytes = System.IO.File.ReadAllBytes("clip.mp4")

    // 1. Upload the video
    let! jobStatus = Bluesky.uploadVideo agent videoBytes "video/mp4"

    // 2. Wait for server-side processing (max 60 poll attempts)
    let! completed = Bluesky.awaitVideoProcessing agent jobStatus.JobId 60

    // 3. Create a post with the processed video
    // (use raw XRPC with the blob ref from the completed job)
}
```

> **Note:** Videos have server-enforced size limits. MP4 is the supported format.

## Supported Formats

Bluesky accepts **JPEG**, **PNG**, **GIF**, and **WebP** images. Use the corresponding `ImageMime` case (`Jpeg`, `Png`, `Gif`, `Webp`) or `Custom of string` for anything else.
