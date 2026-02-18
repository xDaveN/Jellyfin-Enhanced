using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.JellyfinEnhanced.Configuration;

namespace Jellyfin.Plugin.JellyfinEnhanced.Helpers.Jellyseerr
{
    public sealed class JellyseerrInstanceTarget
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
        public string ApiKey { get; init; } = string.Empty;
    }

    public static class JellyseerrInstanceHelper
    {
        public static string[] SplitConfigLines(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<string>();
            }

            return value
                .Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToArray();
        }

        public static List<JellyseerrInstanceTarget> GetConfiguredInstances(PluginConfiguration? config)
        {
            var instances = new List<JellyseerrInstanceTarget>();
            if (config == null)
            {
                return instances;
            }

            var urls = SplitConfigLines(config.JellyseerrUrls);
            if (urls.Length == 0)
            {
                return instances;
            }

            var apiKeys = !string.IsNullOrWhiteSpace(config.JellyseerrApiKeys)
                ? SplitConfigLines(config.JellyseerrApiKeys)
                : SplitConfigLines(config.JellyseerrApiKey);
            var names = SplitConfigLines(config.JellyseerrInstanceNames);
            var useSharedApiKey = apiKeys.Length == 1;

            for (var i = 0; i < urls.Length; i++)
            {
                var apiKey = useSharedApiKey ? apiKeys.FirstOrDefault() : (i < apiKeys.Length ? apiKeys[i] : string.Empty);
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    continue;
                }

                var name = i < names.Length && !string.IsNullOrWhiteSpace(names[i]) ? names[i] : $"Seerr {i + 1}";
                instances.Add(new JellyseerrInstanceTarget
                {
                    Id = $"seerr-{i + 1}",
                    Name = name,
                    Url = urls[i].Trim().TrimEnd('/'),
                    ApiKey = apiKey.Trim()
                });
            }

            return instances;
        }
    }
}
