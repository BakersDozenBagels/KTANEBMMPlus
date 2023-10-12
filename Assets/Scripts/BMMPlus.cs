using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

[RequireComponent(typeof(KMService))]
public class BMMPlus : MonoBehaviour
{
    private static bool _harmonied, _isActive, _dataLoaded;
    private static Harmony _harm;
    private static HarmonyMethod _transpiler;
    private static MethodInfo _funcInvoke;
    private static LambdaExpression _replacement;
    private static ModuleData[] _modules;

    private void Start()
    {
        _isActive = true;
        if (_harmonied)
            return;
        StartCoroutine(DownloadData());
        _harmonied = true;
        _transpiler = new HarmonyMethod(typeof(BMMPlus).GetMethod("Transpile", BindingFlags.NonPublic | BindingFlags.Static));
        _funcInvoke = typeof(Func<string, string[]>).GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance);
        ParameterExpression p1 = Expression.Parameter(typeof(Func<string, string[]>), "original"), p2 = Expression.Parameter(typeof(string), "name"), p3 = Expression.Parameter(typeof(MonoBehaviour), "bossModule");
        _replacement = Expression.Lambda<Func<Func<string, string[]>, string, MonoBehaviour, string[]>>(Expression.Call(typeof(BMMPlus).GetMethod("GetIgnoredModules", BindingFlags.NonPublic | BindingFlags.Static), p1, p2, p3), p1, p2, p3);
        _harm = new Harmony("BakersDozenBagels.BossModuleManagerPlus");
        var methods = typeof(Assembly).GetMethods(ReflectionHelper.AllFlags);
        var method = methods.First(m => m.Name == "Load" && m.GetParameters().Select(pi => pi.ParameterType).SequenceEqual(new Type[] { typeof(byte[]) }));
        _harm.Patch(method, postfix: new HarmonyMethod(typeof(BMMPlus).GetMethod("AfterAssemblyLoad", BindingFlags.NonPublic | BindingFlags.Static)));
        foreach (var c in AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetSafeTypes()).Where(t => t.Name == "KMBossModule"))
            FixClass(c);
    }

    private IEnumerator DownloadData()
    {
        var repo = StartCoroutine(Repository.LoadData());
        var sheet = new GoogleSheet("18AuWZszwsNyDNgYtqV879pjEgk8WItqmsM0pL0a1OkM");

        yield return sheet;
        yield return repo;

        _dataLoaded = true;
        _modules = (from row in sheet.GetRows()
                    select new ModuleData(row)).ToArray();

        Debug.Log("[BMM++] Repositories loaded!");
    }

    private void OnDestroy()
    {
        _isActive = false;
    }

    private static void AfterAssemblyLoad(Assembly __result)
    {
        try
        {
            foreach (var c in __result.GetSafeTypes().Where(t => t.Name == "KMBossModule"))
                FixClass(c);
        }
        // This postfix *cannot* fail in order to maintain stability.
        catch (Exception e)
        {
            Debug.LogError("[BMM++] Fixing a KMBossModule threw an exception! Please report this error to Bagels immediately so this can be resolved. (Gameplay will most likely continue without the ignore list fetch being modified.)");
            Debug.LogError("[BMM++] The affected assmebly: " + __result.FullName);
            Debug.LogError(e.Message);
            Debug.LogError(e.StackTrace);
        }
    }

    private static void FixClass(Type c)
    {
        var methods = c.GetMethods(ReflectionHelper.AllFlags)
            .Where(mi => mi.Name == "GetIgnoredModules")
            .Where(mi => { var ps = mi.GetParameters().Select(pi => pi.ParameterType); return ps.SequenceEqual(new Type[] { typeof(KMBombModule), typeof(string[]) }) || ps.SequenceEqual(new Type[] { typeof(string), typeof(string[]) }); });
        foreach (var m in methods)
            FixMethod(m);
    }

    private static void FixMethod(MethodInfo m)
    {
        _harm.Patch(m, transpiler: _transpiler);
    }

    private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> il)
    {
        foreach (var i in il)
        {
            if (i.Calls(_funcInvoke))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return CodeInstruction.Call(_replacement);
            }
            else
                yield return i;
        }
    }

    private static string[] GetIgnoredModules(Func<string, string[]> original, string name, MonoBehaviour bossModule)
    {
        if (!_isActive)
            return original(name);

        var customMod = _modules.FirstOrDefault(m => m.Name == name);
        if (customMod != null)
            return customMod.IgnoreList;

        if (Repository.ProcessedIgnoreLists.ContainsKey(name))
            return Repository.ProcessedIgnoreLists[name];
        else
            return Repository.ProcessedIgnoreLists[name] = GenerateIgnoreList(Repository.Modules.FirstOrDefault(m => m.Name == name).Ignore);
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
            _ignoreList = null;
        }

        public string Name;
        public Quirks Quirks;
        public string[] IgnoreList
        {
            get
            {
                if (_ignoreList == null)
                {
                    _ignoreList = GenerateIgnoreList(_rawIgnoreList.Split(';').Select(s => s.Trim()));
                }
                return _ignoreList;
            }
        }
        string[] _ignoreList;
        string _rawIgnoreList;

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
            if(item.Length == 0)
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