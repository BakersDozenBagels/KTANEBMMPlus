using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public static class Repository
{
    public static string RawJSON;
    public static List<KtaneModule> Modules;
    public static readonly Dictionary<string, string[]> ProcessedIgnoreLists = new Dictionary<string, string[]>(),
        ProcessedIdIgnoreLists = new Dictionary<string, string[]>();
    public static bool Loaded;

    private static Dictionary<string, string> _moduleIds, _moduleNames;

    public static IEnumerator LoadData()
    {
        if (RawJSON != null)
            yield break;

        var download = new DownloadText("https://ktane.timwi.de/json/raw");
        yield return download;

        var repositoryBackup = Path.Combine(Application.persistentDataPath, "RepositoryBackup-BMMPlus.json");

        RawJSON = download.Text;
        if (RawJSON == null)
        {
            Debug.Log("[BMM+] Unable to download the repository.");

            if (File.Exists(repositoryBackup))
                RawJSON = File.ReadAllText(repositoryBackup);
            else
                Debug.Log("[BMM+] Could not find a repository backup.");
        }

        if (RawJSON == null)
        {
            Debug.Log("[BMM+] Could not get module information.");

            Modules = new List<KtaneModule>();
        }
        else
        {
            // Save a backup of the repository
            File.WriteAllText(repositoryBackup, RawJSON);

            Modules = JsonConvert.DeserializeObject<WebsiteJSON>(RawJSON).KtaneModules;
        }

        _moduleIds = Modules.ToDictionary(m => m.Name, m => m.ModuleID);
        _moduleIds = Modules.ToDictionary(m => m.ModuleID, m => m.Name);
        Loaded = true;
    }

    public class WebsiteJSON
    {
        public List<KtaneModule> KtaneModules;
    }

    public class KtaneModule
    {
        public string Name, ModuleID, Quirks = "";
        public List<string> Ignore;
    }

    public static IEnumerable<string> ProcessQuirk(string q)
    {
        return Modules.Where(m => m.Quirks.Contains(q)).Select(m => m.Name);
    }

    public static string ToOther(this string module)
    {
        return _moduleIds.ContainsKey(module) ? _moduleIds[module] :
            _moduleNames.ContainsKey(module) ? _moduleNames[module] :
            null;
    }
    public static string ToId(this string module)
    {
        return _moduleIds.ContainsKey(module) ? _moduleIds[module] : module;
    }
    public static string[] ToIds(this IEnumerable<string> modules)
    {
        return modules.Select(ToId).ToArray();
    }
    public static bool NameEquals(this string a, string b)
    {
        return a.Replace("’", "'") == b.Replace("’", "'");
    }
}