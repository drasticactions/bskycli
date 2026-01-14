// <copyright file="Program.cs" company="Drastic Actions">
// Copyright (c) Drastic Actions. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using Bskycli;
using ConsoleAppFramework;
using FishyFlip;
using FishyFlip.Lexicon;
using FishyFlip.Lexicon.App.Bsky.Embed;
using FishyFlip.Lexicon.Chat.Bsky.Convo;
using FishyFlip.Models;
using FishyFlip.Tools;
using Microsoft.Extensions.Logging;
using File = System.IO.File;

var app = ConsoleApp.Create();
app.Add<AppCommands>();
app.Run(args);

/// <summary>
/// App Commands.
/// </summary>
#pragma warning disable SA1649 // File name should match first type name
public class AppCommands
#pragma warning restore SA1649 // File name should match first type name
{
    /// <summary>
    /// Create a new post with a video.
    /// </summary>
    /// <param name="videoPath">Path to video.</param>
    /// <param name="username">-u, Username.</param>
    /// <param name="password">-p, Password.</param>
    /// <param name="embedRecord">-r, The record to embed in the post.</param>
    /// <param name="embedRecordCid">-c, The CID of the record to embed in the post. Required if embedding record.</param>
    /// <param name="alt">-a, The alt text for the video.</param>
    /// <param name="vttFiles">-vtt, The VTT files for the video.</param>
    /// <param name="vttFileLanaguages">-vttl, The languages for the VTT files.</param>
    /// <param name="post">-t, The post text to create, can be written using a subset of markdown.</param>
    /// <param name="languages">-l, The languages represented in the post.</param>
    /// <param name="instanceUrl">-i, Instance URL.</param>
    /// <param name="verbose">-v, Verbose logging.</param>
    /// <param name="cancellationToken">Cancellation Token.</param>
    /// <returns>Task.</returns>
    [Command("post video")]
    public async Task CreatePostWithVideoAsync([Argument] string videoPath, string username, string password, string? embedRecord = default, string? embedRecordCid = default, string? alt = default, string[]? vttFiles = default, string[]? vttFileLanaguages = default, string? post = default, string[]? languages = default, string instanceUrl = "https://public.api.bsky.app", bool verbose = false, CancellationToken cancellationToken = default)
    {
        var consoleLog = new ConsoleLog(verbose);
        ATUri? atUri = null;
        if (!string.IsNullOrEmpty(embedRecord) && !ATUri.TryCreate(embedRecord, out atUri))
        {
            consoleLog.LogError("Invalid record URI for embedding.");
            return;
        }

        if (atUri is not null && string.IsNullOrEmpty(embedRecordCid))
        {
            consoleLog.LogError("CID is required for embedding a record.");
            return;
        }

        if (File.Exists(videoPath) == false)
        {
            consoleLog.LogError("Video file does not exist.");
            return;
        }

        if (!this.IsFFmpegInstalled() || !this.IsFFprobeInstalled())
        {
            // TODO: Add a more complete error message.
            consoleLog.LogError("FFmpeg is not installed.");
            return;
        }

        foreach (var vttFile in vttFiles ?? new string[] { })
        {
            if (!File.Exists(vttFile))
            {
                consoleLog.LogError($"VTT file does not exist: {vttFile}.");
                return;
            }
        }

        if (vttFiles?.Length != vttFileLanaguages?.Length)
        {
            consoleLog.LogError("VTT files and languages do not match.");
            return;
        }

        var atProtocol = this.GenerateProtocol(instanceUrl, consoleLog);
        if (await this.AuthenticateWithAppPasswordAsync(username, password, atProtocol, consoleLog) == false)
        {
            return;
        }

        var captions = new List<Caption>();
        if (vttFiles != null)
        {
            for (int i = 0; i < vttFiles.Length; i++)
            {
                var caption = await this.UploadCaptionAsync(vttFiles[i], vttFileLanaguages![i], atProtocol, consoleLog);
                if (caption is null)
                {
                    return;
                }

                captions.Add(caption);
            }
        }

        var (width, height) = this.GetDimensions(videoPath);

        using var videoStream = File.OpenRead(videoPath);
        var videoContentStream = new StreamContent(videoStream);
        videoContentStream.Headers.ContentLength = videoStream.Length;
        videoContentStream.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");

        var (videoResult, videoError) = await atProtocol.Repo.UploadBlobAsync(videoContentStream, cancellationToken);
        if (videoError != null)
        {
            consoleLog.LogError(videoError.ToString());
            return;
        }

        EmbedVideo embedVideo = new EmbedVideo(
        video: videoResult!.Blob,
        alt: alt ?? string.Empty,
        aspectRatio: new(width: width, height: height),
        captions: captions);

        var markdownPost = MarkdownPost.Parse(post ?? string.Empty);
        if (markdownPost is null)
        {
            consoleLog.LogError("Invalid post.");
            return;
        }

        languages ??= new string[] { };

        (var result, var error) = await atProtocol.Feed.CreatePostAsync(text: markdownPost.Post, embed: this.GenerateRecordWithMedia(atUri, embedVideo, embedRecordCid, consoleLog), facets: markdownPost.Facets, langs: languages.ToList(), cancellationToken: cancellationToken);
        if (error != null)
        {
            consoleLog.LogError(error.ToString());
            return;
        }

        consoleLog.Log($"Post created: {result!.Uri}.");
    }

    /// <summary>
    /// Create a new post with images.
    /// </summary>
    /// <param name="imagePaths">Path to the image(s) to upload.</param>
    /// <param name="username">-u, Username.</param>
    /// <param name="password">-p, Password.</param>
    /// <param name="embedRecord">-r, The record to embed in the post.</param>
    /// <param name="embedRecordCid">-c, The CID of the record to embed in the post. Required if embedding record.</param>
    /// <param name="imageAlts">-a, Alt tags for the images.</param>
    /// <param name="post">-t, The post text to create, can be written using a subset of markdown.</param>
    /// <param name="languages">-l, The languages represented in the post.</param>
    /// <param name="instanceUrl">-i, Instance URL.</param>
    /// <param name="verbose">-v, Verbose logging.</param>
    /// <param name="cancellationToken">Cancellation Token.</param>
    /// <returns>Task.</returns>
    [Command("post image")]
    public async Task CreatePostWithImagesAsync([Argument] string[] imagePaths, string username, string password, string? embedRecord = default, string? embedRecordCid = default, string[]? imageAlts = default, string? post = default, string[]? languages = default, string instanceUrl = "https://public.api.bsky.app", bool verbose = false, CancellationToken cancellationToken = default)
    {
        var consoleLog = new ConsoleLog(verbose);

        ATUri? atUri = null;
        if (!string.IsNullOrEmpty(embedRecord) && !ATUri.TryCreate(embedRecord, out atUri))
        {
            consoleLog.LogError("Invalid record URI for embedding.");
            return;
        }

        if (atUri is not null && string.IsNullOrEmpty(embedRecordCid))
        {
            consoleLog.LogError("CID is required for embedding a record.");
            return;
        }

        if (!this.IsFFmpegInstalled() || !this.IsFFprobeInstalled())
        {
            // TODO: Add a more complete error message.
            consoleLog.LogError("FFmpeg is not installed.");
            return;
        }

        var atProtocol = this.GenerateProtocol(instanceUrl, consoleLog);

        foreach (var imagePath in imagePaths)
        {
            if (File.Exists(imagePath) == false)
            {
                consoleLog.LogError($"Image file does not exist: {imagePath}.");
                return;
            }
        }

        if (await this.AuthenticateWithAppPasswordAsync(username, password, atProtocol, consoleLog) == false)
        {
            return;
        }

        var fileContent = new FileContentTypeDetector(consoleLog.Logger);

        var images = new List<Image>();
        for (int i = 0; i < imagePaths.Length; i++)
        {
            string? imagePath = imagePaths[i];
            var (width, height) = this.GetDimensions(imagePath);
            using var imageStream = File.OpenRead(imagePath);
            var imageContentStream = new StreamContent(imageStream);
            imageContentStream.Headers.ContentLength = imageStream.Length;
            var contentType = fileContent.GetContentType(imageStream);
            if (contentType == "unsupported")
            {
                consoleLog.LogError($"Unsupported image type: {imagePath}.");
                return;
            }

            imageContentStream.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            var (imageResult, imageError) = await atProtocol.Repo.UploadBlobAsync(imageContentStream, cancellationToken);
            if (imageError != null)
            {
                consoleLog.LogError(imageError.ToString());
                return;
            }

            images.Add(new Image(image: imageResult!.Blob, aspectRatio: new(width, height), alt: imageAlts?[i].Trim() ?? string.Empty));
        }

        var markdownPost = MarkdownPost.Parse(post ?? string.Empty);

        languages ??= new string[] { };

        var embedImages = new EmbedImages(images: images);

        (var result, var error) = await atProtocol.Feed.CreatePostAsync(text: markdownPost.Post, embed: this.GenerateRecordWithMedia(atUri, embedImages, embedRecordCid, consoleLog), facets: markdownPost.Facets, langs: languages.ToList(), cancellationToken: cancellationToken);
        if (error != null)
        {
            consoleLog.LogError(error.ToString());
            return;
        }

        consoleLog.Log($"Post created: {result!.Uri}.");
    }

    /// <summary>
    /// Create a new post.
    /// </summary>
    /// <param name="post">The post to create, can be written using a subset of markdown.</param>
    /// <param name="username">-u, Username.</param>
    /// <param name="password">-p, Password.</param>
    /// <param name="embedRecord">-r, The record to embed in the post.</param>
    /// <param name="embedRecordCid">-c, The CID of the record to embed in the post. Required if embedding record.</param>
    /// <param name="embeddedUrl">-e, The embedded URL for the post, data pulled from Open Graph. Does not need to be a URL included in the post text.</param>
    /// <param name="languages">-l, The languages represented in the post.</param>
    /// <param name="instanceUrl">-i, Instance URL.</param>
    /// <param name="verbose">-v, Verbose logging.</param>
    /// <param name="cancellationToken">Cancellation Token.</param>
    /// <returns>Task.</returns>
    [Command("post")]
    public async Task CreatePostAsync([Argument] string post, string username, string password, string? embedRecord = default, string? embedRecordCid = default, string? embeddedUrl = default, string[]? languages = default, string instanceUrl = "https://public.api.bsky.app", bool verbose = false, CancellationToken cancellationToken = default)
    {
        var consoleLog = new ConsoleLog(verbose);

        ATUri? atUri = null;
        if (!string.IsNullOrEmpty(embedRecord) && !ATUri.TryCreate(embedRecord, out atUri))
        {
            consoleLog.LogError("Invalid record URI for embedding.");
            return;
        }

        if (atUri is not null && string.IsNullOrEmpty(embedRecordCid))
        {
            consoleLog.LogError("CID is required for embedding a record.");
            return;
        }

        var atProtocol = this.GenerateProtocol(instanceUrl, consoleLog);

        if (await this.AuthenticateWithAppPasswordAsync(username, password, atProtocol, consoleLog) == false)
        {
            return;
        }

        EmbedExternal? embedExternal = null;
        if (!string.IsNullOrEmpty(embeddedUrl))
        {
            if (!Uri.TryCreate(embeddedUrl, UriKind.Absolute, out var uri))
            {
                consoleLog.LogError("Invalid embedded URL.");
                return;
            }
            else
            {
                embedExternal = await atProtocol.OpenGraphParser.GenerateEmbedExternal(uri.ToString());
                if (embedExternal == null)
                {
                    consoleLog.LogError("Failed to generate embed from URL.");
                    return;
                }
            }
        }

        var markdownPost = MarkdownPost.Parse(post);
        if (markdownPost is null)
        {
            consoleLog.LogError("Invalid post.");
            return;
        }

        languages ??= new string[] { };

        (var result, var error) = await atProtocol.Feed.CreatePostAsync(text: markdownPost.Post, facets: markdownPost.Facets, langs: languages.ToList(), embed: this.GenerateRecordWithMedia(atUri, embedExternal, embedRecordCid, consoleLog), cancellationToken: cancellationToken);
        if (error != null)
        {
            consoleLog.LogError(error.ToString());
            return;
        }

        consoleLog.Log($"Post created: {result!.Uri}.");
    }

    /// <summary>
    /// Resolve a Decentralized Identifier (DID) to its DID Document through plc.diretory.
    /// </summary>
    /// <param name="did">The DID.</param>
    /// <param name="instanceUrl">-i, Instance URL.</param>
    /// <param name="verbose">-v, Verbose logging.</param>
    /// <param name="cancellationToken">Cancellation Token.</param>
    /// <returns>Task.</returns>
    [Command("did-doc")]
    public async Task GetDidDocAsync([Argument] string did, string instanceUrl = "https://public.api.bsky.app", bool verbose = false, CancellationToken cancellationToken = default)
    {
        var consoleLog = new ConsoleLog(verbose);
        var atProtocol = this.GenerateProtocol(instanceUrl, consoleLog);
        if (!ATDid.TryCreate(did, out var atDid))
        {
            consoleLog.LogError("Invalid DID.");
            return;
        }

        (var result, var error) = await atProtocol.PlcDirectory.GetDidDocAsync(atDid!, cancellationToken);
        if (error != null)
        {
            consoleLog.LogError(error.ToString());
            return;
        }

        consoleLog.Log(result!.ToString());
    }

    /// <summary>
    /// Resolve a handle to its Decentralized Identifier (DID).
    /// </summary>
    /// <param name="handle">The handle to resolve.</param>
    /// <param name="instanceUrl">-i, Instance URL.</param>
    /// <param name="verbose">-v, Verbose logging.</param>
    /// <param name="cancellationToken">Cancellation Token.</param>
    /// <returns>Task.</returns>
    [Command("resolve-handle")]
    public async Task ResolveHandleAsync([Argument] string handle, string instanceUrl = "https://public.api.bsky.app", bool verbose = false, CancellationToken cancellationToken = default)
    {
        var consoleLog = new ConsoleLog(verbose);
        var atProtocol = this.GenerateProtocol(instanceUrl, consoleLog);
        if (!ATHandle.TryCreate(handle, out var atHandle))
        {
            consoleLog.LogError("Invalid handle.");
            return;
        }

        (var result, var error) = await atProtocol.Identity.ResolveHandleAsync(atHandle!, cancellationToken);
        if (error != null)
        {
            consoleLog.LogError(error.ToString());
            return;
        }

        consoleLog.Log(result!.Did!.ToString());
    }

    /// <summary>
    /// Create a new direct message.
    /// </summary>
    /// <param name="post">The post to create, can be written using a subset of markdown.</param>
    /// <param name="identifiers">-id, Identifier to send to.</param>
    /// <param name="username">-u, Username.</param>
    /// <param name="password">-p, Password.</param>
    /// <param name="embedRecord">-r, The record to embed in the post.</param>
    /// <param name="embedRecordCid">-c, The CID of the record to embed in the post. Required if embedding record.</param>
    /// <param name="instanceUrl">-i, Instance URL.</param>
    /// <param name="verbose">-v, Verbose logging.</param>
    /// <param name="cancellationToken">Cancellation Token.</param>
    /// <returns>Task.</returns>
    [Command("dm")]
    public async Task CreateDirectMessageAsync([Argument] string post, string[] identifiers, string username, string password, string? embedRecord = default, string? embedRecordCid = default,  string instanceUrl = "https://public.api.bsky.app", bool verbose = false, CancellationToken cancellationToken = default)
    {
        var consoleLog = new ConsoleLog(verbose);

        ATUri? atUri = null;
        if (!string.IsNullOrEmpty(embedRecord) && !ATUri.TryCreate(embedRecord, out atUri))
        {
            consoleLog.LogError("Invalid record URI for embedding.");
            return;
        }

        if (atUri is not null && string.IsNullOrEmpty(embedRecordCid))
        {
            consoleLog.LogError("CID is required for embedding a record.");
            return;
        }

        var atProtocol = this.GenerateProtocol(instanceUrl, consoleLog);

        if (await this.AuthenticateWithAppPasswordAsync(username, password, atProtocol, consoleLog) == false)
        {
            return;
        }

        var atDids = await this.GenerateATDidsAsync(identifiers, atProtocol, consoleLog, cancellationToken);

        if (atDids.Count == 0)
        {
            return;
        }

        var (convoForMembers, error) = await atProtocol.GetConvoForMembersAsync(atDids, cancellationToken);

        if (error != null)
        {
            consoleLog.LogError(error.ToString());
            return;
        }

        var convoId = convoForMembers?.Convo.Id ?? throw new Exception("Failed to get conversation id.");

        var mdPost = MarkdownPost.Parse(post);

        if (mdPost is null)
        {
            consoleLog.LogError("Failed to parse post.");
            return;
        }

        var messageInput = new MessageInput(mdPost.Post, mdPost.Facets, (EmbedRecord?)this.GenerateRecordWithMedia(atUri, null, embedRecordCid, consoleLog));
        (var convoResult, var convoError) = await atProtocol.SendMessageAsync(convoId, messageInput, cancellationToken);

        if (convoError != null)
        {
            consoleLog.LogError(convoError.ToString());
            return;
        }

        consoleLog.Log($"Message sent to {convoId}.");
    }

    /// <summary>
    /// Create a new random message.
    /// </summary>
    /// <param name="postTxtFilePath">Path to the text file of posts to pick from.</param>
    /// <param name="username">-u, Username.</param>
    /// <param name="password">-p, Password.</param>
    /// <param name="instanceUrl">-i, Instance URL.</param>
    /// <param name="verbose">-v, Verbose logging.</param>
    /// <param name="cancellationToken">Cancellation Token.</param>
    /// <returns>Task.</returns>
    [Command("random")]
    public async Task CreateRandomPostAsync([Argument] string postTxtFilePath, string username, string password, string instanceUrl = "https://public.api.bsky.app", bool verbose = false, CancellationToken cancellationToken = default)
    {
        var consoleLog = new ConsoleLog(verbose);

        if (!File.Exists(postTxtFilePath))
        {
            consoleLog.LogError("Post text file does not exist.");
            return;
        }

        var posts = File.ReadAllLines(postTxtFilePath);

        var atProtocol = this.GenerateProtocol(instanceUrl, consoleLog);

        if (await this.AuthenticateWithAppPasswordAsync(username, password, atProtocol, consoleLog) == false)
        {
            return;
        }

        var index = new Random().Next(0, posts.Length);
        var post = MarkdownPost.Parse(posts[index]);

        var (result, error) = await atProtocol.Feed.CreatePostAsync(post.Post, post.Facets, cancellationToken: cancellationToken);

        if (error is not null)
        {
            consoleLog.LogError($"Failed to create post: {error}");
            return;
        }

        consoleLog.Log($"Post created: {result!.Uri} - {result!.Cid}");
    }

    /// <summary>
    /// Download a repository from an identifier.
    /// </summary>
    /// <param name="identifier">The repo to download from.</param>
    /// <param name="outputName">-o, The file output name. Defaults to a generated value.</param>
    /// <param name="instanceUrl">-i, Instance URL.</param>
    /// <param name="verbose">-v, Verbose logging.</param>
    /// <param name="cancellationToken">Cancellation Token.</param>
    /// <returns>Task.</returns>
    [Command("download repo")]
    public async Task DownloadRepoAsync([Argument] string identifier, string? outputName = default, string instanceUrl = "https://public.api.bsky.app", bool verbose = false, CancellationToken cancellationToken = default)
    {
        var consoleLog = new ConsoleLog(verbose);
        var atProtocol = this.GenerateProtocol(instanceUrl, consoleLog);
        if (!ATIdentifier.TryCreate(identifier, out var atUri))
        {
            consoleLog.LogError("Invalid Identifier.");
            return;
        }

        if (string.IsNullOrEmpty(outputName))
        {
            outputName = $"{identifier}.repo";
        }

        var directory = Path.GetDirectoryName(outputName);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            directory = System.AppContext.BaseDirectory;
        }

        var outputPath = Path.Combine(directory ?? string.Empty, outputName);

        using var fileStream = File.OpenWrite(outputPath);

        var (result, error) = await atProtocol.DownloadRepoAsync(atUri, fileStream, cancellationToken: cancellationToken);
        if (error != null)
        {
            consoleLog.LogError(error.ToString());
            return;
        }

        if (result is null)
        {
            consoleLog.LogError("Failed to download repository.");
            return;
        }

        consoleLog.Log($"Downloaded repository to {outputName}.");
    }

    /// <summary>
    /// Download a blob from a repository.
    /// </summary>
    /// <param name="atDid">The repo to download from.</param>
    /// <param name="cid">The blob cid.</param>
    /// <param name="outputName">-o, The file output name. Defaults to a generated value.</param>
    /// <param name="instanceUrl">-i, Instance URL.</param>
    /// <param name="verbose">-v, Verbose logging.</param>
    /// <param name="cancellationToken">Cancellation Token.</param>
    /// <returns>Task.</returns>
    [Command("download blob")]
    public async Task DownloadBlobAsync([Argument] string atDid, [Argument] string cid, string? outputName = default, string instanceUrl = "https://public.api.bsky.app", bool verbose = false, CancellationToken cancellationToken = default)
    {
        var consoleLog = new ConsoleLog(verbose);
        var atProtocol = this.GenerateProtocol(instanceUrl, consoleLog);
        if (!ATDid.TryCreate(atDid, out var atUri))
        {
            consoleLog.LogError("Invalid DID.");
            return;
        }

        (var result, var error) = await atProtocol.Sync.GetBlobAsync(atUri!, cid, cancellationToken);
        if (error != null)
        {
            consoleLog.LogError(error.ToString());
            return;
        }

        var directory = Path.GetDirectoryName(outputName);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        outputName = outputName ?? $"{atDid}-{cid}.bin";

        var file = new FileInfo(outputName);
        await using var fileStream = file.OpenWrite();
        await fileStream.WriteAsync(result!, cancellationToken);
        consoleLog.Log($"Downloaded blob to {file.FullName}.");
    }

    /// <summary>
    /// Validate handle syntax.
    /// </summary>
    /// <param name="handle">The handle to validate.</param>
    /// <param name="verbose">-v, Verbose logging.</param>
    [Command("syntax handle")]
    public void ValidateHandleSyntax([Argument] string handle, bool verbose = false)
    {
        var consoleLog = new ConsoleLog(verbose);
        if (ATHandle.TryCreate(handle, out var atHandle))
        {
            consoleLog.Log("valid");
        }
        else
        {
            consoleLog.LogError($"error: handle syntax didn't validate");
        }
    }

    /// <summary>
    /// Validate DID syntax.
    /// </summary>
    /// <param name="did">The DID to validate.</param>
    /// <param name="verbose">-v, Verbose logging.</param>
    [Command("syntax did")]
    public void ValidateDidSyntax([Argument] string did, bool verbose = false)
    {
        var consoleLog = new ConsoleLog(verbose);
        if (ATDid.TryCreate(did, out var atDid))
        {
            consoleLog.Log("valid");
        }
        else
        {
            consoleLog.LogError($"error: DID syntax didn't validate");
        }
    }

    /// <summary>
    /// Validate AT-URI syntax.
    /// </summary>
    /// <param name="uri">The AT-URI to validate.</param>
    /// <param name="verbose">-v, Verbose logging.</param>
    [Command("syntax aturi")]
    public void ValidateAtUriSyntax([Argument] string uri, bool verbose = false)
    {
        var consoleLog = new ConsoleLog(verbose);
        if (ATUri.TryCreate(uri, out var atUri))
        {
            consoleLog.Log("valid");
        }
        else
        {
            consoleLog.LogError($"error: AT-URI syntax didn't validate");
        }
    }

    /// <summary>
    /// Validate a TID (Timestamp Identifier) syntax.
    /// </summary>
    /// <param name="tid">The TID to validate.</param>
    /// <param name="verbose">-v, Verbose logging.</param>
    [Command("syntax tid")]
    public void ValidateTidSyntax([Argument] string tid, bool verbose = false)
    {
        var consoleLog = new ConsoleLog(verbose);

        // TIDs are 13 characters of base32-sortable encoding (lowercase letters and digits 2-7)
        if (tid.Length == 13 && System.Text.RegularExpressions.Regex.IsMatch(tid, "^[a-z2-7]{13}$"))
        {
            consoleLog.Log("valid");
        }
        else
        {
            consoleLog.LogError($"error: TID syntax didn't validate");
        }
    }

    /// <summary>
    /// Fetch a record from the network as JSON.
    /// </summary>
    /// <param name="uri">The AT-URI of the record to fetch.</param>
    /// <param name="instanceUrl">-i, Instance URL.</param>
    /// <param name="verbose">-v, Verbose logging.</param>
    /// <param name="cancellationToken">Cancellation Token.</param>
    /// <returns>Task.</returns>
    [Command("get")]
    public async Task GetRecordAsync([Argument] string uri, string instanceUrl = "https://public.api.bsky.app", bool verbose = false, CancellationToken cancellationToken = default)
    {
        var consoleLog = new ConsoleLog(verbose);
        var atProtocol = this.GenerateProtocol(instanceUrl, consoleLog);

        if (!ATUri.TryCreate(uri, out var atUri))
        {
            consoleLog.LogError("Invalid AT-URI.");
            return;
        }

        // ATUri has both Did and Handle properties - one will be set based on the URI format
        ATDid? repoDid = atUri!.Did;
        if (repoDid == null && atUri.Handle != null)
        {
            // URI contains a handle, need to resolve it to DID
            (var resolveResult, var resolveError) = await atProtocol.Identity.ResolveHandleAsync(atUri.Handle, cancellationToken);
            if (resolveError != null)
            {
                consoleLog.LogError(resolveError.ToString());
                return;
            }

            repoDid = resolveResult?.Did;
        }

        if (repoDid == null)
        {
            consoleLog.LogError("Could not resolve repository identifier.");
            return;
        }

        (var result, var error) = await atProtocol.Repo.GetRecordAsync(repoDid, atUri.Collection!, atUri.Rkey!, cancellationToken: cancellationToken);
        if (error != null)
        {
            consoleLog.LogError(error.ToString());
            return;
        }

        if (result?.Value != null)
        {
            consoleLog.Log(result.Value.ToJson());
        }
        else
        {
            consoleLog.LogError("No record found.");
        }
    }

    /// <summary>
    /// List record collection types for an account.
    /// </summary>
    /// <param name="identifier">The identifier (handle or DID) of the account.</param>
    /// <param name="instanceUrl">-i, Instance URL.</param>
    /// <param name="verbose">-v, Verbose logging.</param>
    /// <param name="cancellationToken">Cancellation Token.</param>
    /// <returns>Task.</returns>
    [Command("list-collections")]
    public async Task ListCollectionsAsync([Argument] string identifier, string instanceUrl = "https://public.api.bsky.app", bool verbose = false, CancellationToken cancellationToken = default)
    {
        var consoleLog = new ConsoleLog(verbose);
        var atProtocol = this.GenerateProtocol(instanceUrl, consoleLog);

        if (!ATIdentifier.TryCreate(identifier, out var atIdentifier))
        {
            consoleLog.LogError("Invalid identifier.");
            return;
        }

        (var result, var error) = await atProtocol.Repo.DescribeRepoAsync(atIdentifier!, cancellationToken);
        if (error != null)
        {
            consoleLog.LogError(error.ToString());
            return;
        }

        if (result?.Collections != null)
        {
            foreach (var collection in result.Collections)
            {
                consoleLog.Log(collection);
            }
        }
        else
        {
            consoleLog.Log("No collections found.");
        }
    }

    /// <summary>
    /// List blob CIDs for an account.
    /// </summary>
    /// <param name="identifier">The identifier (handle or DID) of the account.</param>
    /// <param name="instanceUrl">-i, Instance URL.</param>
    /// <param name="verbose">-v, Verbose logging.</param>
    /// <param name="cancellationToken">Cancellation Token.</param>
    /// <returns>Task.</returns>
    [Command("list-blobs")]
    public async Task ListBlobsAsync([Argument] string identifier, string instanceUrl = "https://public.api.bsky.app", bool verbose = false, CancellationToken cancellationToken = default)
    {
        var consoleLog = new ConsoleLog(verbose);
        var atProtocol = this.GenerateProtocol(instanceUrl, consoleLog);

        if (!ATDid.TryCreate(identifier, out var atDid))
        {
            // Try to resolve as handle first
            if (ATHandle.TryCreate(identifier, out var atHandle))
            {
                (var resolveResult, var resolveError) = await atProtocol.Identity.ResolveHandleAsync(atHandle!, cancellationToken);
                if (resolveError != null)
                {
                    consoleLog.LogError(resolveError.ToString());
                    return;
                }

                atDid = resolveResult?.Did;
            }
            else
            {
                consoleLog.LogError("Invalid identifier.");
                return;
            }
        }

        (var result, var error) = await atProtocol.Sync.ListBlobsAsync(atDid!, cancellationToken: cancellationToken);
        if (error != null)
        {
            consoleLog.LogError(error.ToString());
            return;
        }

        if (result?.Cids != null)
        {
            foreach (var cid in result.Cids)
            {
                consoleLog.Log(cid);
            }
        }
        else
        {
            consoleLog.Log("No blobs found.");
        }
    }

    /// <summary>
    /// Get repository status information.
    /// </summary>
    /// <param name="identifier">The identifier (handle or DID) of the account.</param>
    /// <param name="instanceUrl">-i, Instance URL.</param>
    /// <param name="verbose">-v, Verbose logging.</param>
    /// <param name="cancellationToken">Cancellation Token.</param>
    /// <returns>Task.</returns>
    [Command("repo-status")]
    public async Task GetRepoStatusAsync([Argument] string identifier, string instanceUrl = "https://public.api.bsky.app", bool verbose = false, CancellationToken cancellationToken = default)
    {
        var consoleLog = new ConsoleLog(verbose);
        var atProtocol = this.GenerateProtocol(instanceUrl, consoleLog);

        if (!ATDid.TryCreate(identifier, out var atDid))
        {
            // Try to resolve as handle first
            if (ATHandle.TryCreate(identifier, out var atHandle))
            {
                (var resolveResult, var resolveError) = await atProtocol.Identity.ResolveHandleAsync(atHandle!, cancellationToken);
                if (resolveError != null)
                {
                    consoleLog.LogError(resolveError.ToString());
                    return;
                }

                atDid = resolveResult?.Did;
            }
            else
            {
                consoleLog.LogError("Invalid identifier.");
                return;
            }
        }

        (var latestCommit, var commitError) = await atProtocol.Sync.GetLatestCommitAsync(atDid!, cancellationToken);
        if (commitError != null)
        {
            consoleLog.LogError(commitError.ToString());
            return;
        }

        if (latestCommit != null)
        {
            consoleLog.Log($"DID: {atDid}");
            consoleLog.Log($"CID: {latestCommit.Cid}");
            consoleLog.Log($"Rev: {latestCommit.Rev}");
        }
        else
        {
            consoleLog.LogError("Could not retrieve repo status.");
        }
    }

    /// <summary>
    /// Describe the server/PDS.
    /// </summary>
    /// <param name="instanceUrl">-i, Instance URL.</param>
    /// <param name="verbose">-v, Verbose logging.</param>
    /// <param name="cancellationToken">Cancellation Token.</param>
    /// <returns>Task.</returns>
    [Command("describe-server")]
    public async Task DescribeServerAsync(string instanceUrl = "https://public.api.bsky.app", bool verbose = false, CancellationToken cancellationToken = default)
    {
        var consoleLog = new ConsoleLog(verbose);
        var atProtocol = this.GenerateProtocol(instanceUrl, consoleLog);

        (var result, var error) = await atProtocol.Server.DescribeServerAsync(cancellationToken);
        if (error != null)
        {
            consoleLog.LogError(error.ToString());
            return;
        }

        if (result != null)
        {
            consoleLog.Log(result.ToJson());
        }
        else
        {
            consoleLog.LogError("Could not describe server.");
        }
    }

    private async Task<bool> AuthenticateWithAppPasswordAsync(string username, string password, ATProtocol atProtocol, ConsoleLog consoleLog)
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            consoleLog.LogError("Username and password are required.");
            return false;
        }

        var (result, error) = await atProtocol.AuthenticateWithPasswordResultAsync(username, password);
        if (result is null)
        {
            consoleLog.LogError($"Failed to authenticate as {username}.");
            return false;
        }

        consoleLog.Log($"Authenticated as {username}.");
        return true;
    }

    private ATProtocol GenerateProtocol(string instanceUrl, ConsoleLog consoleLog)
    {
        var atProtocolBuilder = new ATProtocolBuilder();
        if (!Uri.TryCreate(instanceUrl, UriKind.Absolute, out var instanceUri))
        {
            consoleLog.LogError("Invalid instance URL.");
        }
        else
        {
            atProtocolBuilder.WithInstanceUrl(instanceUri);
        }

        if (consoleLog.IsVerbose)
        {
            atProtocolBuilder.WithLogger(consoleLog.Logger);
        }
        else
        {
            atProtocolBuilder.WithLogger(LoggerFactory.Create(builder => { builder.AddDebug(); }).CreateLogger("protocol"));
        }

        var atProtocol = atProtocolBuilder.Build();
        return atProtocol;
    }

    private (int Width, int Height) GetDimensions(string videoPath)
    {
        using var process = new Process();
        process.StartInfo.FileName = "ffprobe";
        process.StartInfo.Arguments = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=s=x:p=0 {videoPath}";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.CreateNoWindow = true;
        process.Start();
        process.WaitForExit(1000);
        var output = process.StandardOutput.ReadToEnd();
        var dimensions = output.Split('x');
        if (dimensions.Length != 2)
        {
            return (0, 0);
        }

        return (int.Parse(dimensions[0]), int.Parse(dimensions[1]));
    }

    private bool IsFFmpegInstalled()
    {
        try
        {
            using (var process = new Process())
            {
                process.StartInfo.FileName = "ffmpeg";
                process.StartInfo.Arguments = "-version";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                process.WaitForExit(1000);
                return process.ExitCode == 0;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool IsFFprobeInstalled()
    {
        try
        {
            using (var process = new Process())
            {
                process.StartInfo.FileName = "ffprobe";
                process.StartInfo.Arguments = "-version";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                process.WaitForExit(1000);
                return process.ExitCode == 0;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<Caption?> UploadCaptionAsync(string vttPath, string language, ATProtocol protocol, ConsoleLog consoleLog)
    {
        using var vttStream = File.OpenRead(vttPath);
        var vttContentStream = new StreamContent(vttStream);
        vttContentStream.Headers.ContentLength = vttStream.Length;
        vttContentStream.Headers.ContentType = new MediaTypeHeaderValue("text/vtt");

        (var vttResult, var vttError) = await protocol.Repo.UploadBlobAsync(vttContentStream);
        if (vttError != null)
        {
            consoleLog.LogError(vttError.ToString());
            return null;
        }

        consoleLog.Log($"Uploaded VTT file for {language}.");
        return new Caption(lang: language, file: vttResult!.Blob);
    }

    private ATObject? GenerateRecordWithMedia(ATUri? atUri, ATObject? embed, string? cid, ConsoleLog consoleLog)
    {
        if (atUri is null)
        {
            return embed;
        }

        cid ??= string.Empty;

        var embedRecord = new EmbedRecord(new FishyFlip.Lexicon.Com.Atproto.Repo.StrongRef(atUri!, cid));

        if (embed is not null)
        {
            consoleLog.Log($"Embedding record {atUri} with {embed.GetType()}.");
            return new RecordWithMedia(embedRecord, embed);
        }

        consoleLog.Log($"Embedding record {atUri}.");

        return embedRecord;
    }

    private async Task<List<ATDid>> GenerateATDidsAsync(string[] identifiers, ATProtocol atProtocol, ConsoleLog consoleLog, CancellationToken cancellationToken)
    {
        var atDids = new List<ATDid>();
        foreach (var identifier in identifiers)
        {
            if (!ATIdentifier.TryCreate(identifier, out var atIdentifier))
            {
                consoleLog.LogError("Invalid identifier.");
                return new List<ATDid>();
            }

            if (atIdentifier is ATDid atDid)
            {
                atDids.Add(atDid);
            }
            else if (atIdentifier is ATHandle handle)
            {
                (var handleResult, var handleError) = await atProtocol.Identity.ResolveHandleAsync(handle!, cancellationToken);
                if (handleError != null)
                {
                    consoleLog.LogError(handleError.ToString());
                    return new List<ATDid>();
                }

                atDids.Add(handleResult?.Did ?? throw new Exception("Failed to resolve handle."));
            }
            else
            {
                consoleLog.LogError("Invalid identifier.");
                return new List<ATDid>();
            }
        }

        return atDids;
    }

    private class FileContentTypeDetector
    {
        private (byte[], string)[] fileSignatures;
        private ILogger logger;

        public FileContentTypeDetector(ILogger logger)
        {
            this.logger = logger;
            this.fileSignatures = new[]
            {
                (new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg"), // JPEG
                (new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png"), // PNG
                ("GIF8"u8.ToArray(), "image/gif"), // GIF
                (new byte[] { 0x52, 0x49, 0x46, 0x46 }, "image/webp"), // WebP
            };
        }

        public string GetContentType(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return "unsupported";
            }

            return this.GetContentType(new MemoryStream(bytes));
        }

        public string GetContentType(Stream? fileStream)
        {
            if (fileStream == null || fileStream.Length == 0)
            {
                return "unsupported";
            }

            long originalPosition = fileStream.Position;
            fileStream.Position = 0;

            foreach (var (signature, mimeType) in this.fileSignatures)
            {
                var buffer = new byte[signature.Length];
                if (fileStream.Read(buffer, 0, signature.Length) == signature.Length)
                {
                    if (signature.SequenceEqual(buffer))
                    {
                        fileStream.Position = originalPosition;
                        this.logger.LogDebug($"Detected file type: {mimeType}");
                        return mimeType;
                    }
                }

                fileStream.Position = 0;
            }

            fileStream.Position = originalPosition;
            this.logger.LogDebug("Unsupported file type.");
            return "unsupported";
        }
    }
}
