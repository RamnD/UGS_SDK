using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Posts analytics batches to game-services gateway via <see cref="UnityWebRequest"/>.
/// </summary>
public sealed class UnityWebRequestRamndTransport : IRamndAnalyticsTransport
{
    readonly string _ingestUrl;
    readonly string _apiKey;

    public UnityWebRequestRamndTransport(RamndAnalyticsConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));
        _ingestUrl = config.BaseUrl + "/v1/analytics/events";
        _apiKey = config.ApiKey;
    }

    public async Task<RamndIngestResult> PostEventsAsync(
        string jsonBody,
        CancellationToken cancellationToken = default)
    {
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody ?? "{}");

        using var request = new UnityWebRequest(_ingestUrl, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("X-Api-Key", _apiKey);
        request.timeout = 30;

        UnityWebRequestAsyncOperation op = request.SendWebRequest();
        while (!op.isDone)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }

#if UNITY_2020_1_OR_NEWER
        bool networkError = request.result == UnityWebRequest.Result.ConnectionError
            || request.result == UnityWebRequest.Result.DataProcessingError;
#else
        bool networkError = request.isNetworkError;
#endif
        if (networkError)
            throw new InvalidOperationException($"Ramnd analytics transport error: {request.error}");

        return new RamndIngestResult((int)request.responseCode, request.downloadHandler?.text);
    }
}
