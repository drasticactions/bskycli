// <copyright file="SessionDataJsonContext.cs" company="Drastic Actions">
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
/// Source generation context for JSON serialization (NativeAOT compatible).
/// </summary>
[JsonSerializable(typeof(SessionData))]
internal partial class SessionDataJsonContext : JsonSerializerContext
{
}
