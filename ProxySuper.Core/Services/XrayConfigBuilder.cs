﻿using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProxySuper.Core.Models.Projects;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ProxySuper.Core.Services
{
    public class XrayConfigBuilder
    {
        private const string ServerLogDir = @"Templates\xray\server\00_log";
        private const string ServerApiDir = @"Templates\xray\server\01_api";
        private const string ServerDnsDir = @"Templates\xray\server\02_dns";
        private const string ServerRoutingDir = @"Templates\xray\server\03_routing";
        private const string ServerPolicyDir = @"Templates\xray\server\04_policy";
        private const string ServerInboundsDir = @"Templates\xray\server\05_inbounds";
        private const string ServerOutboundsDir = @"Templates\xray\server\06_outbounds";
        private const string ServerTransportDir = @"Templates\xray\server\07_transport";
        private const string ServerStatsDir = @"Templates\xray\server\08_stats";
        private const string ServerReverseDir = @"Templates\xray\server\09_reverse";
        private const string CaddyFileDir = @"Templates\xray\caddy";

        public static int VLESS_TCP_Port = 1110;
        public static int VLESS_WS_Port = 1111;
        public static int VLESS_H2_Port = 1112;

        public static int VMESS_TCP_Port = 1210;
        public static int VMESS_WS_Port = 1211;
        public static int VMESS_H2_Port = 1212;

        public static int Trojan_TCP_Port = 1310;
        public static int Trojan_WS_Port = 1311;

        public static int FullbackPort = 8080;



        public static dynamic LoadXrayConfig()
        {
            dynamic logObj = LoadJsonObj(Path.Combine(ServerLogDir, "00_log.json"));
            dynamic apiObj = LoadJsonObj(Path.Combine(ServerApiDir, "01_api.json"));
            dynamic dnsObj = LoadJsonObj(Path.Combine(ServerDnsDir, "02_dns.json"));
            dynamic routingObj = LoadJsonObj(Path.Combine(ServerRoutingDir, "03_routing.json"));
            dynamic policyObj = LoadJsonObj(Path.Combine(ServerPolicyDir, "04_policy.json"));
            dynamic inboundsObj = LoadJsonObj(Path.Combine(ServerInboundsDir, "05_inbounds.json"));
            dynamic outboundsObj = LoadJsonObj(Path.Combine(ServerOutboundsDir, "06_outbounds.json"));
            dynamic transportObj = LoadJsonObj(Path.Combine(ServerTransportDir, "07_transport.json"));
            dynamic statsObj = LoadJsonObj(Path.Combine(ServerStatsDir, "08_stats.json"));
            dynamic reverseObj = LoadJsonObj(Path.Combine(ServerReverseDir, "09_reverse.json"));

            return new
            {
                log = logObj["log"],
                //api = apiObj["api"],  api不能为空
                dns = dnsObj["dns"],
                routing = routingObj["routing"],
                policy = policyObj["policy"],
                inbounds = inboundsObj["inbounds"],
                outbounds = outboundsObj["outbounds"],
                transport = transportObj["transport"],
                stats = statsObj["stats"],
                reverse = reverseObj["reverse"]
            };
        }

        public static string BuildCaddyConfig(XraySettings parameters, bool useCustomWeb = false)
        {
            var caddyStr = File.ReadAllText(Path.Combine(CaddyFileDir, "base.caddyfile"));
            caddyStr = caddyStr.Replace("##domain##", parameters.IsIPAddress ? "" : parameters.Domain);
            caddyStr = caddyStr.Replace("##port##", FullbackPort.ToString());

            if (!useCustomWeb && !string.IsNullOrEmpty(parameters.MaskDomain))
            {
                var prefix = "http://";
                if (parameters.MaskDomain.StartsWith("https://"))
                {
                    prefix = "https://";
                }
                var domain = parameters.MaskDomain
                    .TrimStart("http://".ToCharArray())
                    .TrimStart("https://".ToCharArray());

                caddyStr = caddyStr.Replace("##reverse_proxy##", $"reverse_proxy {prefix}{domain} {{ \n        header_up Host {domain} \n    }}");
            }
            else
            {
                caddyStr = caddyStr.Replace("##reverse_proxy##", "");
            }

            return caddyStr;
        }

        private static void SetClients(dynamic bound, List<string> uuidList, bool withXtls = false, string flow = "xtls-rprx-direct")
        {
            bound.settings.clients.Clear();
            uuidList.ForEach(id =>
            {
                object obj;
                if (!withXtls)
                {
                    obj = new { id };
                }
                else
                {
                    obj = new { id, flow };
                }

                bound.settings.clients.Add(JToken.FromObject(obj));
            });
        }

        /// <summary>
        /// https://github.com/XTLS/Xray-core/releases/tag/v1.6.2.
        /// </summary>
        /// <param name="version"></param>
        /// <returns></returns>
        private static bool CheckVersionForVision(string version)
        {
            var arr = version
                ?.Replace("v", string.Empty)
                .Replace("[PRE]", string.Empty)
                .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (arr == null || arr.Count <= 2)
            {
                return false;
            }

            if (!int.TryParse(arr[0], out var version1) && version1 < 1)
            {
                return false;
            }

            if (!int.TryParse(arr[1], out var version2) && version2 < 6)
            {
                return false;
            }

            if (!int.TryParse(arr[2], out var version3) && version3 < 2)
            {
                return false;
            }

            return true;
        }

        public static string BuildXrayConfig(XraySettings parameters)
        {
            var uuidList = new List<string>();
            uuidList.Add(parameters.UUID);
            uuidList.AddRange(parameters.MulitUUID);

            var xrayConfig = LoadXrayConfig();

            var isVision = parameters.XTlSFlowOption?.ToLower().Contains("vision") == true && CheckVersionForVision(parameters.XrayVersionOption);
            var baseBound = isVision ? GetBound("VLESS_TCP_XTLS_Vision.json") : GetBound("VLESS_TCP_XTLS.json");

            baseBound.port = parameters.Port;
            baseBound.settings.fallbacks.Add(JToken.FromObject(new
            {
                dest = FullbackPort
            }));
            xrayConfig.inbounds.Add(baseBound);
            SetClients(baseBound, uuidList, withXtls: true, flow: parameters.XTlSFlowOption);

            #region Fullbacks

            if (parameters.Types.Contains(RayType.VLESS_WS))
            {
                var wsInbound = GetBound("VLESS_WS.json");
                wsInbound.port = VLESS_WS_Port;
                SetClients(wsInbound, uuidList);
                wsInbound.streamSettings.wsSettings.path = parameters.VLESS_WS_Path;
                baseBound.settings.fallbacks.Add(JToken.FromObject(new
                {
                    dest = VLESS_WS_Port,
                    path = parameters.VLESS_WS_Path,
                    xver = 1,
                }));
                xrayConfig.inbounds.Add(JToken.FromObject(wsInbound));
            }

            if (parameters.Types.Contains(RayType.VMESS_TCP))
            {
                var mtcpBound = GetBound("VMESS_TCP.json");
                mtcpBound.port = VMESS_TCP_Port;
                SetClients(mtcpBound, uuidList);
                mtcpBound.streamSettings.tcpSettings.header.request.path = parameters.VMESS_TCP_Path;
                baseBound.settings.fallbacks.Add(JToken.FromObject(new
                {
                    dest = VMESS_TCP_Port,
                    path = parameters.VMESS_TCP_Path,
                    xver = 1,
                }));
                xrayConfig.inbounds.Add(JToken.FromObject(mtcpBound));
            }

            if (parameters.Types.Contains(RayType.VMESS_WS))
            {
                var mwsBound = GetBound("VMESS_WS.json");
                mwsBound.port = VMESS_WS_Port;
                SetClients(mwsBound, uuidList);
                mwsBound.streamSettings.wsSettings.path = parameters.VMESS_WS_Path;
                baseBound.settings.fallbacks.Add(JToken.FromObject(new
                {
                    dest = VMESS_WS_Port,
                    path = parameters.VMESS_WS_Path,
                    xver = 1,
                }));
                xrayConfig.inbounds.Add(JToken.FromObject(mwsBound));
            }

            if (parameters.Types.Contains(RayType.Trojan_TCP))
            {
                var trojanTcpBound = GetBound("Trojan_TCP.json");
                trojanTcpBound.port = Trojan_TCP_Port;
                trojanTcpBound.settings.clients[0].password = parameters.TrojanPassword;
                trojanTcpBound.settings.fallbacks[0].dest = FullbackPort;
                baseBound.settings.fallbacks[0] = JToken.FromObject(new
                {
                    dest = Trojan_TCP_Port,
                    xver = 1,
                });
                xrayConfig.inbounds.Add(JToken.FromObject(trojanTcpBound));
            }
            #endregion

            if (parameters.Types.Contains(RayType.VLESS_gRPC))
            {
                var gRPCInBound = GetBound("VLESS_gRPC.json");
                gRPCInBound.port = parameters.VLESS_gRPC_Port;
                SetClients(gRPCInBound, uuidList);
                gRPCInBound.streamSettings.grpcSettings.serviceName = parameters.VLESS_gRPC_ServiceName;
                gRPCInBound.streamSettings.tlsSettings.serverName = parameters.Domain;
                xrayConfig.inbounds.Add(JToken.FromObject(gRPCInBound));
            }

            if (parameters.Types.Contains(RayType.VLESS_KCP))
            {
                var kcpBound = GetBound("VLESS_KCP.json");
                kcpBound.port = parameters.VLESS_KCP_Port;
                SetClients(kcpBound, uuidList);
                kcpBound.streamSettings.kcpSettings.header.type = parameters.VLESS_KCP_Type;
                kcpBound.streamSettings.kcpSettings.seed = parameters.VLESS_KCP_Seed;
                xrayConfig.inbounds.Add(JToken.FromObject(kcpBound));
            }

            if (parameters.Types.Contains(RayType.VMESS_KCP))
            {
                var kcpBound = GetBound("VMESS_KCP.json");
                kcpBound.port = parameters.VMESS_KCP_Port;
                SetClients(kcpBound, uuidList);
                kcpBound.streamSettings.kcpSettings.header.type = parameters.VMESS_KCP_Type;
                kcpBound.streamSettings.kcpSettings.seed = parameters.VMESS_KCP_Seed;
                xrayConfig.inbounds.Add(JToken.FromObject(kcpBound));
            }

            if (parameters.Types.Contains(RayType.ShadowsocksAEAD))
            {
                var ssBound = GetBound("Shadowsocks-AEAD.json");
                ssBound.port = parameters.ShadowSocksPort;
                ssBound.settings.password = parameters.ShadowSocksPassword;
                ssBound.settings.method = parameters.ShadowSocksMethod;
                xrayConfig.inbounds.Add(JToken.FromObject(ssBound));
            }

            return JsonConvert.SerializeObject(
                xrayConfig,
                Formatting.Indented,
                new JsonSerializerSettings()
                {
                    NullValueHandling = NullValueHandling.Ignore
                });
        }

        private static dynamic GetBound(string name)
        {
            return LoadJsonObj(Path.Combine(ServerInboundsDir, name));
        }

        private static dynamic LoadJsonObj(string path)
        {
            if (File.Exists(path))
            {
                var jsonStr = File.ReadAllText(path, Encoding.UTF8);
                return JToken.FromObject(JsonConvert.DeserializeObject(jsonStr));
            }
            return null;
        }

    }

}
