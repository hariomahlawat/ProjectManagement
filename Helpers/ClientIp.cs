using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;

namespace ProjectManagement.Helpers
{
    public static class ClientIp
    {
        public static string Get(HttpContext? ctx)
        {
            if (ctx?.Connection?.RemoteIpAddress is not IPAddress ip)
                return string.Empty;

            // Handle IPv6 and mapped IPv4 correctly
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv4MappedToIPv6)
                {
                    ip = ip.MapToIPv4();               // ::ffff:192.168.1.50 -> 192.168.1.50
                }
                else if (IPAddress.IPv6Loopback.Equals(ip))
                {
                    return "127.0.0.1";                // ::1 -> 127.0.0.1 for clarity in logs
                }
            }

            return ip.ToString();
        }
    }
}
