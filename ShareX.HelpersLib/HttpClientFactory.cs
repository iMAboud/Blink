#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;

namespace ShareX.HelpersLib
{
    public static class HttpClientFactory
    {
        private static readonly object lockObject = new object();

        private static readonly Dictionary<(bool AllowAutoRedirect, bool InfiniteTimeout), HttpClient> clients =
            new Dictionary<(bool AllowAutoRedirect, bool InfiniteTimeout), HttpClient>();
        private static string proxyKey;

        private static readonly string[] GitHubFallbackIps = new[]
        {
            "185.199.108.133",
            "185.199.109.133",
            "185.199.110.133",
            "185.199.111.133"
        };

        public static HttpClient Create(bool allowAutoRedirect = true, bool infiniteTimeout = false)
        {
            lock (lockObject)
            {
                IWebProxy proxy = HelpersOptions.CurrentProxy.GetWebProxy();
                string currentProxyKey = GetProxyKey();

                if (!string.Equals(proxyKey, currentProxyKey, StringComparison.Ordinal))
                {
                    DisposeClients();
                    proxyKey = currentProxyKey;
                }

                (bool AllowAutoRedirect, bool InfiniteTimeout) clientKey = (allowAutoRedirect, infiniteTimeout);

                if (!clients.TryGetValue(clientKey, out HttpClient client))
                {
                    SocketsHttpHandler handler = new SocketsHttpHandler()
                    {
                        AllowAutoRedirect = allowAutoRedirect,
                        ConnectTimeout = TimeSpan.FromSeconds(30),
                        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                        Proxy = proxy,
                        UseCookies = false,
                        UseProxy = proxy != null,
                        ConnectCallback = async (context, cancellationToken) =>
                        {
                            string host = context.DnsEndPoint.Host;
                            int port = context.DnsEndPoint.Port;

                            try
                            {
                                System.Net.Sockets.Socket socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
                                await socket.ConnectAsync(host, port, cancellationToken);
                                return new NetworkStream(socket, ownsSocket: true);
                            }
                            catch (Exception) when (host.Contains("githubusercontent.com", StringComparison.OrdinalIgnoreCase))
                            {
                                foreach (string ip in GitHubFallbackIps)
                                {
                                    try
                                    {
                                        System.Net.Sockets.Socket socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
                                        await socket.ConnectAsync(IPAddress.Parse(ip), port, cancellationToken);
                                        return new NetworkStream(socket, ownsSocket: true);
                                    }
                                    catch { }
                                }
                                throw;
                            }
                        }
                    };

                    client = new HttpClient(handler)
                    {
                        Timeout = infiniteTimeout ? System.Threading.Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(100)
                    };
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(ShareXResources.UserAgent);
                    client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue()
                    {
                        NoCache = true
                    };

                    clients.Add(clientKey, client);
                }

                return client;
            }
        }

        public static void Reset()
        {
            lock (lockObject)
            {
                DisposeClients();
                proxyKey = null;
            }
        }

        private static void DisposeClients()
        {
            foreach (HttpClient client in clients.Values)
            {
                client.Dispose();
            }

            clients.Clear();
        }

        private static string GetProxyKey()
        {
            ProxyInfo proxy = HelpersOptions.CurrentProxy;
            return $"{proxy.ProxyMethod}\0{proxy.Host}\0{proxy.Port}\0{proxy.Username}\0{proxy.Password}";
        }
    }
}
