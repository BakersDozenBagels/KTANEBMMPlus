using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEngine;

public class GoogleSheet : CustomYieldInstruction
{
    readonly DownloadText download;

    public GoogleSheet(string sheetID, string gid = "0")
    {
        download = new DownloadText("https://docs.google.com/spreadsheets/d/" + sheetID + "/gviz/tq?tqx=out:json&gid=" + gid);
    }

    public override bool keepWaiting { get { return download.keepWaiting; } }

    public bool Success { get { return download.Text != null; } }

    private class SheetResponse
    {
        public Table table;

        public class Table
        {
            public Column[] cols;
            public Row[] rows;
            public class Column
            {
                public string id;
            }

            public class Row
            {
                public Value[] c;

                public class Value
                {
                    public object v;
                    public string f;
                }
            }
        }
    }

    public IEnumerable<Dictionary<string, string>> GetRows()
    {
        var matches = Regex.Match(download.Text, @"google.visualization.Query.setResponse\((.+)\)");
        if (!matches.Success)
            yield break;

        var json = matches.Groups[1].Value;

        var response = JsonConvert.DeserializeObject<SheetResponse>(json);
        if (response == null)
            yield break;

        var table = response.table;
        var columns = table.cols.Select(column => Regex.Replace(column.id.ToUpperInvariant(), "[^A-Z]", "")).ToArray();

        foreach (var row in table.rows)
        {
            var dictionary = new Dictionary<string, string>();
            for (int i = 0; i < columns.Length; i++)
            {
                if (row.c[i] == null)
                    dictionary[columns[i]] = "";
                else if (row.c[i].f != null)
                    dictionary[columns[i]] = row.c[i].f;
                else if (row.c[i].v != null)
                    dictionary[columns[i]] = row.c[i].v.ToString();
                else
                    dictionary[columns[i]] = "";
            }

            yield return dictionary;
        }
    }
}