// <copyright file="SessionData.cs" company="Drastic Actions">
// Copyright (c) Drastic Actions. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bskycli;
using ConsoleAppFramework;
using FishyFlip;
using FishyFlip.Events;
using FishyFlip.Lexicon;
using FishyFlip.Lexicon.App.Bsky.Embed;
using FishyFlip.Lexicon.Chat.Bsky.Convo;
using FishyFlip.Models;
using FishyFlip.Tools;
using Microsoft.Extensions.Logging;
using File = System.IO.File;

/// <summary>
/// Session data for persistent login.
/// </summary>
public class SessionData
{
    /// <summary>
    /// Gets or sets the DID.
    /// </summary>
    public string? Did { get; set; }

    /// <summary>
    /// Gets or sets the handle.
    /// </summary>
    public string? Handle { get; set; }

    /// <summary>
    /// Gets or sets the access JWT.
    /// </summary>
    public string? AccessJwt { get; set; }

    /// <summary>
    /// Gets or sets the refresh JWT.
    /// </summary>
    public string? RefreshJwt { get; set; }

    /// <summary>
    /// Gets or sets the instance URL.
    /// </summary>
    public string? InstanceUrl { get; set; }

    /// <summary>
    /// Gets or sets the DID document JSON.
    /// </summary>
    public DidDoc? DidDoc { get; set; }
}
