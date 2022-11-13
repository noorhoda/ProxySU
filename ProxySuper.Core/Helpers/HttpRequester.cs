using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Newtonsoft.Json;
using ProxySuper.Core.Models.Http;

namespace ProxySuper.Core.Helpers
{
    public class HttpRequester
    {
        private static readonly HttpClient _httpClient;

        static HttpRequester()
        {
            _httpClient = HttpClientUtil.CreateHttpClient(10000);
        }


        public static List<string> CoreVersionList { get; set; }
        public static void InitXrayCoreVersionList()
        {
            var httpRequestMessage =
                new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/XTLS/Xray-core/releases");
            httpRequestMessage.Headers.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/107.0.0.0 Safari/537.36 Edg/107.0.1418.42");
            var response = _httpClient.SendAsync(httpRequestMessage).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                CoreVersionList = new List<string> { "版本拉取失败，请手动填写" };
            }

            CoreVersionList = JsonConvert
                .DeserializeObject<List<XrayVersionResponse>>(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())
                ?.Select(e => $"{e.Version}.{(e.IsPreRelease ? "[PRE]" : string.Empty)}")
                .ToList();
        }
    }
}