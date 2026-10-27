using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WinTAK_OpenMANET_Plugin;

internal sealed class OpenMANET_DiagnosticRedactor
{
	private static readonly Regex MacAddressPattern = new Regex("\\b(?:[0-9a-fA-F]{2}[:-]){5}[0-9a-fA-F]{2}\\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex Ipv4Pattern = new Regex("(?<!\\d)(?:\\d{1,3}\\.){3}\\d{1,3}(?!\\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex Ipv6Pattern = new Regex("(?<![0-9a-fA-F])(?:[0-9a-fA-F]{0,4}:){2,7}[0-9a-fA-F]{0,4}(?![0-9a-fA-F])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private readonly Dictionary<string, string> _aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, int> _nextAliasByCategory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	public string Alias(string value, string category)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "--";
		}
		string key = value.Trim();
		if (_aliases.TryGetValue(key, out var value2))
		{
			return value2;
		}
		_nextAliasByCategory.TryGetValue(category, out var value3);
		value3++;
		_nextAliasByCategory[category] = value3;
		string text = category + "-" + value3.ToString("000");
		_aliases[key] = text;
		return text;
	}

	public string RedactText(string value)
	{
		string text = OpenMANET_Security.NormalizeDetail(value) ?? string.Empty;
		foreach (KeyValuePair<string, string> item in _aliases.OrderByDescending((KeyValuePair<string, string> item) => item.Key.Length))
		{
			text = ReplaceOrdinalIgnoreCase(text, item.Key, item.Value);
		}
		text = MacAddressPattern.Replace(text, "MAC-[redacted]");
		text = Ipv4Pattern.Replace(text, "IP-[redacted]");
		return Ipv6Pattern.Replace(text, "IP-[redacted]");
	}

	private static string ReplaceOrdinalIgnoreCase(string value, string search, string replacement)
	{
		if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(search))
		{
			return value;
		}
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		int num = 0;
		while (num < value.Length)
		{
			int num2 = value.IndexOf(search, num, StringComparison.OrdinalIgnoreCase);
			// Skip matches inside a longer token so "Node1" doesn't rewrite part of "Node10".
			while (num2 >= 0 && !IsWholeToken(value, num2, search.Length))
			{
				num2 = value.IndexOf(search, num2 + 1, StringComparison.OrdinalIgnoreCase);
			}
			if (num2 < 0)
			{
				stringBuilder.Append(value, num, value.Length - num);
				break;
			}
			stringBuilder.Append(value, num, num2 - num);
			stringBuilder.Append(replacement);
			num = num2 + search.Length;
		}
		return stringBuilder.ToString();
	}

	private static bool IsWholeToken(string value, int start, int length)
	{
		int end = start + length;
		bool startsClean = start == 0 || !IsTokenChar(value[start - 1]) || !IsTokenChar(value[start]);
		bool endsClean = end >= value.Length || !IsTokenChar(value[end]) || !IsTokenChar(value[end - 1]);
		return startsClean && endsClean;
	}

	private static bool IsTokenChar(char c)
	{
		// Separators like '_' and '.' stay boundaries so "Node0_wlan0" still redacts "Node0".
		return char.IsLetterOrDigit(c);
	}
}
