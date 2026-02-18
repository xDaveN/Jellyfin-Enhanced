// Global aliases
global using JUser = Jellyfin.Database.Implementations.Entities.User;
global using JSortOrder = Jellyfin.Database.Implementations.Enums.SortOrder;

using System.Globalization;
using Jellyfin.Plugin.JellyfinEnhanced.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using System.IO;
using System.Collections.Generic;
using System;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Configuration;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using MediaBrowser.Common.Net;
using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;

namespace Jellyfin.Plugin.JellyfinEnhanced
{
    public class JellyfinEnhanced : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        private readonly IApplicationPaths _applicationPaths;
        private readonly Logger _logger;
        private const string PluginName = "Jellyfin Enhanced";
        private const string ManagedRequestsCustomTabMarker = "data-je-managed=\"requests-seerr\"";
        private readonly object _customTabsSyncLock = new();

        public JellyfinEnhanced(IApplicationPaths applicationPaths, IServerConfigurationManager serverConfigurationManager, IXmlSerializer xmlSerializer, Logger logger) : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            _applicationPaths = applicationPaths;
            _logger = logger;
            _logger.Info($"{PluginName} v{Version} initialized. Plugin logs will be written to: {_logger.CurrentLogFilePath}");
            CleanupOldScript();
            CheckPluginPages(applicationPaths, serverConfigurationManager, 1);
            SyncCustomTabsConfiguration();
        }

        public override string Name => PluginName;
        public override Guid Id => Guid.Parse("f69e946a-4b3c-4e9a-8f0a-8d7c1b2c4d9b");
        public static JellyfinEnhanced? Instance { get; private set; }

        private string IndexHtmlPath => Path.Combine(_applicationPaths.WebPath, "index.html");

        private sealed class ManagedCustomTab
        {
            public string Title { get; init; } = string.Empty;
            public string ContentHtml { get; init; } = string.Empty;
        }

        private sealed class JellyseerrInstanceTab
        {
            public string Id { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
        }

        public static string BrandingDirectory
        {
            get
            {
                if (Instance == null)
                    return string.Empty;

                var configPath = Instance.ConfigurationFilePath;
                if (string.IsNullOrWhiteSpace(configPath))
                    return string.Empty;

                var configDir = Path.GetDirectoryName(configPath);
                if (string.IsNullOrWhiteSpace(configDir))
                    return string.Empty;

                var pluginFolderName = Path.GetFileNameWithoutExtension(configPath) ?? "Jellyfin.Plugin.JellyfinEnhanced";
                return Path.Combine(configDir, pluginFolderName, "custom_branding");
            }
        }

        private static string[] SplitConfigLines(string? value)
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

        private static string NormalizeHtml(string? html)
        {
            return Regex.Replace(html ?? string.Empty, "\\s+", string.Empty).ToLowerInvariant();
        }

        private static bool IsManagedRequestsCustomTab(string? contentHtml)
        {
            if (string.IsNullOrWhiteSpace(contentHtml))
            {
                return false;
            }

            return contentHtml.Contains(ManagedRequestsCustomTabMarker, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLegacyRequestsCustomTab(string? contentHtml)
        {
            if (string.IsNullOrWhiteSpace(contentHtml))
            {
                return false;
            }

            var normalized = NormalizeHtml(contentHtml);
            return normalized == "<divclass=\"jellyfinenhancedrequests\"></div>"
                || normalized == "<divclass='jellyfinenhancedrequests'></div>";
        }

        private List<JellyseerrInstanceTab> GetConfiguredJellyseerrInstancesForTabs(PluginConfiguration? config)
        {
            var instances = new List<JellyseerrInstanceTab>();
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
                instances.Add(new JellyseerrInstanceTab
                {
                    Id = $"seerr-{i + 1}",
                    Name = name
                });
            }

            return instances;
        }

        private List<ManagedCustomTab> BuildManagedRequestsCustomTabs(PluginConfiguration config)
        {
            var tabs = new List<ManagedCustomTab>();
            if (!config.DownloadsPageEnabled || !config.DownloadsUseCustomTabs)
            {
                return tabs;
            }

            var baseTitle = "Requests";
            var instances = config.JellyseerrEnabled
                ? GetConfiguredJellyseerrInstancesForTabs(config)
                : new List<JellyseerrInstanceTab>();

            if (instances.Count > 1)
            {
                foreach (var instance in instances)
                {
                    tabs.Add(new ManagedCustomTab
                    {
                        Title = instance.Name,
                        ContentHtml = $"<div class=\"jellyfinenhanced requests\" {ManagedRequestsCustomTabMarker} data-je-seerr-instance-id=\"{instance.Id}\"></div>"
                    });
                }

                return tabs;
            }

            var singleInstanceId = instances.FirstOrDefault()?.Id;
            var instanceAttr = string.IsNullOrWhiteSpace(singleInstanceId)
                ? string.Empty
                : $" data-je-seerr-instance-id=\"{singleInstanceId}\"";

            tabs.Add(new ManagedCustomTab
            {
                Title = instances.FirstOrDefault()?.Name ?? baseTitle,
                ContentHtml = $"<div class=\"jellyfinenhanced requests\" {ManagedRequestsCustomTabMarker}{instanceAttr}></div>"
            });

            return tabs;
        }

        public void SyncCustomTabsConfiguration()
        {
            lock (_customTabsSyncLock)
            {
                try
                {
                    var customTabsConfigPath = Path.Combine(_applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.CustomTabs.xml");
                    if (!File.Exists(customTabsConfigPath))
                    {
                        return;
                    }

                    var pluginConfig = Configuration;
                    var desiredTabs = BuildManagedRequestsCustomTabs(pluginConfig);

                    var document = XDocument.Load(customTabsConfigPath);
                    var root = document.Root;
                    if (root == null)
                    {
                        return;
                    }

                    var tabsElement = root.Element("Tabs");
                    if (tabsElement == null)
                    {
                        tabsElement = new XElement("Tabs");
                        root.Add(tabsElement);
                    }

                    var existingTabs = tabsElement.Elements().ToList();
                    var managedExistingTabs = existingTabs
                        .Where(tab => IsManagedRequestsCustomTab(tab.Element("ContentHtml")?.Value))
                        .ToList();

                    var legacyExistingTabs = desiredTabs.Count > 0
                        ? existingTabs
                            .Where(tab => IsLegacyRequestsCustomTab(tab.Element("ContentHtml")?.Value))
                            .ToList()
                        : new List<XElement>();

                    var existingManagedProjection = managedExistingTabs
                        .Select(tab => new ManagedCustomTab
                        {
                            Title = tab.Element("Title")?.Value ?? string.Empty,
                            ContentHtml = tab.Element("ContentHtml")?.Value ?? string.Empty
                        })
                        .ToList();

                    bool shouldRewrite = managedExistingTabs.Count != desiredTabs.Count
                        || legacyExistingTabs.Count > 0
                        || existingManagedProjection.Zip(desiredTabs, (a, b) => a.Title == b.Title && a.ContentHtml == b.ContentHtml).Any(equal => !equal);

                    if (!shouldRewrite)
                    {
                        return;
                    }

                    foreach (var tab in managedExistingTabs)
                    {
                        tab.Remove();
                    }

                    foreach (var tab in legacyExistingTabs)
                    {
                        tab.Remove();
                    }

                    foreach (var desiredTab in desiredTabs)
                    {
                        tabsElement.Add(new XElement("TabConfig",
                            new XElement("Title", desiredTab.Title),
                            new XElement("ContentHtml", desiredTab.ContentHtml)));
                    }

                    document.Save(customTabsConfigPath);
                    _logger.Info($"Synced {desiredTabs.Count} managed Requests tab(s) to Custom Tabs config.");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to sync Custom Tabs configuration: {ex.Message}");
                }
            }
        }

        public void InjectScript()
        {
            UpdateIndexHtml(true);
        }

        public override void OnUninstalling()
        {
            UpdateIndexHtml(false);
            base.OnUninstalling();
        }
        private void CleanupOldScript()
        {
            try
            {
                var indexPath = IndexHtmlPath;
                if (!File.Exists(indexPath))
                {
                    _logger.Error($"Could not find index.html at path: {indexPath}");
                    return;
                }

                var content = File.ReadAllText(indexPath);
                var regex = new Regex($"<script[^>]*plugin=[\"']{Name}[\"'][^>]*>\\s*</script>\\n?");

                if (regex.IsMatch(content))
                {
                    _logger.Info("Found old Jellyfin Enhanced script tag in index.html. Removing it now.");
                    content = regex.Replace(content, string.Empty);
                    File.WriteAllText(indexPath, content);
                    _logger.Info("Successfully removed old script tag.");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error during cleanup of old script from index.html: {ex.Message}");
            }
        }
        private void CheckPluginPages(IApplicationPaths applicationPaths, IServerConfigurationManager serverConfigurationManager, int pluginPageConfigVersion)
        {
            string pluginPagesConfig = Path.Combine(applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.PluginPages", "config.json");

            JObject config = new JObject();
            if (!File.Exists(pluginPagesConfig))
            {
                FileInfo info = new FileInfo(pluginPagesConfig);
                info.Directory?.Create();
            }
            else
            {
                config = JObject.Parse(File.ReadAllText(pluginPagesConfig));
            }

            if (!config.ContainsKey("pages"))
            {
                config.Add("pages", new JArray());
            }

            var namespaceName = typeof(JellyfinEnhanced).Namespace;

            JObject? hssPageConfig = config.Value<JArray>("pages")!.FirstOrDefault(x =>
                x.Value<string>("Id") == namespaceName) as JObject;

            if (hssPageConfig != null)
            {
                if ((hssPageConfig.Value<int?>("Version") ?? 0) < pluginPageConfigVersion)
                {
                    config.Value<JArray>("pages")!.Remove(hssPageConfig);
                }
            }

            Assembly? pluginPagesAssembly = AssemblyLoadContext.All.SelectMany(x => x.Assemblies).FirstOrDefault(x => x.FullName?.Contains("Jellyfin.Plugin.PluginPages") ?? false);

            Version earliestVersionWithSubUrls = new Version("2.4.1.0");
            bool supportsSubUrls = pluginPagesAssembly != null && pluginPagesAssembly.GetName().Version >= earliestVersionWithSubUrls;

            string rootUrl = serverConfigurationManager.GetNetworkConfiguration().BaseUrl.TrimStart('/').Trim();
            if (!string.IsNullOrEmpty(rootUrl))
            {
                rootUrl = $"/{rootUrl}";
            }

            var pluginConfig = Configuration;

            bool calendarExists = config.Value<JArray>("pages")!
                .Any(x => x.Value<string>("Id") == $"{namespaceName}.CalendarPage");

            bool downloadsExists = config.Value<JArray>("pages")!
                .Any(x => x.Value<string>("Id") == $"{namespaceName}.DownloadsPage");

            bool bookmarksExists = config.Value<JArray>("pages")!
                .Any(x => x.Value<string>("Id") == $"{namespaceName}.BookmarksPage");

            bool hiddenContentExists = config.Value<JArray>("pages")!
                .Any(x => x.Value<string>("Id") == $"{namespaceName}.HiddenContentPage");

            // Only add calendar page if it's enabled and using plugin pages
            if (!calendarExists && pluginConfig.CalendarPageEnabled && pluginConfig.CalendarUsePluginPages)
            {
                config.Value<JArray>("pages")!.Add(new JObject
                {
                    { "Id", $"{namespaceName}.CalendarPage" },
                    { "Url", $"{(supportsSubUrls ? "" : rootUrl)}/JellyfinEnhanced/calendarPage" },
                    { "DisplayText", "Calendar" },
                    { "Icon", "calendar_today" },
                    { "Version", pluginPageConfigVersion }
                });
            }
            // Remove calendar page if it exists but is now disabled or not using plugin pages
            else if (calendarExists && (!pluginConfig.CalendarPageEnabled || !pluginConfig.CalendarUsePluginPages))
            {
                var calendarPage = config.Value<JArray>("pages")!
                    .FirstOrDefault(x => x.Value<string>("Id") == $"{namespaceName}.CalendarPage");
                if (calendarPage != null)
                {
                    config.Value<JArray>("pages")!.Remove(calendarPage);
                }
            }

            // Only add downloads page if it's enabled and using plugin pages
            if (!downloadsExists && pluginConfig.DownloadsPageEnabled && pluginConfig.DownloadsUsePluginPages)
            {
                config.Value<JArray>("pages")!.Add(new JObject
                {
                    { "Id", $"{namespaceName}.DownloadsPage" },
                    { "Url", $"{(supportsSubUrls ? "" : rootUrl)}/JellyfinEnhanced/downloadsPage" },
                    { "DisplayText", "Requests" },
                    { "Icon", "download" },
                    { "Version", pluginPageConfigVersion }
                });
            }
            // Remove downloads page if it exists but is now disabled or not using plugin pages
            else if (downloadsExists && (!pluginConfig.DownloadsPageEnabled || !pluginConfig.DownloadsUsePluginPages))
            {
                var downloadsPage = config.Value<JArray>("pages")!
                    .FirstOrDefault(x => x.Value<string>("Id") == $"{namespaceName}.DownloadsPage");
                if (downloadsPage != null)
                {
                    config.Value<JArray>("pages")!.Remove(downloadsPage);
                }
            }

            // Only add bookmarks page if it's enabled and using plugin pages
            if (!bookmarksExists && pluginConfig.BookmarksEnabled && pluginConfig.BookmarksUsePluginPages)
            {
                config.Value<JArray>("pages")!.Add(new JObject
                {
                    { "Id", $"{namespaceName}.BookmarksPage" },
                    { "Url", $"{(supportsSubUrls ? "" : rootUrl)}/JellyfinEnhanced/bookmarksPage" },
                    { "DisplayText", "Bookmarks" },
                    { "Icon", "bookmark" },
                    { "Version", pluginPageConfigVersion }
                });
            }
            // Remove bookmarks page if it exists but is now disabled or not using plugin pages
            else if (bookmarksExists && (!pluginConfig.BookmarksEnabled || !pluginConfig.BookmarksUsePluginPages))
            {
                var bookmarksPage = config.Value<JArray>("pages")!
                    .FirstOrDefault(x => x.Value<string>("Id") == $"{namespaceName}.BookmarksPage");
                if (bookmarksPage != null)
                {
                    config.Value<JArray>("pages")!.Remove(bookmarksPage);
                }
            }

            // Only add hidden content page if it's enabled and using plugin pages
            if (!hiddenContentExists && pluginConfig.HiddenContentEnabled && pluginConfig.HiddenContentUsePluginPages)
            {
                config.Value<JArray>("pages")!.Add(new JObject
                {
                    { "Id", $"{namespaceName}.HiddenContentPage" },
                    { "Url", $"{(supportsSubUrls ? "" : rootUrl)}/JellyfinEnhanced/hiddenContentPage" },
                    { "DisplayText", "Hidden Content" },
                    { "Icon", "visibility_off" },
                    { "Version", pluginPageConfigVersion }
                });
            }
            // Remove hidden content page if it exists but is now disabled or not using plugin pages
            else if (hiddenContentExists && (!pluginConfig.HiddenContentEnabled || !pluginConfig.HiddenContentUsePluginPages))
            {
                var hiddenContentPage = config.Value<JArray>("pages")!
                    .FirstOrDefault(x => x.Value<string>("Id") == $"{namespaceName}.HiddenContentPage");
                if (hiddenContentPage != null)
                {
                    config.Value<JArray>("pages")!.Remove(hiddenContentPage);
                }
            }

            File.WriteAllText(pluginPagesConfig, config.ToString(Formatting.Indented));
        }
        private void UpdateIndexHtml(bool inject)
        {
            try
            {
                var indexPath = IndexHtmlPath;
                if (!File.Exists(indexPath))
                {
                    _logger.Error($"Could not find index.html at path: {indexPath}");
                    return;
                }

                var content = File.ReadAllText(indexPath);
                var scriptUrl = "../JellyfinEnhanced/script";
                var scriptTag = $"<script plugin=\"{Name}\" version=\"{Version}\" src=\"{scriptUrl}\" defer></script>";
                var regex = new Regex($"<script[^>]*plugin=[\"']{Name}[\"'][^>]*>\\s*</script>\\n?");

                // Remove any old versions of the script tag first
                content = regex.Replace(content, string.Empty);

                if (inject)
                {
                    var closingBodyTag = "</body>";
                    if (content.Contains(closingBodyTag))
                    {
                        content = content.Replace(closingBodyTag, $"{scriptTag}\n{closingBodyTag}");
                        _logger.Info($"Successfully injected/updated the {PluginName} script.");
                    }
                    else
                    {
                        _logger.Warning("Could not find </body> tag in index.html. Script not injected.");
                        return; // Return early if injection point not found
                    }
                }
                else
                {
                    _logger.Info($"Successfully removed the {PluginName} script from index.html during uninstall.");
                }

                File.WriteAllText(indexPath, content);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error while trying to update index.html: {ex.Message}");
            }
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = this.Name,
                    DisplayName = "Jellyfin Enhanced",
                    EnableInMainMenu = true,
                    EmbeddedResourcePath = "Jellyfin.Plugin.JellyfinEnhanced.Configuration.configPage.html"
                    //Custom Icons are not supported - https://github.com/jellyfin/jellyfin-web/blob/38ac3355447a91bf280df419d745f5d49d05aa9b/src/apps/dashboard/components/drawer/sections/PluginDrawerSection.tsx#L61
                }
            };
        }

        public IEnumerable<PluginPageInfo> GetViews()
        {
            return new[]
            {
                new PluginPageInfo {
                    Name = "calendarPage",
                    EmbeddedResourcePath = $"{GetType().Namespace}.PluginPages.CalendarPage.html"
                },
                new PluginPageInfo {
                    Name = "downloadsPage",
                    EmbeddedResourcePath = $"{GetType().Namespace}.PluginPages.DownloadsPage.html"
                },
                new PluginPageInfo {
                    Name = "bookmarksPage",
                    EmbeddedResourcePath = $"{GetType().Namespace}.PluginPages.BookmarksPage.html"
                },
                new PluginPageInfo {
                    Name = "hiddenContentPage",
                    EmbeddedResourcePath = $"{GetType().Namespace}.PluginPages.HiddenContentPage.html"
                }
            };
        }
    }
}
