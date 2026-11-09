using System.Net.Http;

namespace HexIDE.Tests;

internal static class DownloadRetry
{
    internal static void ToFile(HttpClient http, string url, string path, TimeSpan timeout) =>
        Run(token =>
        {
            using var response = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token)
                .GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            using var target = File.Create(path);
            response.Content.CopyToAsync(target, token).GetAwaiter().GetResult();
            return true;
        }, timeout);

    internal static T Run<T>(Func<CancellationToken, T> fetch, TimeSpan timeout)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var deadline = new CancellationTokenSource(timeout);
            try { return fetch(deadline.Token); }
            catch (Exception error) when (attempt < 3 && IsTransient(error))
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(250 * attempt));
            }
        }
    }

    private static bool IsTransient(Exception error) => error switch
    {
        HttpRequestException { StatusCode: { } status } => (int)status is >= 500 and <= 599,
        HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.ResponseEnded } => true,
        HttpIOException { HttpRequestError: HttpRequestError.ResponseEnded } => true,
        HttpRequestException { StatusCode: null, InnerException: HttpIOException bodyError } => IsTransient(bodyError),
        OperationCanceledException => true,
        _ => false,
    };
}
