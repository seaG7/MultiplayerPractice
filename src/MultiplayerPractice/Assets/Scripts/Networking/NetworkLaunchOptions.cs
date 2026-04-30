using System;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace Networking
{
    public static class NetworkLaunchOptions
    {
        public const ushort DefaultPort = 7770;

        public static string GetAddress()
        {
            string value = GetOption("-address", "-connect", "-ip");
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        public static ushort GetPort(ushort fallback = DefaultPort)
        {
            string value = GetOption("-port");
            return ushort.TryParse(value, out ushort port) ? port : fallback;
        }

        public static bool TryGetLatency(out long latencyMs)
        {
            string value = GetOption("-latency", "-lag");
            return long.TryParse(value, out latencyMs) && latencyMs > 0;
        }

        public static void Apply(NetworkManager networkManager, bool applyAddress, string addressOverride = null, ushort? portOverride = null)
        {
            if (networkManager == null || networkManager.TransportManager == null)
            {
                return;
            }

            Transport transport = networkManager.TransportManager.Transport;
            if (transport == null)
            {
                return;
            }

            ushort port = portOverride ?? GetPort();
            transport.SetPort(port);

            string address = string.IsNullOrWhiteSpace(addressOverride) ? GetAddress() : addressOverride.Trim();
            if (applyAddress && !string.IsNullOrWhiteSpace(address))
            {
                transport.SetClientAddress(address);
            }

            if (TryGetLatency(out long latencyMs))
            {
                networkManager.TransportManager.LatencySimulator.SetEnabled(true);
                networkManager.TransportManager.LatencySimulator.SetLatency(latencyMs);
                Debug.Log($"[Network] Latency simulator enabled: {latencyMs} ms.");
            }
        }

        private static string GetOption(params string[] names)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                foreach (string name in names)
                {
                    if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    int valueIndex = i + 1;
                    if (valueIndex < args.Length)
                    {
                        return args[valueIndex];
                    }
                }
            }

            return string.Empty;
        }
    }
}
