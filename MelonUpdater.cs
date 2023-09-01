using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using MelonLoader;
using Mono.Cecil;
using Semver;

[assembly: MelonInfo(typeof(MelonUpdater.MelonUpdater), nameof(MelonUpdater), "1.0.0", "GrahamKracker")]
[assembly: MelonGame]
[assembly: MelonPriority(-1000)]
[assembly: MelonOptionalDependencies("System.Net.Http", "Newtonsoft.Json", "Microsoft.CSharp")]

namespace MelonUpdater;

public class MelonUpdater : MelonPlugin
{
    public static string MelonLoaderDirectory => Path.Combine(GameRootDirectory, "MelonLoader");
    public static string GameRootDirectory => Path.GetDirectoryName(GameExecutablePath);
    public static string GameExecutablePath => Process.GetCurrentProcess().MainModule.FileName;
    public static string MelonBaseDirectory => Directory.GetParent(MelonLoaderDirectory).FullName;
    public static string ModsDirectory => Path.Combine(MelonBaseDirectory, "Mods");
    public static string PluginsDirectory => Path.Combine(MelonBaseDirectory, "Plugins");
    public static string UserLibsDirectory => Path.Combine(MelonBaseDirectory, "UserLibs");
    
    public static string MelonManagedDirectory => Path.Combine(MelonLoaderDirectory, "Managed");
    public override void OnEarlyInitializeMelon()
    {
        if (!File.Exists(UserLibsDirectory + "/System.Net.Http.dll"))
        {
            File.Copy(MelonManagedDirectory + "/System.Net.Http.dll", UserLibsDirectory + "/System.Net.Http.dll", true);
        }
        if (!File.Exists(UserLibsDirectory + "/Newtonsoft.Json.dll"))
        {
            File.Copy(MelonManagedDirectory + "/Newtonsoft.Json.dll", UserLibsDirectory + "/Newtonsoft.Json.dll", true);
        }
        if (!File.Exists(UserLibsDirectory + "/Microsoft.CSharp.dll"))
        {
            File.Copy(MelonManagedDirectory + "/Microsoft.CSharp.dll", UserLibsDirectory + "/Microsoft.CSharp.dll", true);
        }
        if (!File.Exists(UserLibsDirectory + "/System.Numerics.dll"))
        {
            File.Copy(MelonManagedDirectory + "/System.Numerics.dll", UserLibsDirectory + "/System.Numerics.dll", true);
        }
    }

    public override void OnApplicationEarlyStart()
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "MelonUpdater");

        foreach (var melon in Directory.GetFiles(ModsDirectory, "*.dll"))
        {
            try
            {
                LoggerInstance.Msg("Scanning " + melon+" for updates");

                var module = ModuleDefinition.ReadModule(melon);

                var type = module.GetCustomAttributes()
                    .FirstOrDefault(x => x.AttributeType.FullName == "MelonLoader.MelonInfoAttribute");

                if (type?.ConstructorArguments[4].Value is null)
                {
                    continue;
                }

                if (type.ConstructorArguments[2].Value is null)
                {
                    continue;
                }

                var version = type.ConstructorArguments[2].Value.ToString();

                var url = type.ConstructorArguments[4].Value.ToString();

                module.Dispose();

                if (!url.StartsWith("https://github.com/"))
                {
                    continue;
                }

                var repoOwner = url.Split('/')[3];
                var repoName = url.Split('/')[4];

                url = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases/latest";

                var urlResponseMessage = httpClient.GetAsync(url).GetAwaiter().GetResult();

                urlResponseMessage.EnsureSuccessStatusCode();
                var content = urlResponseMessage.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                dynamic json = Newtonsoft.Json.JsonConvert.DeserializeObject(content)!;

                if (!SemVersion.TryParse(json.tag_name.Value, out SemVersion semver, false) || semver <= version) continue;

                LoggerInstance.Msg("Remote version is newer, attempting to update");

                var foundFileLink = false;
                var downloadUrl = "";
                var fileName = "";

                foreach (var asset in json.assets)
                {
                    if (asset.name.Value != Path.GetFileName(melon))
                    {
                        continue;
                    }

                    foundFileLink = true;
                    downloadUrl = asset.browser_download_url.Value;
                    fileName = asset.name.Value;
                    break;
                }

                if (!foundFileLink)
                {
                    downloadUrl = json.assets[0].browser_download_url.Value;
                    fileName = json.assets[0].name.Value;
                }

                fileName = Path.Combine(ModsDirectory, fileName);

                LoggerInstance.Msg("Downloading new version from: " + downloadUrl);

                var downloadFile = httpClient.GetAsync(downloadUrl).GetAwaiter().GetResult();
                downloadFile.EnsureSuccessStatusCode();
                var downloadContent = downloadFile.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();

                LoggerInstance.Msg("Successfully downloaded new version");

                File.Delete(melon);
                File.WriteAllBytes(fileName, downloadContent);
            }
            catch (Exception e)
            {
                LoggerInstance.Error(e);
            }
        }

        httpClient.Dispose();
    }
}