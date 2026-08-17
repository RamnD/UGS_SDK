using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Thin logging façade. Levels gated by <see cref="MinLevel"/> from <c>UGS_ENV_*</c>.
/// Format: <c>[Tag] message</c>. Optional session time / player id via flags — not on every Verbose.
/// Does not open UI or report service faults — call sites stay explicit.
/// Stack traces skip this façade via <see cref="HideInCallstackAttribute"/>.
/// </summary>
public static class AppLog
{
    static readonly StringBuilder _sb = new(128);
    static float _sessionStartRealtime;
    static string _playerId;
    static bool _configured;

    public static AppLogLevel MinLevel { get; set; } = AppLogLevel.Debug;

    /// <summary>When true, Info+ lines may include <c>t+12.3s</c> from session start.</summary>
    public static bool IncludeSessionTime { get; set; }

    /// <summary>When true, Info+ lines may include <c>pid=…</c> / <c>pid=anon</c>.</summary>
    public static bool IncludePlayerId { get; set; }

    public static string PlayerId =>
        string.IsNullOrWhiteSpace(_playerId) ? "anon" : _playerId;

    /// <summary>
    /// Apply defaults from scripting defines.
    /// Production → Warning; Staging → Info; Development / unset → Verbose.
    /// </summary>
    public static void ConfigureFromEnvironment()
    {
        _sessionStartRealtime = Time.realtimeSinceStartup;

#if UGS_ENV_PRODUCTION
        MinLevel = AppLogLevel.Warning;
        IncludeSessionTime = false;
        IncludePlayerId = false;
#elif UGS_ENV_STAGING
        MinLevel = AppLogLevel.Info;
        IncludeSessionTime = false;
        IncludePlayerId = true;
#elif UGS_ENV_DEVELOPMENT
        MinLevel = AppLogLevel.Verbose;
        IncludeSessionTime = true;
        IncludePlayerId = true;
#else
        MinLevel = AppLogLevel.Verbose;
        IncludeSessionTime = true;
        IncludePlayerId = true;
#endif

        _configured = true;
        Info("AppLog", $"configured minLevel={MinLevel}");
    }

    public static void SetPlayerId(string playerId)
    {
        _playerId = string.IsNullOrWhiteSpace(playerId) ? null : playerId.Trim();
        AppDiagnostics.SetPlayerId(_playerId);
    }

    public static void ClearPlayerId()
    {
        _playerId = null;
        AppDiagnostics.ClearPlayerId();
    }

    public static bool IsEnabled(AppLogLevel level) =>
        level >= MinLevel;

    [HideInCallstack]
    [Conditional("UNITY_EDITOR")]
    [Conditional("DEVELOPMENT_BUILD")]
    [Conditional("UGS_ENV_DEVELOPMENT")]
    [Conditional("UGS_ENV_STAGING")]
    public static void Verbose(string tag, string message, UnityEngine.Object context = null)
    {
        EnsureConfigured();
        if (!IsEnabled(AppLogLevel.Verbose))
            return;
        Debug.Log(Format(AppLogLevel.Verbose, tag, message), context);
    }

    [HideInCallstack]
    [Conditional("UNITY_EDITOR")]
    [Conditional("DEVELOPMENT_BUILD")]
    [Conditional("UGS_ENV_DEVELOPMENT")]
    [Conditional("UGS_ENV_STAGING")]
    public static void DebugLog(string tag, string message, UnityEngine.Object context = null)
    {
        EnsureConfigured();
        if (!IsEnabled(AppLogLevel.Debug))
            return;
        Debug.Log(Format(AppLogLevel.Debug, tag, message), context);
    }

    [HideInCallstack]
    public static void Info(string tag, string message, UnityEngine.Object context = null)
    {
        EnsureConfigured();
        if (!IsEnabled(AppLogLevel.Info))
            return;
        Debug.Log(Format(AppLogLevel.Info, tag, message), context);
    }

    [HideInCallstack]
    public static void Warn(string tag, string message, UnityEngine.Object context = null)
    {
        EnsureConfigured();
        if (!IsEnabled(AppLogLevel.Warning))
            return;
        Debug.LogWarning(Format(AppLogLevel.Warning, tag, message), context);
    }

    [HideInCallstack]
    public static void Error(string tag, string message, UnityEngine.Object context = null)
    {
        EnsureConfigured();
        if (!IsEnabled(AppLogLevel.Error))
            return;
        Debug.LogError(Format(AppLogLevel.Error, tag, message), context);
    }

    [HideInCallstack]
    public static void Error(string tag, Exception exception, string message = null, UnityEngine.Object context = null)
    {
        EnsureConfigured();
        if (!IsEnabled(AppLogLevel.Error))
            return;

        string body = string.IsNullOrWhiteSpace(message)
            ? exception?.Message
            : message;
        string formatted = Format(AppLogLevel.Error, tag, body ?? "exception");
        if (exception != null)
            Debug.LogException(exception, context);
        Debug.LogError(formatted, context);
    }

    [HideInCallstack]
    static void EnsureConfigured()
    {
        if (_configured)
            return;
        ConfigureFromEnvironment();
    }

    [HideInCallstack]
    static string Format(AppLogLevel level, string tag, string message)
    {
        string safeTag = string.IsNullOrWhiteSpace(tag) ? "App" : tag.Trim();
        string safeMessage = message ?? string.Empty;

        bool enrich = level >= AppLogLevel.Info
                      && (IncludeSessionTime || IncludePlayerId);

        if (!enrich)
            return $"[{safeTag}] {safeMessage}";

        _sb.Clear();
        _sb.Append('[').Append(safeTag).Append("] ").Append(safeMessage);

        if (IncludeSessionTime)
        {
            float t = Mathf.Max(0f, Time.realtimeSinceStartup - _sessionStartRealtime);
            _sb.Append(" t+").Append(t.ToString("0.0")).Append('s');
        }

        if (IncludePlayerId)
            _sb.Append(" pid=").Append(PlayerId);

        return _sb.ToString();
    }
}
