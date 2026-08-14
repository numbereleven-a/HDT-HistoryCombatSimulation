using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HistoryCombatSimulation
{
	public sealed class VersionCheckResult
	{
		public VersionCheckResult(string repository, ReleaseVersion latest, bool updateAvailable)
		{
			Repository = repository;
			Latest = latest;
			UpdateAvailable = updateAvailable;
		}

		public string Repository { get; }
		public ReleaseVersion Latest { get; }
		public bool UpdateAvailable { get; }
		public string LatestReleaseUrl => "https://github.com/" + Repository + "/releases/latest";
	}

	public sealed class VersionChecker
	{
		public const string DefaultRepository = "numbereleven-a/HDT-HistoryCombatSimulation";
		private static readonly HttpClient SharedClient = CreateClient();
		private readonly HttpClient _client;

		public VersionChecker() : this(SharedClient) { }
		public VersionChecker(HttpClient client) => _client = client ?? throw new ArgumentNullException(nameof(client));

		public async Task<VersionCheckResult> CheckAsync(Version installedVersion, CancellationToken cancellationToken)
		{
			var repository = Environment.GetEnvironmentVariable("HDT_HISTORYCOMBATSIMULATION_UPDATE_REPOSITORY");
			if(string.IsNullOrWhiteSpace(repository)) repository = DefaultRepository;
			if(!IsValidRepository(repository!)) throw new InvalidOperationException("Invalid update repository.");

			var requestUri = new Uri("https://api.github.com/repos/" + repository + "/releases/latest", UriKind.Absolute);
			using(var request = new HttpRequestMessage(HttpMethod.Get, requestUri))
			{
				request.Headers.UserAgent.ParseAdd("HistoryCombatSimulation/" + PluginVersion.Display);
				request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
				request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
				var token = Environment.GetEnvironmentVariable("HDT_HISTORYCOMBATSIMULATION_UPDATE_TOKEN");
				if(!string.IsNullOrWhiteSpace(token))
					request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

				using(var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
				{
					response.EnsureSuccessStatusCode();
					using(var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
					{
						var payload = (LatestReleasePayload?)new DataContractJsonSerializer(typeof(LatestReleasePayload)).ReadObject(stream);
						if(payload == null || !ReleaseVersion.TryParse(payload.TagName, out var latest))
							throw new InvalidDataException("The latest release tag is not a supported version.");
						var installed = ReleaseVersion.FromVersion(installedVersion);
						return new VersionCheckResult(repository!, latest!, latest!.CompareTo(installed) > 0);
					}
				}
			}
		}

		public static bool IsValidRepository(string value)
		{
			if(!Regex.IsMatch(value, @"^[A-Za-z0-9](?:[A-Za-z0-9_.-]{0,38})/[A-Za-z0-9_.-]{1,100}$", RegexOptions.CultureInvariant))
				return false;
			var repository = value.Substring(value.IndexOf('/') + 1);
			return repository.Any(character => character != '.');
		}

		private static HttpClient CreateClient() => new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

		[DataContract]
		private sealed class LatestReleasePayload
		{
			[DataMember(Name = "tag_name")]
			public string? TagName { get; set; }
		}
	}

	public sealed class ReleaseVersion : IComparable<ReleaseVersion>
	{
		private static readonly Regex Pattern = new Regex(
			@"^[vV]?(?<core>\d+(?:\.\d+){1,3})(?:-(?<pre>[0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);
		private readonly int[] _parts;
		private readonly string[] _prerelease;

		private ReleaseVersion(int[] parts, string[] prerelease, string display)
		{
			_parts = parts;
			_prerelease = prerelease;
			Display = display;
		}

		public string Display { get; }

		public static ReleaseVersion FromVersion(Version version) =>
			new ReleaseVersion(
				new[] { version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build), Math.Max(0, version.Revision) },
				Array.Empty<string>(),
				version.ToString());

		public static bool TryParse(string? value, out ReleaseVersion? version)
		{
			version = null;
			if(string.IsNullOrWhiteSpace(value)) return false;
			var trimmed = value!.Trim();
			var match = Pattern.Match(trimmed);
			if(!match.Success) return false;
			var segments = match.Groups["core"].Value.Split('.');
			var parts = new int[4];
			for(var index = 0; index < segments.Length; index++)
			{
				if(!int.TryParse(segments[index], NumberStyles.None, CultureInfo.InvariantCulture, out parts[index]))
					return false;
			}
			var prerelease = match.Groups["pre"].Success
				? match.Groups["pre"].Value.Split('.')
				: Array.Empty<string>();
			if(prerelease.Any(string.IsNullOrEmpty)) return false;
			version = new ReleaseVersion(parts, prerelease, trimmed.TrimStart('v', 'V'));
			return true;
		}

		public int CompareTo(ReleaseVersion? other)
		{
			if(other == null) return 1;
			for(var index = 0; index < _parts.Length; index++)
			{
				var coreComparison = _parts[index].CompareTo(other._parts[index]);
				if(coreComparison != 0) return coreComparison;
			}
			if(_prerelease.Length == 0) return other._prerelease.Length == 0 ? 0 : 1;
			if(other._prerelease.Length == 0) return -1;
			var commonLength = Math.Min(_prerelease.Length, other._prerelease.Length);
			for(var index = 0; index < commonLength; index++)
			{
				var comparison = ComparePrereleaseIdentifier(_prerelease[index], other._prerelease[index]);
				if(comparison != 0) return comparison;
			}
			return _prerelease.Length.CompareTo(other._prerelease.Length);
		}

		public override string ToString() => Display;

		private static int ComparePrereleaseIdentifier(string left, string right)
		{
			var leftNumeric = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
			var rightNumeric = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);
			if(leftNumeric && rightNumeric) return leftNumber.CompareTo(rightNumber);
			if(leftNumeric != rightNumeric) return leftNumeric ? -1 : 1;
			return string.Compare(left, right, StringComparison.Ordinal);
		}
	}
}
