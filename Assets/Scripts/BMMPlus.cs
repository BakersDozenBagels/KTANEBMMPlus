using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// Unused members are either used by reflection or Unity
#pragma warning disable IDE0051 // Remove unused private members

[RequireComponent(typeof(KMService))]
public class BMMPlus : MonoBehaviour
{
    private static bool _harmonied, _isActive, _dataLoaded;
    private static readonly Harmony _harm =
#if UNITY_EDITOR
        null;
#else
        new Harmony("BDB.BMM+");
#endif
    private static ModuleData[] _modules;

    private void Start()
    {
        _isActive = true;
        if (_harmonied)
            return;
        StartCoroutine(DownloadData());
        StartCoroutine(PatchBMM());
        return;
    }

    private IEnumerator PatchBMM()
    {
        if (_harmonied)
            yield break;

        GameObject bmm = GameObject.Find("BossModuleManager");
        while (bmm == null)
        {
            yield return null;
            bmm = GameObject.Find("BossModuleManager");
        }

        if (_harmonied)
            yield break;
        _harmonied = true;

        var comp = bmm.GetComponent<IDictionary<string, object>>();
        var method = comp.GetType().GetMethod("GetIgnoredModules", BindingFlags.NonPublic | BindingFlags.Instance);
        _harm.Patch(method, postfix: new HarmonyMethod(typeof(BMMPlus).GetMethod("BMMPostfix", BindingFlags.NonPublic | BindingFlags.Static)));
    }
    
    private static void BMMPostfix(string moduleId, bool ids, ref string[] __result)
    {
        if (!_isActive)
            return;
        if (!_dataLoaded)
        {
            Debug.Log("[BMM++] Got a request for \"" + moduleId + "\"'s ignore list (" + (ids ? "ids" : "names") + "). Unfortuantely, I can't change that list since I don't have any data yet.");
            return;
        }

        Debug.Log("[BMM++] Intercepting request for \"" + moduleId + "\"'s ignore list (" + (ids ? "ids)." : "names)."));

        var customMod = _modules.FirstOrDefault(m => m.ID == moduleId || m.Name.NameEquals( moduleId));
        if (customMod != null)
        {
            __result = (ids ? customMod.IdIgnoreList : customMod.IgnoreList).ToArray();
            return;
        }

        if (ids && Repository.ProcessedIdIgnoreLists.ContainsKey(moduleId))
            __result = Repository.ProcessedIdIgnoreLists[moduleId].ToArray();
        else if (Repository.ProcessedIgnoreLists.ContainsKey(moduleId))
            __result = Repository.ProcessedIgnoreLists[moduleId].ToArray();
        else if (ids)
            __result = Repository.ProcessedIdIgnoreLists[moduleId] = GenerateIgnoreList(Repository.Modules.First(m => m.ModuleID == moduleId || m.Name.NameEquals( moduleId)).Ignore).ToIds().ToArray();
        else
            __result = Repository.ProcessedIgnoreLists[moduleId] = GenerateIgnoreList(Repository.Modules.First(m => m.ModuleID == moduleId || m.Name.NameEquals(moduleId)).Ignore).ToArray();
    }

    private IEnumerator DownloadData()
    {
        var repo = StartCoroutine(Repository.LoadData());
        var sheet = new GoogleSheet("18AuWZszwsNyDNgYtqV879pjEgk8WItqmsM0pL0a1OkM");

        yield return sheet;
        yield return repo;

        _dataLoaded = true;
        _modules = sheet.GetRows().Select(row => new ModuleData(row)).ToArray();

        Debug.Log("[BMM++] Repositories loaded!");
    }

    private void OnDestroy()
    {
        _isActive = false;
    }

    private sealed class ModuleData
    {
        public ModuleData(Dictionary<string, string> row)
        {
            Name = row["A"];
            Quirks = Quirks.None;
            if (row["B"] == "TRUE")
                Quirks |= Quirks.SolvesAtEnd;
            if (row["C"] == "TRUE")
                Quirks |= Quirks.NeedsOtherSolves;
            if (row["D"] == "TRUE")
                Quirks |= Quirks.MustSolveBeforeSome;
            if (row["E"] == "TRUE")
                Quirks |= Quirks.SolvesWithOthers;
            if (row["F"] == "TRUE")
                Quirks |= Quirks.WillSolveSuddenly;
            if (row["G"] == "TRUE")
                Quirks |= Quirks.PseudoNeedy;
            if (row["H"] == "TRUE")
                Quirks |= Quirks.TimeDependent;
            if (row["I"] == "TRUE")
                Quirks |= Quirks.NeedsOtherSolves;
            if (row["J"] == "TRUE")
                Quirks |= Quirks.InstantDeath;
            _rawIgnoreList = row["K"];
            ID = row["L"];
            _ignoreList = null;
        }

        public string Name, ID;
        public Quirks Quirks;
        public string[] IgnoreList
        {
            get
            {
                return _ignoreList = _ignoreList ?? GenerateIgnoreList(_rawIgnoreList.Split(';').Select(s => s.Trim()));
            }
        }
        public string[] IdIgnoreList
        {
            get
            {
                return _idIgnoreList = _idIgnoreList ?? IgnoreList.ToIds();
            }
        }
        string[] _ignoreList, _idIgnoreList;
        readonly string _rawIgnoreList;

        public bool HasQuirk(string q)
        {
            return (Quirks & (Quirks)Enum.Parse(typeof(Quirks), q)) != Quirks.None;
        }
    }

    private static string[] GenerateIgnoreList(IEnumerable<string> list)
    {
        var processed = new List<string>();
        foreach (var item in list)
        {
            if (item.Length == 0)
                processed.Add(item);
            else if (item[0] == '+' && item.Length > 1)
            {
                var q = item.Substring(1);
                processed.AddRange(Repository.ProcessQuirk(q));
                processed.AddRange(_modules.Where(m => m.HasQuirk(q)).Select(m => m.Name));
            }
            else if (item[0] == '-')
            {
                processed.RemoveAt(processed.LastIndexOf(item.Substring(1)));
            }
            else
                processed.Add(item);
        }
        return processed.ToArray();
    }

    [Flags]
    public enum Quirks : short
    {
        None = 0,
        SolvesAtEnd = 1,
        NeedsOtherSolves = 2,
        MustSolveBeforeSome = 4,
        SolvesWithOthers = 8,
        WillSolveSuddenly = 16,
        PseudoNeedy = 32,
        TimeDependent = 64,
        NeedsImmediateAttention = 128,
        InstantDeath = 256
    }
}