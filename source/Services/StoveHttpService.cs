using Playnite.SDK;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RateLimiter;

namespace StoveLibrary.Services
{
    public class StoveHttpService : IDisposable
    {
        private readonly ILogger logger = LogManager.GetLogger();
        private readonly HttpClient httpClient;
        private readonly TimeLimiter rateLimiter;
        private bool disposed = false;

        public StoveHttpService()
        {
            httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("X-Lang", "en");
            httpClient.DefaultRequestHeaders.Add("X-Nation", "US");
            httpClient.DefaultRequestHeaders.Add("X-Device-Type", "P01");
            httpClient.DefaultRequestHeaders.Add("X-Timezone", "America/Los_Angeles");
            httpClient.DefaultRequestHeaders.Add("X-Utc-Offset", "-420");
            httpClient.DefaultRequestHeaders.Add("Origin", "https://store.onstove.com");
            httpClient.DefaultRequestHeaders.Add("Referer", "https://store.onstove.com/");
            httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.6998.205 Safari/537.36");

            httpClient.DefaultRequestHeaders.Accept.Clear();
            httpClient.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            rateLimiter = TimeLimiter.GetFromMaxCountByInterval(10, TimeSpan.FromSeconds(1));
        }

        public async Task<HttpResponseMessage> GetAsync(string requestUri, CancellationToken cancellationToken = default)
        {
            return await rateLimiter.Enqueue(async () =>
            {
                return await httpClient.GetAsync(requestUri, cancellationToken);
            }, cancellationToken);
        }

        public async Task<HttpResponseMessage> GetAsync(string requestUri, string authToken, CancellationToken cancellationToken = default)
        {
            return await rateLimiter.Enqueue(async () =>
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, requestUri))
                {
                    if (!string.IsNullOrEmpty(authToken))
                    {
                        request.Headers.Add("Authorization", $"Bearer {authToken}");
                    }
                    return await httpClient.SendAsync(request, cancellationToken);
                }
            }, cancellationToken);
        }

        public void Dispose()
        {
            if (!disposed)
            {
                httpClient?.Dispose();
                disposed = true;
            }
        }
    }
}