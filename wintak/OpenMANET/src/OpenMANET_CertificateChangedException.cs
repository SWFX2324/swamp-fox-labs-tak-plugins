using System.Net.Http;

namespace WinTAK_OpenMANET_Plugin;

/// <summary>The node presented a different HTTPS certificate from the one pinned on first connect.</summary>
internal sealed class OpenMANET_CertificateChangedException : HttpRequestException
{
	public string ExpectedThumbprint { get; }

	public string ActualThumbprint { get; }

	public OpenMANET_CertificateChangedException(string expectedThumbprint, string actualThumbprint)
		: base("The OpenMANET node's HTTPS certificate has changed since it was first trusted. The connection was blocked.")
	{
		ExpectedThumbprint = expectedThumbprint;
		ActualThumbprint = actualThumbprint;
	}
}
