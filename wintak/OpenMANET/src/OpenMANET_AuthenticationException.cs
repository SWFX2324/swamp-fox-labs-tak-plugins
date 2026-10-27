using System.Net.Http;

namespace WinTAK_OpenMANET_Plugin;

internal sealed class OpenMANET_AuthenticationException : HttpRequestException
{
	public OpenMANET_AuthenticationException(string message)
		: base(message)
	{
	}
}
