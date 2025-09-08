using System.Net;
using Microsoft.AspNetCore.Http;
using ProjectManagement.Helpers;

namespace ProjectManagement.Tests
{
    public class ClientIpTests
    {
        [Fact]
        public void IPv6Loopback_To_Localhost()
        {
            var ctx = new DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = IPAddress.IPv6Loopback;
            Assert.Equal("127.0.0.1", ClientIp.Get(ctx));
        }

        [Fact]
        public void IPv4MappedIPv6_To_IPv4()
        {
            var ctx = new DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:10.0.0.5");
            Assert.Equal("10.0.0.5", ClientIp.Get(ctx));
        }

        [Fact]
        public void RealIPv6_RemainsIPv6()
        {
            var ctx = new DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("2001:db8::1");
            Assert.Equal("2001:db8::1", ClientIp.Get(ctx));
        }
    }
}
