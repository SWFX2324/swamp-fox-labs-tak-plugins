using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinTAK_OpenMANET_Plugin;

internal static class OpenMANET_Security
{
	public const int MaximumResponseBytes = 2097152;

	public const int MaximumSerializerItems = 10000;

	public const int MaximumNodes = 256;

	public const int MaximumEdges = 1024;

	public const int MaximumCollectionItems = 256;

	public const int MaximumSecondaryMacs = 16;

	public const int MaximumHostLength = 253;

	public const int MaximumUsernameLength = 128;

	public const int MaximumPasswordLength = 1024;

	public const int MaximumTokenLength = 8192;

	public const int MaximumIdentifierLength = 253;

	public const int MaximumDetailLength = 1024;

	public const int MaximumOperatorMessageLength = 384;

	public const int MaximumSettingsFileBytes = 4096;

	public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15.0);

	public static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(30.0);

	public static readonly TimeSpan LogoutTimeout = TimeSpan.FromSeconds(5.0);

	public static readonly TimeSpan LegacyAddressValidationTimeout = TimeSpan.FromSeconds(5.0);

	public static Uri BuildEndpoint(string hostText, int port, bool allowInsecureHttp)
	{
		if (string.IsNullOrWhiteSpace(hostText))
		{
			throw new ArgumentException("Gateway hostname is required.", "hostText");
		}
		if (port < 1 || port > 65535)
		{
			throw new ArgumentOutOfRangeException("port", "Port must be between 1 and 65535.");
		}
		string text = hostText.Trim();
		if (text.Length > 269 || ContainsControlCharacter(text))
		{
			throw new ArgumentException("Gateway hostname is invalid.", "hostText");
		}
		bool flag = text.IndexOf("://", StringComparison.Ordinal) >= 0;
		string text2 = (allowInsecureHttp ? Uri.UriSchemeHttp : Uri.UriSchemeHttps);
		string uriString = text;
		if (!flag)
		{
			bool flag2 = IPAddress.TryParse(text, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6;
			uriString = text2 + "://" + (flag2 ? ("[" + text + "]") : text);
		}
		if (!Uri.TryCreate(uriString, UriKind.Absolute, out var result))
		{
			throw new ArgumentException("Gateway must be a valid DNS name or IP address.", "hostText");
		}
		if (!string.Equals(result.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) && !string.Equals(result.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException("Gateway scheme must be HTTPS or HTTP.", "hostText");
		}
		if (string.Equals(result.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && !allowInsecureHttp)
		{
			throw new SecurityException("HTTP is disabled because it exposes the password and bearer token. Use HTTPS, a protected tunnel, or explicitly enable legacy insecure HTTP in Settings.");
		}
		if (allowInsecureHttp && !string.Equals(result.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException("Legacy direct mode requires HTTP. Clear the legacy option to use the secure HTTPS frontend.", "hostText");
		}
		if (!string.IsNullOrEmpty(result.UserInfo) || result.AbsolutePath != "/" || !string.IsNullOrEmpty(result.Query) || !string.IsNullOrEmpty(result.Fragment))
		{
			throw new ArgumentException("Gateway must contain only a scheme and hostname or IP address; paths, credentials, queries, and fragments are not allowed.", "hostText");
		}
		if (!result.IsDefaultPort && result.Port != port)
		{
			throw new ArgumentException("The port embedded in the gateway address does not match the Port field.", "hostText");
		}
		if (string.IsNullOrWhiteSpace(result.Host) || result.Host.Length > 253 || Uri.CheckHostName(result.Host) == UriHostNameType.Unknown)
		{
			throw new ArgumentException("Gateway hostname is invalid.", "hostText");
		}
		return new UriBuilder(result.Scheme, result.Host, port, "/").Uri;
	}

	public static async Task<Uri> ValidateAndPinLegacyEndpointAsync(Uri endpoint, CancellationToken cancellationToken)
	{
		if (endpoint == null)
		{
			throw new ArgumentNullException("endpoint");
		}
		if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
		{
			return endpoint;
		}
		IPAddress[] array;
		if (IPAddress.TryParse(endpoint.Host, out var address))
		{
			array = new IPAddress[1] { address };
		}
		else
		{
			Task<IPAddress[]> resolution = Dns.GetHostAddressesAsync(endpoint.DnsSafeHost);
			Task task = Task.Delay(LegacyAddressValidationTimeout, cancellationToken);
			if (await Task.WhenAny(resolution, task).ConfigureAwait(continueOnCapturedContext: false) != resolution)
			{
				cancellationToken.ThrowIfCancellationRequested();
				throw new SecurityException("Legacy HTTP gateway address validation timed out.");
			}
			array = await resolution.ConfigureAwait(continueOnCapturedContext: false);
		}
		if (array == null || array.Length == 0)
		{
			throw new SecurityException("Legacy HTTP gateway did not resolve to an address.");
		}
		IPAddress iPAddress = null;
		IPAddress[] array2 = array;
		foreach (IPAddress iPAddress2 in array2)
		{
			if (!IsPrivateOrLocalAddress(iPAddress2))
			{
				throw new SecurityException("Legacy HTTP is restricted to private or local network addresses. Use HTTPS or a protected tunnel for public destinations.");
			}
			if (iPAddress == null || (iPAddress.AddressFamily != AddressFamily.InterNetwork && iPAddress2.AddressFamily == AddressFamily.InterNetwork))
			{
				iPAddress = iPAddress2;
			}
		}
		return new UriBuilder(endpoint)
		{
			Host = iPAddress.ToString()
		}.Uri;
	}

	private static bool IsPrivateOrLocalAddress(IPAddress address)
	{
		if (address == null || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
		{
			return false;
		}
		if (IPAddress.IsLoopback(address))
		{
			return true;
		}
		if (address.IsIPv4MappedToIPv6)
		{
			address = address.MapToIPv4();
		}
		if (address.AddressFamily == AddressFamily.InterNetwork)
		{
			byte[] addressBytes = address.GetAddressBytes();
			if (addressBytes[0] != 10 && (addressBytes[0] != 172 || addressBytes[1] < 16 || addressBytes[1] > 31) && (addressBytes[0] != 192 || addressBytes[1] != 168))
			{
				if (addressBytes[0] == 169)
				{
					return addressBytes[1] == 254;
				}
				return false;
			}
			return true;
		}
		if (address.AddressFamily == AddressFamily.InterNetworkV6)
		{
			byte[] addressBytes2 = address.GetAddressBytes();
			if (!address.IsIPv6LinkLocal && !address.IsIPv6SiteLocal)
			{
				return (addressBytes2[0] & 0xFE) == 252;
			}
			return true;
		}
		return false;
	}

	public static string ValidateUsername(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new ArgumentException("Username is required.", "value");
		}
		string text = value.Trim();
		if (text.Length > 128 || ContainsControlCharacter(text))
		{
			throw new ArgumentException("Username is too long or contains invalid characters.", "value");
		}
		return text;
	}

	public static void ValidatePassword(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			throw new ArgumentException("Password is required.", "value");
		}
		if (value.Length > 1024)
		{
			throw new ArgumentException("Password exceeds the supported length.", "value");
		}
	}

	public static async Task<byte[]> ReadBoundedContentAsync(HttpContent content, CancellationToken cancellationToken)
	{
		if (content == null)
		{
			return new byte[0];
		}
		long? contentLength = content.Headers.ContentLength;
		if (contentLength.HasValue && contentLength.Value > 2097152)
		{
			throw new SerializationException("OpenMANET response exceeded the permitted size.");
		}
		using Stream input = await content.ReadAsStreamAsync().ConfigureAwait(continueOnCapturedContext: false);
		using MemoryStream output = new MemoryStream();
		byte[] buffer = new byte[8192];
		int total = 0;
		while (true)
		{
			int num = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			if (num == 0)
			{
				break;
			}
			total += num;
			if (total > 2097152)
			{
				throw new SerializationException("OpenMANET response exceeded the permitted size.");
			}
			output.Write(buffer, 0, num);
		}
		return output.ToArray();
	}

	public static string NormalizeIdentifier(string value, string fieldName, int maximumLength = 253)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		string text = value.Trim();
		if (text.Length > maximumLength || ContainsControlCharacter(text))
		{
			throw new SerializationException("OpenMANET returned an invalid " + fieldName + ".");
		}
		return text;
	}

	public static string NormalizeDetail(string value, int maximumLength = 1024)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		StringBuilder stringBuilder = new StringBuilder(Math.Min(value.Length, maximumLength));
		bool flag = false;
		foreach (char c in value)
		{
			char c2 = (char.IsControl(c) ? ' ' : c);
			if (char.IsWhiteSpace(c2))
			{
				if (flag)
				{
					continue;
				}
				c2 = ' ';
				flag = true;
			}
			else
			{
				flag = false;
			}
			stringBuilder.Append(c2);
			if (stringBuilder.Length >= maximumLength)
			{
				break;
			}
		}
		return stringBuilder.ToString().Trim();
	}

	public static string OperatorMessage(Exception exception)
	{
		if (exception == null)
		{
			return "The OpenMANET operation failed.";
		}
		if (exception is OperationCanceledException)
		{
			return "The OpenMANET operation timed out or was cancelled.";
		}
		if (exception is HttpRequestException && exception.InnerException != null)
		{
			Exception innerException = exception.InnerException;
			while (innerException.InnerException != null)
			{
				innerException = innerException.InnerException;
			}
			string text = NormalizeDetail(innerException.Message, 336);
			if (!string.IsNullOrWhiteSpace(text))
			{
				return "Unable to establish the OpenMANET network connection: " + text;
			}
			return "Unable to establish the OpenMANET network connection.";
		}
		return NormalizeDetail(exception.Message, 384) ?? "The OpenMANET operation failed.";
	}

	public static bool IsFinite(double value)
	{
		if (!double.IsNaN(value))
		{
			return !double.IsInfinity(value);
		}
		return false;
	}

	private static bool ContainsControlCharacter(string value)
	{
		for (int i = 0; i < value.Length; i++)
		{
			if (char.IsControl(value[i]))
			{
				return true;
			}
		}
		return false;
	}
}
