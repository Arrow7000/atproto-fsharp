---
title: Media
category: Guides
categoryindex: 1
index: 8
description: Upload images and attach media to Bluesky posts with FSharp.ATProto
keywords: images, media, upload, blob, alt text, bluesky
---

# Media

FSharp.ATProto provides convenience methods for uploading images and attaching them to posts. The high-level API handles blob upload, embed construction, and rich text detection in a single call.

All examples below assume you have an authenticated agent:

```fsharp
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax
```

## Posting with Images

`Bluesky.postWithImages` is the easiest way to create a post with attached images. It uploads each image, constructs the embed record, detects rich text facets, and creates the post:

```fsharp
task {
    let imageBytes = System.IO.File.ReadAllBytes ("/path/to/photo.jpg")

    let images =
        [ { Data = imageBytes
            MimeType = Jpeg
            AltText = "A sunset over the mountains" } ]

    let! result = Bluesky.postWithImages agent "Evening view from the trail" images

    match result with
    | Ok postRef -> printfn "Posted: %s" (AtUri.value postRef.Uri)
    | Error err -> printfn "Failed: %A" err
}
```

Each image is described by an `ImageUpload` record:

```fsharp
type ImageUpload =
    { Data : byte[] // raw binary image data
      MimeType : ImageMime // Jpeg, Png, Gif, Webp, or Custom of string
      AltText : string } // accessibility text (required)
```

The `MimeType` field uses the `ImageMime` discriminated union, which provides type-safe MIME type selection:

```fsharp
type ImageMime =
    | Png
    | Jpeg
    | Gif
    | Webp
    | Custom of string // for other MIME types not covered above
```

Use the DU cases directly -- `Jpeg`, not `"image/jpeg"`.

Alt text is required. It describes the image content for screen readers and users who cannot see the image. Write it as if you are describing the image to someone over the phone.

## Multiple Images

Bluesky supports up to 4 images per post. Each gets its own `ImageUpload` record with independent alt text:

```fsharp
task {
    let! result =
        Bluesky.postWithImages
            agent
            "Before and after"
            [ { Data = System.IO.File.ReadAllBytes ("before.png")
                MimeType = Png
                AltText = "The garden before planting, bare soil" }
              { Data = System.IO.File.ReadAllBytes ("after.jpg")
                MimeType = Jpeg
                AltText = "The garden three months later, full of tomatoes" } ]

    match result with
    | Ok postRef -> printfn "Posted: %s" (AtUri.value postRef.Uri)
    | Error err -> printfn "Failed: %A" err
}
```

Images are uploaded sequentially. If any upload fails, the entire operation returns the error without creating the post.

## Uploading Blobs Directly

`Bluesky.uploadBlob` is the lower-level API for uploading binary data to the PDS. It takes `byte[]` and an `ImageMime` value, and returns a typed `BlobRef`:

```fsharp
task {
    let data = System.IO.File.ReadAllBytes ("diagram.png")

    let! result = Bluesky.uploadBlob agent data Png

    match result with
    | Ok blobRef ->
        // BlobRef has typed fields:
        //   Json: JsonElement   -- the raw blob JSON for embedding in records
        //   Ref: Cid            -- content identifier of the uploaded blob
        //   MimeType: string    -- e.g. "image/png"
        //   Size: int64         -- blob size in bytes
        printfn "Uploaded: CID=%s, size=%d bytes" (Cid.value blobRef.Ref) blobRef.Size
    | Error err -> printfn "Upload failed: %A" err
}
```

The `BlobRef` return type:

```fsharp
type BlobRef =
    { Json : JsonElement // raw blob reference for use in custom records
      Ref : Cid // content identifier
      MimeType : string // MIME type string
      Size : int64 } // size in bytes
```

This is useful when `postWithImages` is not flexible enough -- for example, when you want to upload a blob once and reference it in multiple posts, or when you need to construct a custom embed type.

## Rich Text with Images

`postWithImages` auto-detects mentions, links, and hashtags in the text, just like `Bluesky.post`. Rich text and images compose naturally:

```fsharp
task {
    let images =
        [ { Data = System.IO.File.ReadAllBytes ("screenshot.png")
            MimeType = Png
            AltText = "Screenshot of the FSharp.ATProto test suite passing" } ]

    let! result =
        Bluesky.postWithImages agent "All 1,503 tests passing! @my-handle.bsky.social check it out #fsharp" images

    match result with
    | Ok postRef -> printfn "Posted: %s" (AtUri.value postRef.Uri)
    | Error err -> printfn "Failed: %A" err
}
```

The `@my-handle.bsky.social` mention is resolved to a DID, the `#fsharp` hashtag becomes a clickable facet, and the image is attached as an embed -- all in one call.

## Supported Formats

Bluesky accepts **JPEG**, **PNG**, **GIF**, and **WebP** images. The maximum blob size is **1 MB**.

If your images may exceed this limit, resize them before uploading. The library does not perform automatic resizing -- it sends the bytes you provide directly to the PDS, and oversized uploads will be rejected by the server.
